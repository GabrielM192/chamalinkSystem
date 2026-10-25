using ChamaLink.Domain.Localization;
using System.Security.Cryptography;
using System.Text.Json;
using ChamaLink.Application.Interfaces;
using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Exceptions;
using ChamaLink.Domain.Ledger;
using ChamaLink.Infrastructure.Services.Benefits;
using ChamaLink.Infrastructure.Services.Common;
using ChamaLink.Infrastructure.Services.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ChamaLink.Infrastructure.Services.Truth;

/// <summary>
/// SASISHO17 (P0-2, P1-7, P1-8, P1-9): PDF ya M-Koba — chanzo kikuu cha ukweli wa fedha — haiingii tena moja kwa moja.
///
///   1. KUPAKIA (kiongozi A): ukubwa/kurasa vinakaguliwa, faili linahifadhiwa likiwa LIMESIMBWA (forensics), safu zinasomwa
///      na kulinganishwa na data iliyopo: MPYA / ZINAZOFANANA / MIGONGANO (rejea ile ile, data tofauti — inakataliwa kabisa),
///      safu za NYUMA (tarehe kabla ya muamala wa mwisho uliopo), mapengo mapya/yaliyozibwa, namba zisizojulikana.
///      Hakuna kinachoingia kwenye MkobaRawTransactions. Rasimu moja tu kwa kikundi.
///   2. KUIDHINISHA (kiongozi B, tofauti na A): faili linasomwa UPYA kutoka hifadhi, hash inahakikiwa, safu zinalinganishwa
///      UPYA na hali ya sasa (si JSON iliyohifadhiwa) — kisha zinaingizwa zote ndani ya transaction MOJA, zikiwa na ImportId.
///   3. A anaweza kughairi; B anaweza kukataa (kwa sababu). Kila hatua → AuditEvent + arifa.
/// </summary>
public class StatementImportService
{
    private const long DefaultMaxUploadBytes = 10 * 1024 * 1024;
    private const int DefaultMaxPages = 200;
    private const int DefaultPendingTtlHours = 72;
    private const int MaxListedConflicts = 10;
    private const int CopyBufferBytes = 80 * 1024;
    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    private readonly ApplicationDbContext _context;
    private readonly IMKobaParserService _parser;
    private readonly IEvidenceStorage _storage;
    private readonly TruthSetupService _setup;
    private readonly NotificationService _notify;
    private readonly IConfiguration _config;
    private readonly EventWalletService _wallet;

    private readonly ChamaLink.Infrastructure.Services.Events.PaymentInboxService? _inbox;

    public StatementImportService(ApplicationDbContext context, IMKobaParserService parser, IEvidenceStorage storage, TruthSetupService setup,
        NotificationService notify, IConfiguration config, EventWalletService wallet,
        // SASISHO21: sanduku la malipo linajazwa yenyewe mara statement inapoidhinishwa.
        // (Hiari ili majaribio ya zamani yasivunjike.)
        ChamaLink.Infrastructure.Services.Events.PaymentInboxService? inbox = null)
    {
        _context = context; _parser = parser; _storage = storage; _setup = setup; _notify = notify; _config = config; _wallet = wallet;
        _inbox = inbox;
    }

    public long MaxUploadBytes => long.TryParse(_config["Mkoba:MaxUploadBytes"], out var v) && v > 0 ? v : DefaultMaxUploadBytes;
    private int MaxPages => int.TryParse(_config["Mkoba:MaxPages"], out var v) && v > 0 ? v : DefaultMaxPages;
    private int PendingTtlHours => int.TryParse(_config["Mkoba:PendingImportTtlHours"], out var v) && v > 0 ? v : DefaultPendingTtlHours;

    public sealed record StagedRow(string Reference, DateTime Timestamp, string Phone, string PayerName, decimal Amount, decimal? Balance,
        bool IsWithdrawal, string Description, int RowOrder);

    private sealed record Classified(List<StagedRow> New, int Identical, List<object> Conflicts, int Backdated, List<Loc> Warnings, List<string> Unreadable,
        decimal? DeclaredContributionTotal, decimal? DeclaredGroupBalance, decimal? LastBalance, DateTime? From, DateTime? To, int RowsInPdf,
        decimal Deposits, decimal Withdrawals);

    // ------------------------------------------------------------------ msaada
    private async Task<byte[]> ReadBoundedAsync(Stream s, CancellationToken ct)
    {
        var max = MaxUploadBytes;
        using var ms = new MemoryStream();
        var buf = new byte[CopyBufferBytes];
        int n;
        while ((n = await s.ReadAsync(buf, ct)) > 0)
        {
            if (ms.Length + n > max) throw new ValidationException($"Faili ni kubwa mno (kikomo ni MB {max / (1024 * 1024)}).");
            ms.Write(buf, 0, n);
        }
        return ms.ToArray();
    }

    private void EnsurePdfShape(byte[] bytes)
    {
        if (bytes.Length < PdfMagic.Length || !bytes.AsSpan(0, PdfMagic.Length).SequenceEqual(PdfMagic))
            throw new ValidationException("Faili si PDF. Pakia taarifa (statement) ya M-Koba katika PDF.");
        int pages;
        try { pages = CountPages(bytes); }
        catch (Exception ex) when (ex is not ValidationException) { throw new ValidationException("Faili si PDF halali ya M-Koba."); }
        if (pages > MaxPages) throw new ValidationException($"PDF ina kurasa {pages}; kikomo ni {MaxPages}. Gawanya taarifa kwa vipindi.");
    }

    /// <summary>Idadi ya kurasa (PdfPig). Virtual ili majaribio yaweze kutumia PDF bandia bila kudhoofisha ukaguzi wa production.</summary>
    protected virtual int CountPages(byte[] bytes)
    {
        using var doc = UglyToad.PdfPig.PdfDocument.Open(bytes);
        return doc.NumberOfPages;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static bool Same(MkobaRawTransaction e, StagedRow r) =>
        e.Timestamp == r.Timestamp && e.Amount == r.Amount && e.IsWithdrawal == r.IsWithdrawal && e.Balance == r.Balance
        && string.Equals(e.Phone, r.Phone, StringComparison.Ordinal);

    /// <summary>Soma PDF na ulinganishe na data ILIYOPO sasa. Inatumika wakati wa kupakia NA tena wakati wa kuidhinisha.</summary>
    private async Task<Classified> ClassifyAsync(Guid groupId, byte[] bytes, CancellationToken ct)
    {
        using var ms = new MemoryStream(bytes, writable: false);
        var parsed = await _parser.ParseStatementDetailedAsync(ms);
        if (parsed.Transactions.Count == 0) throw new ConflictException("PDF haina miamala inayosomeka.");
        var warnings = new List<Loc>();   // SASISHO17: kiolezo + vigeu → EN haionyeshi Kiswahili
        var rows = new List<StagedRow>();
        var seenInPdf = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in parsed.Transactions)
        {
            var reference = t.ReferenceNumber.Trim().ToUpperInvariant();
            if (!seenInPdf.Add(reference)) { warnings.Add($"{reference}: rejea imejirudia ndani ya PDF hii — mstari wa pili umepuuzwa."); continue; }
            string phone;
            try { phone = PhoneNumbers.Normalize(t.PhoneNumber); }
            catch (ArgumentException) { phone = t.PhoneNumber; warnings.Add($"{reference}: namba ya simu '{t.PhoneNumber}' haieleweki."); }
            rows.Add(new StagedRow(reference, DateTime.SpecifyKind(t.TransactionDate, DateTimeKind.Utc), phone, t.MemberName ?? string.Empty, t.Amount, t.Balance,
                t.IsWithdrawal, t.Description ?? string.Empty, t.RowOrder));
        }

        var refs = rows.Select(r => r.Reference).ToList();
        var existing = (await _context.MkobaRawTransactions.AsNoTracking().Where(x => x.GroupId == groupId && refs.Contains(x.Reference)).ToListAsync(ct))
            .ToDictionary(x => x.Reference, StringComparer.OrdinalIgnoreCase);
        var latest = await _context.MkobaRawTransactions.AsNoTracking().Where(x => x.GroupId == groupId)
            .OrderByDescending(x => x.Timestamp).Select(x => (DateTime?)x.Timestamp).FirstOrDefaultAsync(ct);

        var fresh = new List<StagedRow>();
        var conflicts = new List<object>();
        var identical = 0;
        foreach (var r in rows)
        {
            if (existing.TryGetValue(r.Reference, out var e))
            {
                if (Same(e, r)) identical++;
                else conflicts.Add(new
                {
                    reference = r.Reference,
                    existing = new { e.Timestamp, e.Amount, e.Balance, e.Phone, e.IsWithdrawal },
                    inPdf = new { r.Timestamp, r.Amount, r.Balance, r.Phone, r.IsWithdrawal }
                });
            }
            else fresh.Add(r);
        }
        var backdated = latest is { } lt ? fresh.Count(r => r.Timestamp < lt) : 0;
        if (backdated > 0)
            warnings.Add($"Safu {backdated} mpya zina tarehe KABLA ya muamala wa mwisho uliopo ({latest:dd/MM/yyyy}). Hakiki kwamba ni miamala iliyokosekana awali, si iliyobuniwa.");

        // Mnyororo wa balance NDANI ya PDF hii pekee
        var inner = StatementIntegrity.FindGaps(rows.Select(r => new StatementIntegrity.Row(r.Reference, r.Timestamp, r.Amount, r.IsWithdrawal, r.Balance, r.RowOrder)));
        if (inner.Count > 0)
            warnings.Add($"Mnyororo wa balance ndani ya PDF hii unaruka mara {inner.Count} (mf. baada ya {inner[0].AfterReference ?? "mwanzo"}). Hakiki PDF ni kamili.");

        var deposits = rows.Where(r => !r.IsWithdrawal).Sum(r => r.Amount);
        var withdrawals = rows.Where(r => r.IsWithdrawal).Sum(r => r.Amount);
        var last = rows.OrderBy(r => r.Timestamp).ThenBy(r => r.RowOrder).LastOrDefault()?.Balance;
        if (parsed.UnreadableRows.Count > 0) warnings.Add($"Mistari {parsed.UnreadableRows.Count} haikusomeka — angalia orodha. HAIJARUKWA KIMYA.");
        if (parsed.DeclaredGroupBalance is { } gb && last is { } lb && gb != lb)
            warnings.Add($"Balance ya mwisho kwenye mistari ({lb:N0}) ≠ 'Group Balance' ya PDF ({gb:N0}).");

        return new Classified(fresh, identical, conflicts, backdated, warnings, parsed.UnreadableRows, parsed.DeclaredContributionTotal, parsed.DeclaredGroupBalance,
            last, rows.Min(r => (DateTime?)r.Timestamp), rows.Max(r => (DateTime?)r.Timestamp), rows.Count, deposits, withdrawals);
    }

    /// <summary>Hakiki ya mapengo: data iliyopo + safu mpya → mapengo mapya na yanayozibwa (bila kuandika kitu).</summary>
    private async Task<object> GapPreviewAsync(Guid groupId, List<StagedRow> fresh, CancellationToken ct)
    {
        var cur = await _context.MkobaRawTransactions.AsNoTracking().Where(t => t.GroupId == groupId)
            .Select(t => new StatementIntegrity.Row(t.Reference, t.Timestamp, t.Amount, t.IsWithdrawal, t.Balance, t.RowOrder)).ToListAsync(ct);
        var after = StatementIntegrity.FindGaps(cur.Concat(fresh.Select(r => new StatementIntegrity.Row(r.Reference, r.Timestamp, r.Amount, r.IsWithdrawal, r.Balance, r.RowOrder))));
        var open = await _context.MkobaStatementGaps.AsNoTracking().Where(g => g.GroupId == groupId && g.ResolutionStatus != TruthApprovalStatus.Approved)
            .Select(g => g.BeforeReference).ToListAsync(ct);
        var afterRefs = after.Select(g => g.BeforeReference).ToHashSet();
        return new
        {
            newGaps = after.Where(g => !open.Contains(g.BeforeReference)).Select(g => new { g.AfterReference, g.BeforeReference, g.Amount, g.WindowStart, g.WindowEnd }).ToList(),
            closedGaps = open.Where(r => !afterRefs.Contains(r)).ToList(),
            totalGapsAfter = after.Count
        };
    }

    private async Task<List<object>> UnknownPhonesAsync(Guid groupId, List<StagedRow> fresh, CancellationToken ct)
    {
        var known = (await _context.MemberPhones.AsNoTracking().Where(p => p.GroupId == groupId && p.Status != TruthApprovalStatus.Rejected)
            .Select(p => p.Phone).ToListAsync(ct)).ToHashSet();
        return fresh.Where(r => !r.IsWithdrawal && !known.Contains(r.Phone)).GroupBy(r => r.Phone)
            .Select(g => (object)new { phone = g.Key, mkobaName = g.Max(x => x.PayerName), rows = g.Count(), total = g.Sum(x => x.Amount) }).ToList();
    }

    // ------------------------------------------------------------------ 1. kupakia (rasimu)
    public async Task<object> StageAsync(Guid groupId, Stream pdf, string? fileName, Guid userId, CancellationToken ct = default)
    {
        var actor = await MemberAccess.RequireEffectiveLeaderAsync(_context, groupId, userId, ct, "Kupakia PDF ya M-Koba ni kazi ya viongozi.");
        var bytes = await ReadBoundedAsync(pdf, ct);
        EnsurePdfShape(bytes);
        var hash = Hash(bytes);
        var c = await ClassifyAsync(groupId, bytes, ct);

        if (c.Conflicts.Count > 0)
        {
            // Rejea ile ile ikiwa na data tofauti = PDF imebadilishwa au ni ya kikundi kingine. Haiingii kamwe; inarekodiwa.
            await AuditTrail.AddAsync(_context, groupId, actor.Id, userId, AuditAction.RiskBlocked, nameof(MkobaStatementImport), Guid.Empty, null,
                new { Code = "STATEMENT_CONFLICT", fileHash = hash, conflicts = c.Conflicts.Take(MaxListedConflicts) }, hash,
                "PDF ina miamala yenye rejea zilizopo lakini data tofauti", ct);
            await _context.SaveChangesAsync(ct);
            throw new ConflictException($"PDF hii ina miamala {c.Conflicts.Count} yenye rejea zilizopo tayari lakini data TOFAUTI (tarehe/kiasi/salio/namba). " +
                                        $"Haiwezi kuingizwa. Pakua PDF halisi kutoka M-Koba tena; jaribio hili limerekodiwa.");
        }
        if (c.New.Count == 0) throw new ConflictException($"Hakuna muamala mpya: safu zote {c.Identical} tayari zipo.");

        var gapPreview = await GapPreviewAsync(groupId, c.New, ct);
        var unknown = await UnknownPhonesAsync(groupId, c.New, ct);
        var storageKey = await _storage.SaveAsync(groupId, hash, ".pdf", bytes, ct);

        return await DbTx.SerializedAsync(_context, groupId, DbTx.StatementImport, async () =>
        {
            if (await _context.MkobaStatementImports.AnyAsync(x => x.GroupId == groupId && x.Status == StatementImportStatus.Pending, ct))
                throw new ConflictException("Tayari kuna PDF ya M-Koba inayosubiri idhini. Iidhinishwe, ikataliwe au ighairiwe kwanza.");
            var imp = new MkobaStatementImport
            {
                Id = Guid.NewGuid(), GroupId = groupId, FileHash = hash, FileName = SafeName(fileName), FileSize = bytes.Length, StorageKey = storageKey,
                UploadedByUserId = userId, RowsInPdf = c.RowsInPdf, NewRows = c.New.Count, ExistingRows = c.Identical, ConflictingRows = 0,
                BackdatedRows = c.Backdated, PeriodFrom = c.From, PeriodTo = c.To, LastBalance = c.LastBalance,
                RowsJson = JsonSerializer.Serialize(c.New),
                ReportJson = JsonSerializer.Serialize(new
                {
                    deposits = c.Deposits, withdrawals = c.Withdrawals, declaredContributionTotal = c.DeclaredContributionTotal,
                    declaredGroupBalance = c.DeclaredGroupBalance, warnings = c.Warnings.Select(w => w.Text).ToList(), warningsLoc = c.Warnings.Select(w => w.ToMessage()).ToList(), unreadableRows = c.Unreadable, unknownPhones = unknown, gaps = gapPreview
                })
            };
            _context.MkobaStatementImports.Add(imp);
            await AuditTrail.AddAsync(_context, groupId, actor.Id, userId, AuditAction.StatementImportStaged, nameof(MkobaStatementImport), imp.Id, null,
                new { fileHash = hash, imp.RowsInPdf, imp.NewRows, imp.ExistingRows, imp.BackdatedRows, imp.LastBalance }, hash, "PDF ya M-Koba imepakiwa (inasubiri kiongozi wa pili)", ct);
            var others = (await _context.GroupMembers.AsNoTracking().Where(m => m.GroupId == groupId && MemberAccess.LeaderRoles.Contains(m.Role) && m.UserId != userId
                    && MemberAccess.ActiveStatuses.Contains(m.Status)).ToListAsync(ct)).Where(m => MemberAccess.IsEffectiveLeader(m)).Select(m => m.Id).ToList();
            await _notify.NotifyAsync(groupId, others, "StatementPending", $"PDF ya M-Koba inasubiri idhini",
                $"Safu mpya {imp.NewRows} ({imp.PeriodFrom:dd/MM/yyyy}–{imp.PeriodTo:dd/MM/yyyy}), salio la mwisho {imp.LastBalance:N0}. Hakiki kisha idhinisha au kataa.",
                "/ukweli/import", ct);
            await _context.SaveChangesAsync(ct);
            return Detail(imp, userId);
        }, ct);
    }

    private static string? SafeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var n = Path.GetFileName(name).Trim();
        n = new string(n.Where(ch => !char.IsControl(ch)).ToArray());
        return n.Length > 200 ? n[..200] : n;
    }

    // ------------------------------------------------------------------ 2. kuidhinisha
    /// <summary>Matokeo ya idhini: rasimu (kama ilivyo kwenye orodha) + ripoti kamili ya import (mapengo yaliyoelezwa, maonyo, namba zisizojulikana).</summary>
    public sealed record ApprovalResult(object Import, TruthSetupService.ImportReport Report);

    public async Task<ApprovalResult> ApproveAsync(Guid groupId, Guid importId, string? note, Guid userId, CancellationToken ct = default)
    {
        var actor = await MemberAccess.RequireEffectiveLeaderAsync(_context, groupId, userId, ct, "Kuidhinisha PDF ya M-Koba ni kazi ya viongozi.");
        // Kufuli: rasimu (StatementImport) + ahadi za pesa (CashCommitment) — salio jipya haliwezi kuingia katikati ya idhini ya malipo
        return await DbTx.SerializedAsync(_context, groupId, new[] { DbTx.CashCommitment, DbTx.StatementImport }, async () =>
        {
            var imp = await _context.MkobaStatementImports.FirstOrDefaultAsync(x => x.Id == importId && x.GroupId == groupId, ct)
                      ?? throw new NotFoundException("Rasimu ya PDF haipo.");
            if (imp.Status != StatementImportStatus.Pending) throw new ConflictException($"Rasimu hii tayari ni {imp.Status}.");
            if (imp.UploadedByUserId == userId) throw new ForbiddenException("Aliyepakia PDF HAWEZI kuiidhinisha — kiongozi wa pili anahitajika.");
            if (imp.UploadedAt.AddHours(PendingTtlHours) < DateTime.UtcNow)
                throw new ConflictException("Rasimu hii imekaa muda mrefu bila idhini. Ikatae, kisha pakia PDF ya karibuni.");

            // Soma faili UPYA na hakiki hash — RowsJson haiaminiwi
            var bytes = await _storage.ReadAsync(imp.StorageKey ?? throw new ConflictException("Faili la rasimu halipatikani."), ct);
            if (!string.Equals(Hash(bytes), imp.FileHash, StringComparison.OrdinalIgnoreCase))
                throw new ConflictException("Faili la rasimu limebadilika baada ya kupakiwa. Likatae na upakie upya.");
            var c = await ClassifyAsync(groupId, bytes, ct);
            if (c.Conflicts.Count > 0) throw new ConflictException("Tangu rasimu ipakiwe, data iliyopo imebadilika na sasa inagongana na PDF hii. Ikatae na upakie upya.");
            var unknown = await UnknownPhonesAsync(groupId, c.New, ct);

            foreach (var r in c.New)
                _context.MkobaRawTransactions.Add(new MkobaRawTransaction
                {
                    Id = Guid.NewGuid(), GroupId = groupId, Reference = r.Reference, Timestamp = r.Timestamp, Phone = r.Phone, PayerName = r.PayerName,
                    Amount = r.Amount, Balance = r.Balance, IsWithdrawal = r.IsWithdrawal, Description = r.Description, RowOrder = r.RowOrder,
                    StatementFileHash = imp.FileHash, ImportedByUserId = imp.UploadedByUserId, ImportId = imp.Id
                });
            imp.Status = StatementImportStatus.Approved; imp.DecidedByUserId = userId; imp.DecidedAt = DateTime.UtcNow;
            imp.DecisionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim()[..Math.Min(note.Trim().Length, 500)];
            await _context.SaveChangesAsync(ct);

            // SASISHO18: kikundi cha "kila tukio" — amana mpya inalipa deni la ZAMANI la mwanachama
            // (deni la zamani kwanza), juu ya floor pekee. Kikundi cha mwezi: hakuna kinachotokea.
            if (c.New.Count > 0)
                await _wallet.SweepDepositsAsync(groupId, c.New.Select(r => r.Phone), ct);

            // SASISHO21: mistari mipya inaingia kwenye sanduku la malipo (kodi / namba / tupu).
            if (c.New.Count > 0 && _inbox is not null)
                await _inbox.SyncAsync(groupId, ct);

            // Namba zilizoonekana + mapengo juu ya data YOTE
            var phonesSeen = await _context.MkobaRawTransactions.Where(t => t.GroupId == groupId)
                .GroupBy(t => t.Phone).Select(g => new { Phone = g.Key, Name = g.Max(x => x.PayerName) }).ToListAsync(ct);
            foreach (var mp in await _context.MemberPhones.Where(p => p.GroupId == groupId).ToListAsync(ct))
            {
                var seen = phonesSeen.FirstOrDefault(p => p.Phone == mp.Phone);
                if (seen is not null) { mp.MatchedInMkoba = true; mp.MkobaNameSeen = seen.Name; }
            }
            var gaps = await _setup.RecomputeGapsAsync(groupId, ct);
            await AuditTrail.AddAsync(_context, groupId, actor.Id, userId, AuditAction.StatementImportApproved, nameof(MkobaStatementImport), imp.Id, null,
                new { imp.FileHash, inserted = c.New.Count, alreadyExisted = c.Identical, uploadedBy = imp.UploadedByUserId, gaps = gaps.Count }, imp.FileHash, imp.DecisionNote, ct);
            await AuditTrail.AddAsync(_context, groupId, null, userId, AuditAction.ImportCompleted, "MkobaStatement", groupId, null,
                new { fileHash = imp.FileHash, rowsInPdf = c.RowsInPdf, inserted = c.New.Count, alreadyExisted = c.Identical, unreadable = c.Unreadable.Count, gaps = gaps.Count, importId = imp.Id },
                imp.FileHash, "Import ya PDF ya M-Koba (imeidhinishwa na kiongozi wa pili)", ct);
            await _notify.NotifyAllMembersAsync(groupId, "StatementImported", $"Taarifa ya M-Koba imesasishwa",
                $"Miamala mipya {c.New.Count} hadi {c.To:dd/MM/yyyy} imeingizwa (salio {c.LastBalance:N0}). Angalia taarifa yako.", "/", ct);
            imp.NewRows = c.New.Count; imp.ExistingRows = c.Identical;
            await _context.SaveChangesAsync(ct);

            var described = await StatementGapDescriber.DescribeAsync(_context, groupId, gaps, ct);
            var warnings = new List<Loc>(c.Warnings);
            var openGaps = gaps.Where(g => g.ResolutionStatus != TruthApprovalStatus.Approved).ToList();
            if (openGaps.Count > 0)
            {
                var inflow = openGaps.Where(g => g.Amount > 0).Sum(g => g.Amount);
                var outflow = openGaps.Where(g => g.Amount < 0).Sum(g => -g.Amount);
                // Kiolezo kamili kwa kila hali (si string.Join wala Loc ndani ya Loc) ili kila neno litafsirike
                var n = openGaps.Count;
                warnings.Insert(0, inflow > 0 && outflow > 0
                    ? Loc.L($"Mnyororo wa balance umeruka mara {n}: {inflow:N0} ziliingia na {outflow:N0} zilitoka bila mstari kwenye PDF. " +
                            $"Miamala ya kabla na baada ya kila pengo ni sahihi; pesa ya pengo HAIHESABIWI kwa mtu yeyote mpaka " +
                                   $"mwenyewe atambuliwe kwa ushahidi (tabo ya Mapengo ya PDF).")
                    : inflow > 0
                        ? Loc.L($"Mnyororo wa balance umeruka mara {n}: {inflow:N0} ziliingia bila mstari kwenye PDF. " +
                                $"Miamala ya kabla na baada ya kila pengo ni sahihi; pesa ya pengo HAIHESABIWI kwa mtu yeyote mpaka " +
                                   $"mwenyewe atambuliwe kwa ushahidi (tabo ya Mapengo ya PDF).")
                        : Loc.L($"Mnyororo wa balance umeruka mara {n}: {outflow:N0} zilitoka bila mstari kwenye PDF. " +
                                $"Miamala ya kabla na baada ya kila pengo ni sahihi; pesa ya pengo HAIHESABIWI kwa mtu yeyote mpaka " +
                                   $"mwenyewe atambuliwe kwa ushahidi (tabo ya Mapengo ya PDF)."));
            }
            var report = new TruthSetupService.ImportReport(imp.FileHash, c.RowsInPdf, c.New.Count, c.Identical, c.Unreadable,
                described.Select(d => (object)new
                {
                    d.Id, d.AfterReference, d.BeforeReference, d.WindowStart, d.WindowEnd, d.Amount, d.Previous, d.Next,
                    d.BalanceAfterPrevious, d.ExpectedBalanceBeforeNext, d.Title, maelezo = d.Message,
                    resolved = gaps.First(g => g.Id == d.Id).ResolutionStatus == TruthApprovalStatus.Approved
                }).ToList(),
                c.Deposits, c.Withdrawals, c.DeclaredContributionTotal, c.DeclaredGroupBalance, c.LastBalance, unknown,
                warnings.Select(w => w.Text).ToList(), warnings.Select(w => w.ToMessage()).ToList());
            return new ApprovalResult(Detail(imp, userId), report);
        }, ct);
    }

    public async Task RejectAsync(Guid groupId, Guid importId, string? note, Guid userId, CancellationToken ct = default)
    {
        var actor = await MemberAccess.RequireEffectiveLeaderAsync(_context, groupId, userId, ct);
        await DbTx.SerializedAsync(_context, groupId, DbTx.StatementImport, async () =>
        {
            var imp = await _context.MkobaStatementImports.FirstOrDefaultAsync(x => x.Id == importId && x.GroupId == groupId, ct)
                      ?? throw new NotFoundException("Rasimu ya PDF haipo.");
            if (imp.Status != StatementImportStatus.Pending) throw new ConflictException($"Rasimu hii tayari ni {imp.Status}.");
            var mine = imp.UploadedByUserId == userId;
            if (!mine && string.IsNullOrWhiteSpace(note)) throw new ValidationException("Andika sababu ya kukataa (aliyepakia ataiona).");
            imp.Status = mine ? StatementImportStatus.Cancelled : StatementImportStatus.Rejected;
            imp.DecidedByUserId = userId; imp.DecidedAt = DateTime.UtcNow; imp.DecisionNote = note?.Trim()[..Math.Min(note.Trim().Length, 500)];
            await AuditTrail.AddAsync(_context, groupId, actor.Id, userId, mine ? AuditAction.StatementImportCancelled : AuditAction.StatementImportRejected,
                nameof(MkobaStatementImport), imp.Id, null, new { imp.FileHash, status = imp.Status.ToString() }, imp.FileHash, imp.DecisionNote, ct);
            await _context.SaveChangesAsync(ct);
            return true;
        }, ct);
    }

    // ------------------------------------------------------------------ kusoma
    private static object Detail(MkobaStatementImport i, Guid viewer) => new
    {
        i.Id, status = i.Status.ToString(), i.FileHash, i.FileName, i.FileSize, i.UploadedAt, i.DecidedAt, i.DecisionNote,
        i.RowsInPdf, i.NewRows, i.ExistingRows, i.ConflictingRows, i.BackdatedRows, i.PeriodFrom, i.PeriodTo, i.LastBalance,
        mine = i.UploadedByUserId == viewer,
        canApprove = i.Status == StatementImportStatus.Pending && i.UploadedByUserId != viewer,
        canCancel = i.Status == StatementImportStatus.Pending && i.UploadedByUserId == viewer,
        report = JsonSerializer.Deserialize<JsonElement>(i.ReportJson),
        rows = JsonSerializer.Deserialize<List<StagedRow>>(i.RowsJson)
    };

    public async Task<List<object>> ListAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        await MemberAccess.RequireEffectiveLeaderAsync(_context, groupId, userId, ct, tracking: false);
        var list = await _context.MkobaStatementImports.AsNoTracking().Where(x => x.GroupId == groupId).OrderByDescending(x => x.UploadedAt).Take(50).ToListAsync(ct);
        return list.Select(i => (object)new
        {
            i.Id, status = i.Status.ToString(), i.FileName, i.UploadedAt, i.DecidedAt, i.NewRows, i.ExistingRows, i.BackdatedRows, i.PeriodFrom, i.PeriodTo, i.LastBalance,
            mine = i.UploadedByUserId == userId, canApprove = i.Status == StatementImportStatus.Pending && i.UploadedByUserId != userId,
            canCancel = i.Status == StatementImportStatus.Pending && i.UploadedByUserId == userId
        }).ToList();
    }

    public async Task<object> GetAsync(Guid groupId, Guid importId, Guid userId, CancellationToken ct = default)
    {
        await MemberAccess.RequireEffectiveLeaderAsync(_context, groupId, userId, ct, tracking: false);
        var i = await _context.MkobaStatementImports.AsNoTracking().FirstOrDefaultAsync(x => x.Id == importId && x.GroupId == groupId, ct)
                ?? throw new NotFoundException("Rasimu ya PDF haipo.");
        return Detail(i, userId);
    }

    public async Task<(byte[] Bytes, string Name)> FileAsync(Guid groupId, Guid importId, Guid userId, CancellationToken ct = default)
    {
        await MemberAccess.RequireEffectiveLeaderAsync(_context, groupId, userId, ct, tracking: false);
        var i = await _context.MkobaStatementImports.AsNoTracking().FirstOrDefaultAsync(x => x.Id == importId && x.GroupId == groupId, ct)
                ?? throw new NotFoundException("Rasimu ya PDF haipo.");
        var bytes = await _storage.ReadAsync(i.StorageKey ?? throw new NotFoundException("Faili halipatikani."), ct);
        return (bytes, $"mkoba-{i.PeriodFrom:yyyyMMdd}-{i.PeriodTo:yyyyMMdd}.pdf");
    }
}
