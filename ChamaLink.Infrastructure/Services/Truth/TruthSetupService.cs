using ChamaLink.Domain.Exceptions;
using System.Security.Cryptography;
using ChamaLink.Application.Interfaces;
using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Ledger;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Infrastructure.Services.Truth;

/// <summary>
/// UPANDE WA KUANDIKA wa Injini ya Ukweli — kila kitu kinachobadilisha "ukweli" kiko hapa,
/// na kila kinachotoka kwa binadamu (manual, msamaha, pengo) kina maker-checker:
/// aliyeomba HAWEZI kuidhinisha ombi lake mwenyewe.
/// </summary>
public class TruthSetupService
{
    private readonly ApplicationDbContext _context;
    private readonly IMKobaParserService _parser;
    private readonly TruthLedgerService _truth;
    public TruthSetupService(ApplicationDbContext context, IMKobaParserService parser, TruthLedgerService truth)
    {
        _context = context;
        _parser = parser;
        _truth = truth;
    }

    // =====================================================================
    // 0. KUMBUKUMBU ZA UKAGUZI (AuditEvents)
    // Kila kitendo kinachobadilisha ukweli wa fedha kinaandika AuditEvent ndani ya SaveChanges ILE ILE
    // ya kitendo chenyewe: kama kitendo kimehifadhiwa, kumbukumbu yake imehifadhiwa — hakuna moja bila jingine.
    // AuditEvents HAZIFUTWI kamwe (hata na "futa data za majaribio").
    // =====================================================================
    private Task AddAuditAsync(Guid groupId, Guid? memberId, Guid actorUserId, AuditAction action,
        string entityType, Guid entityId, object? before, object after, string? sourceReference, string? reason, CancellationToken ct)
        => AuditTrail.AddAsync(_context, groupId, memberId, actorUserId, action, entityType, entityId, before, after, sourceReference, reason, ct);

    private static object Snap(TruthGroupSettings t) => new
    {
        t.MonthlyContribution, t.LateFine, t.JoiningFee, t.DueDateDay, t.GracePeriodDays, t.FineOnPartialPayment,
        t.AutoDeductSavings, t.ContributionStartDate, t.FoundingMembersCutoff, t.ManualEntryCutoff, t.LoanLatePenaltyAmount
    };
    private static object Snap(FinancialEvent e) => new
    {
        e.Type, e.Amount, e.OccurredAt, Status = e.Status.ToString(), e.Reason, EvidenceType = e.EvidenceType?.ToString(),
        e.EvidenceReference, e.CreatedBy, e.ApprovedByGroupMemberId, e.ApprovedAt, e.ApprovalNote
    };
    private static object Snap(TruthAdjustmentRecord a) => new
    {
        Kind = a.Kind.ToString(), a.GroupMemberId, a.Year, a.Month, a.ExtraDays, a.Reason, Status = a.Status.ToString(),
        a.RequestedByUserId, a.DecidedByUserId, a.DecidedAt
    };
    private static object Snap(MkobaStatementGap g) => new
    {
        g.AfterReference, g.BeforeReference, g.Amount, g.AssignedGroupMemberId, g.Evidence, g.AssumedTimestamp,
        Status = g.ResolutionStatus?.ToString(), g.RequestedByUserId, g.ApprovedByUserId, g.ApprovedAt
    };

    // =====================================================================
    // 1. MIPANGILIO YA KIKUNDI (hakuna hardcode)
    // =====================================================================
    public sealed class SettingsDto
    {
        public DateTime? ContributionStartDate { get; set; }
        public int? DueDateDay { get; set; }
        public int? GracePeriodDays { get; set; }
        public bool? FineOnPartialPayment { get; set; }
        public bool? AutoDeductSavings { get; set; }
        public DateTime? ManualEntryCutoff { get; set; }
        public DateTime? FoundingMembersCutoff { get; set; }
        public decimal? MonthlyContribution { get; set; }
        public decimal? LateFine { get; set; }
        public decimal? JoiningFee { get; set; }
        public decimal? LoanLatePenaltyAmount { get; set; }
    }

    public async Task<TruthGroupSettings> UpdateSettingsAsync(Guid groupId, SettingsDto dto, Guid userId, CancellationToken ct = default)
    {
        if (!await _context.Groups.AnyAsync(g => g.Id == groupId, ct)) throw new KeyNotFoundException("Kikundi hakipo.");
        var tracked = await _context.TruthGroupSettings.FirstOrDefaultAsync(s => s.GroupId == groupId, ct);
        var before = tracked is null ? null : Snap(tracked);
        if (tracked is null)
        {
            // Mara ya kwanza: KILA sheria ya fedha lazima itoke kwa viongozi — hakuna default ya kiasi/siku.
            var missing = new List<string>();
            if (dto.ContributionStartDate is null) missing.Add("mwezi wa kwanza wa michango (ContributionStartDate)");
            if (dto.MonthlyContribution is null) missing.Add("mchango wa mwezi (MonthlyContribution)");
            if (dto.LateFine is null) missing.Add("faini ya kuchelewa (LateFine; 0 kama hakuna)");
            if (dto.JoiningFee is null) missing.Add("Kianzio (JoiningFee; 0 kama hakuna)");
            if (dto.DueDateDay is null) missing.Add("tarehe ya mwisho ya mwezi unaofuata (DueDateDay)");
            if (dto.GracePeriodDays is null) missing.Add("siku za neema (GracePeriodDays; 0 kama hakuna)");
            if (dto.FineOnPartialPayment is null) missing.Add("faini kwa malipo pungufu ndiyo/hapana (FineOnPartialPayment)");
            if (dto.AutoDeductSavings is null) missing.Add("akiba ilipe mwezi usiolipwa ndiyo/hapana (AutoDeductSavings)");
            if (missing.Count > 0)
                throw new ArgumentException("Mipangilio ya kwanza haijakamilika. Weka: " + string.Join("; ", missing) + ".");
            tracked = new TruthGroupSettings { GroupId = groupId };
            _context.TruthGroupSettings.Add(tracked);
        }

        if (dto.DueDateDay is { } dd) { if (dd is < 1 or > 28) throw new ArgumentException("DueDateDay 1..28"); tracked.DueDateDay = dd; }
        if (dto.GracePeriodDays is { } g) { if (g is < 0 or > 25) throw new ArgumentException("GracePeriodDays 0..25"); tracked.GracePeriodDays = g; }
        if (dto.ContributionStartDate is { } s) tracked.ContributionStartDate = DateTime.SpecifyKind(new DateTime(s.Year, s.Month, 1), DateTimeKind.Utc);
        if (dto.FineOnPartialPayment is { } f) tracked.FineOnPartialPayment = f;
        if (dto.AutoDeductSavings is { } ads) tracked.AutoDeductSavings = ads;
        if (dto.ManualEntryCutoff is { } mc) tracked.ManualEntryCutoff = DateTime.SpecifyKind(mc, DateTimeKind.Utc);
        tracked.UpdatedAt = DateTime.UtcNow;
        tracked.UpdatedByUserId = userId;
        if (dto.FoundingMembersCutoff is { } fc) tracked.FoundingMembersCutoff = DateTime.SpecifyKind(fc, DateTimeKind.Utc);
        if (dto.MonthlyContribution is { } m) { if (m <= 0) throw new ArgumentException("MonthlyContribution > 0"); tracked.MonthlyContribution = m; }
        if (dto.LateFine is { } lf) { if (lf < 0) throw new ArgumentException("LateFine >= 0"); tracked.LateFine = lf; }
        if (dto.JoiningFee is { } jf) { if (jf < 0) throw new ArgumentException("JoiningFee >= 0"); tracked.JoiningFee = jf; }
        if (dto.LoanLatePenaltyAmount is { } lp) { if (lp < 0) throw new ArgumentException("LoanLatePenaltyAmount >= 0"); tracked.LoanLatePenaltyAmount = lp; }

        await AddAuditAsync(groupId, null, userId, before is null ? AuditAction.GroupPolicyCreated : AuditAction.GroupPolicyVersioned,
            nameof(TruthGroupSettings), groupId, before, Snap(tracked), null, "Kanuni za kikundi", ct);
        await _context.SaveChangesAsync(ct);
        return tracked;
    }

    // =====================================================================
    // 2. KUFUTA DATA ZA MAJARIBIO (kikundi kimoja tu; dryRun kwanza)
    // =====================================================================
    public sealed record ResetReport(bool DryRun, Dictionary<string, int> Rows);

    public async Task<ResetReport> ResetGroupFinancialDataAsync(Guid groupId, bool dryRun, bool keepLoans, Guid actorUserId, CancellationToken ct = default)
    {
        var memberIds = await _context.GroupMembers.Where(m => m.GroupId == groupId).Select(m => m.Id).ToListAsync(ct);
        var withdrawalIds = await _context.Withdrawals.Where(w => w.GroupId == groupId).Select(w => w.Id).ToListAsync(ct);
        var welfareIds = await _context.WelfareEvents.Where(w => w.GroupId == groupId).Select(w => w.Id).ToListAsync(ct);
        var groupEventIds = await _context.GroupEvents.Where(e => e.GroupId == groupId).Select(e => e.Id).ToListAsync(ct);

        // Mpangilio unaheshimu foreign keys (watoto kwanza)
        var plan = new List<(string Name, Func<Task<int>> Run)>
        {
            ("WithdrawalApprovals", () => Del(_context.WithdrawalApprovals.Where(x => withdrawalIds.Contains(x.WithdrawalId)))),
            ("Withdrawals",         () => Del(_context.Withdrawals.Where(x => x.GroupId == groupId))),
            ("WalletWithdrawals",   () => Del(_context.WalletWithdrawals.Where(x => x.GroupId == groupId))),
            ("WelfareContributions",() => Del(_context.WelfareContributions.Where(x => welfareIds.Contains(x.WelfareEventId)))),
            ("WelfareDisbursements",() => Del(_context.WelfareDisbursements.Where(x => welfareIds.Contains(x.WelfareEventId)))),
            ("WelfareObligations",  () => Del(_context.WelfareObligations.Where(x => welfareIds.Contains(x.WelfareEventId)))),
            ("WelfareEvents",       () => Del(_context.WelfareEvents.Where(x => x.GroupId == groupId))),
            ("EventContributions",  () => Del(_context.EventContributions.Where(x => groupEventIds.Contains(x.GroupEventId)))),
            ("Fines",               () => Del(_context.Fines.Where(x => x.GroupId == groupId))),
            ("GroupEvents",         () => Del(_context.GroupEvents.Where(x => x.GroupId == groupId))),
            ("ComplianceSnapshots", () => Del(_context.ComplianceSnapshots.Where(x => x.GroupId == groupId))),
            ("Debts",               () => Del(_context.Debts.Where(x => x.GroupId == groupId))),
            ("LedgerEntries",       () => Del(_context.LedgerEntries.Where(x => x.GroupId == groupId))),
            ("FinancialEvents",     () => Del(_context.FinancialEvents.Where(x => x.GroupId == groupId))),
            ("ImportedStatements",  () => Del(_context.ImportedStatements.Where(x => x.GroupId == groupId))),
            ("MemberStatusHistories",() => Del(_context.MemberStatusHistories.Where(x => memberIds.Contains(x.MemberId)))),
            ("MkobaRawTransactions",() => Del(_context.MkobaRawTransactions.Where(x => x.GroupId == groupId))),
            ("MkobaStatementGaps",  () => Del(_context.MkobaStatementGaps.Where(x => x.GroupId == groupId))),
            ("TruthAdjustments",    () => Del(_context.TruthAdjustments.Where(x => x.GroupId == groupId))),
        };
        if (!keepLoans) plan.Add(("Loans", () => Del(_context.Loans.Where(x => x.GroupId == groupId))));
        // HAZIGUSWI: Groups, GroupMembers, Users, GroupPolicies, GroupSettings, MemberPhones, AuditEvents

        var counts = new Dictionary<string, int>();
        if (dryRun)
        {
            counts["WithdrawalApprovals"] = await _context.WithdrawalApprovals.CountAsync(x => withdrawalIds.Contains(x.WithdrawalId), ct);
            counts["Withdrawals"] = withdrawalIds.Count;
            counts["WalletWithdrawals"] = await _context.WalletWithdrawals.CountAsync(x => x.GroupId == groupId, ct);
            counts["WelfareEvents"] = welfareIds.Count;
            counts["GroupEvents"] = groupEventIds.Count;
            counts["Fines"] = await _context.Fines.CountAsync(x => x.GroupId == groupId, ct);
            counts["ComplianceSnapshots"] = await _context.ComplianceSnapshots.CountAsync(x => x.GroupId == groupId, ct);
            counts["Debts"] = await _context.Debts.CountAsync(x => x.GroupId == groupId, ct);
            counts["LedgerEntries"] = await _context.LedgerEntries.CountAsync(x => x.GroupId == groupId, ct);
            counts["FinancialEvents"] = await _context.FinancialEvents.CountAsync(x => x.GroupId == groupId, ct);
            counts["ImportedStatements"] = await _context.ImportedStatements.CountAsync(x => x.GroupId == groupId, ct);
            counts["MkobaRawTransactions"] = await _context.MkobaRawTransactions.CountAsync(x => x.GroupId == groupId, ct);
            counts["MkobaStatementGaps"] = await _context.MkobaStatementGaps.CountAsync(x => x.GroupId == groupId, ct);
            counts["TruthAdjustments"] = await _context.TruthAdjustments.CountAsync(x => x.GroupId == groupId, ct);
            if (!keepLoans) counts["Loans"] = await _context.Loans.CountAsync(x => x.GroupId == groupId, ct);
            return new ResetReport(true, counts);
        }

        var useTx = _context.Database.IsRelational();
        await using var tx = useTx ? await _context.Database.BeginTransactionAsync(ct) : null;
        foreach (var (name, run) in plan) counts[name] = await run();

        // Counters za zamani za wanachama zirudi sifuri
        var members = await _context.GroupMembers.Where(m => m.GroupId == groupId).ToListAsync(ct);
        foreach (var m in members) { m.TotalContributionsCount = 0; m.AdvanceBalance = 0m; }
        await AddAuditAsync(groupId, null, actorUserId, AuditAction.Deleted, "GroupFinancialData", groupId, null,
            new { keepLoans, rowsDeleted = counts }, null, "Futa data za fedha za majaribio", ct);
        await _context.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return new ResetReport(false, counts);

        async Task<int> Del<T>(IQueryable<T> q) where T : class
        {
            var rows = await q.ToListAsync(ct);
            _context.RemoveRange(rows);
            await _context.SaveChangesAsync(ct);
            return rows.Count;
        }
    }

    // =====================================================================
    // 3. IMPORT YA PDF YA M-KOBA — data ghafi tu + ukaguzi wa uadilifu
    // =====================================================================
    public sealed record ImportReport(
        string FileHash, int RowsInPdf, int Inserted, int AlreadyExisted,
        List<string> UnreadableRows, List<object> Gaps,
        decimal DepositsInPdf, decimal WithdrawalsInPdf,
        decimal? DeclaredContributionTotal, decimal? DeclaredGroupBalance, decimal? LastBalanceInPdf,
        List<object> UnknownPhones, List<string> Warnings);

    public async Task<ImportReport> ImportMkobaStatementAsync(Guid groupId, Stream pdf, Guid userId, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        await pdf.CopyToAsync(ms, ct);
        var hash = Convert.ToHexString(SHA256.HashData(ms.ToArray()));
        ms.Position = 0;

        var parsed = await _parser.ParseStatementDetailedAsync(ms);
        var warnings = new List<string>();
        if (parsed.Transactions.Count == 0) throw new ConflictException("PDF haina miamala inayosomeka.");

        var existingRefs = (await _context.MkobaRawTransactions.Where(t => t.GroupId == groupId).Select(t => t.Reference).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        int inserted = 0, existed = 0;
        foreach (var t in parsed.Transactions)
        {
            var reference = t.ReferenceNumber.Trim().ToUpperInvariant();
            if (!existingRefs.Add(reference)) { existed++; continue; } // PDF zinazopishana (Jul + Aug) ni salama
            string phone;
            try { phone = PhoneNumbers.Normalize(t.PhoneNumber); }
            catch (ArgumentException) { phone = t.PhoneNumber; warnings.Add($"{reference}: namba ya simu '{t.PhoneNumber}' haieleweki."); }
            _context.MkobaRawTransactions.Add(new MkobaRawTransaction
            {
                Id = Guid.NewGuid(), GroupId = groupId, Reference = reference,
                Timestamp = DateTime.SpecifyKind(t.TransactionDate, DateTimeKind.Utc), // saa za ukutani za EAT (kama PDF)
                Phone = phone, PayerName = t.MemberName, Amount = t.Amount, Balance = t.Balance,
                IsWithdrawal = t.IsWithdrawal, Description = t.Description, RowOrder = t.RowOrder,
                StatementFileHash = hash, ImportedByUserId = userId
            });
            inserted++;
        }
        await _context.SaveChangesAsync(ct);

        // Namba za simu mpya zilizoonekana: sasisha MemberPhones zilizowekwa kabla ya import
        var phonesSeen = await _context.MkobaRawTransactions.Where(t => t.GroupId == groupId)
            .GroupBy(t => t.Phone).Select(g => new { Phone = g.Key, Name = g.Max(x => x.PayerName) }).ToListAsync(ct);
        var mapped = await _context.MemberPhones.Where(p => p.GroupId == groupId).ToListAsync(ct);
        foreach (var mp in mapped)
        {
            var seen = phonesSeen.FirstOrDefault(p => p.Phone == mp.Phone);
            if (seen is not null) { mp.MatchedInMkoba = true; mp.MkobaNameSeen = seen.Name; }
        }

        // Ukaguzi wa mnyororo wa balance juu ya data YOTE ya kikundi (inashughulikia PDF zinazofuatana)
        var gapsFound = await RecomputeGapsAsync(groupId, ct);
        await AddAuditAsync(groupId, null, userId, AuditAction.ImportCompleted, "MkobaStatement", groupId, null,
            new { fileHash = hash, rowsInPdf = parsed.Transactions.Count, inserted, alreadyExisted = existed, unreadable = parsed.UnreadableRows.Count,
                  gaps = gapsFound.Count }, hash, "Import ya PDF ya M-Koba", ct);
        await _context.SaveChangesAsync(ct);

        var mappedSet = mapped.Select(m => m.Phone).ToHashSet();
        var unknownPhones = phonesSeen.Where(p => !mappedSet.Contains(p.Phone))
            .Select(p => (object)new { phone = p.Phone, mkobaName = p.Name }).ToList();

        var deposits = parsed.Transactions.Where(t => !t.IsWithdrawal).Sum(t => t.Amount);
        var withdrawals = parsed.Transactions.Where(t => t.IsWithdrawal).Sum(t => t.Amount);
        var last = parsed.Transactions.OrderBy(t => t.TransactionDate).ThenBy(t => t.RowOrder).LastOrDefault()?.Balance;
        if (parsed.UnreadableRows.Count > 0)
            warnings.Add($"Mistari {parsed.UnreadableRows.Count} haikusomeka — angalia UnreadableRows. HAIJARUKWA KIMYA.");
        if (parsed.DeclaredContributionTotal is { } dc && dc != deposits)
            warnings.Add($"Jumla ya michango iliyotangazwa na PDF ({dc:N0}) ≠ jumla ya mistari ya deposit ({deposits:N0}); tofauti {dc - deposits:N0} " +
                         $"= mapengo ({gapsFound.Where(g => g.Amount > 0).Sum(g => g.Amount):N0}) + balance ya kuanzia/mengineyo.");
        if (parsed.DeclaredGroupBalance is { } gb && last is { } lb && gb != lb)
            warnings.Add($"Balance ya mwisho kwenye mistari ({lb:N0}) ≠ 'Group Balance' ya PDF ({gb:N0}).");

        // Mapengo yanaelezwa kwa kila kikundi (majina, balance, muda) — ujumbe uleule unaoonekana kwenye tabo ya Mapengo
        var described = await StatementGapDescriber.DescribeAsync(_context, groupId, gapsFound, ct);
        var openGaps = gapsFound.Where(g => g.ResolutionStatus != TruthApprovalStatus.Approved).ToList();
        if (openGaps.Count > 0)
        {
            var inflow = openGaps.Where(g => g.Amount > 0).Sum(g => g.Amount);
            var outflow = openGaps.Where(g => g.Amount < 0).Sum(g => -g.Amount);
            var parts = new List<string>();
            if (inflow > 0) parts.Add($"{inflow:N0} ziliingia");
            if (outflow > 0) parts.Add($"{outflow:N0} zilitoka");
            warnings.Insert(0, $"Mnyororo wa balance umeruka mara {openGaps.Count}: {string.Join(" na ", parts)} bila mstari kwenye PDF. " +
                               "Miamala ya kabla na baada ya kila pengo ni sahihi; pesa ya pengo HAIHESABIWI kwa mtu yeyote mpaka " +
                               "mwenyewe atambuliwe kwa ushahidi (tabo ya Mapengo ya PDF).");
        }

        return new ImportReport(hash, parsed.Transactions.Count, inserted, existed, parsed.UnreadableRows,
            described.Select(d => (object)new
            {
                d.Id, d.AfterReference, d.BeforeReference, d.WindowStart, d.WindowEnd, d.Amount,
                d.Previous, d.Next, d.BalanceAfterPrevious, d.ExpectedBalanceBeforeNext, d.Title,
                maelezo = d.Message,
                resolved = gapsFound.First(g => g.Id == d.Id).ResolutionStatus == TruthApprovalStatus.Approved
            }).ToList(),
            deposits, withdrawals, parsed.DeclaredContributionTotal, parsed.DeclaredGroupBalance, last, unknownPhones, warnings);
    }

    private async Task<List<MkobaStatementGap>> RecomputeGapsAsync(Guid groupId, CancellationToken ct)
    {
        var rows = await _context.MkobaRawTransactions.Where(t => t.GroupId == groupId)
            .Select(t => new { t.Reference, t.Timestamp, t.Amount, t.IsWithdrawal, t.Balance, t.RowOrder }).ToListAsync(ct);
        var gaps = StatementIntegrity.FindGaps(rows.Select(r =>
            new StatementIntegrity.Row(r.Reference, r.Timestamp, r.Amount, r.IsWithdrawal, r.Balance, r.RowOrder)));

        var existing = await _context.MkobaStatementGaps.Where(g => g.GroupId == groupId).ToListAsync(ct);
        var current = new List<MkobaStatementGap>();
        foreach (var g in gaps)
        {
            var e = existing.FirstOrDefault(x => x.BeforeReference == g.BeforeReference);
            if (e is null)
            {
                e = new MkobaStatementGap
                {
                    Id = Guid.NewGuid(), GroupId = groupId, AfterReference = g.AfterReference, BeforeReference = g.BeforeReference,
                    WindowStart = DateTime.SpecifyKind(g.WindowStart, DateTimeKind.Utc), WindowEnd = DateTime.SpecifyKind(g.WindowEnd, DateTimeKind.Utc),
                    Amount = g.Amount
                };
                _context.MkobaStatementGaps.Add(e);
            }
            current.Add(e);
        }
        // Pengo lililojazwa na PDF mpya (mstari uliokosekana sasa upo) linaondolewa kama halijaidhinishwa
        foreach (var stale in existing.Where(x => gaps.All(g => g.BeforeReference != x.BeforeReference)
                                                  && x.ResolutionStatus != TruthApprovalStatus.Approved))
            _context.MkobaStatementGaps.Remove(stale);
        return current;
    }

    // =====================================================================
    // 4. NAMBA ZA SIMU (kiongozi anaweka; mfumo unahakiki dhidi ya M-Koba)
    // =====================================================================
    public sealed record PhoneResult(MemberPhone Phone, bool MatchedInMkoba, string? MkobaNameSeen, int DepositsFound, decimal DepositsTotal, string Ujumbe);

    public async Task<PhoneResult> AddPhoneAsync(Guid groupId, Guid memberId, string rawPhone, Guid userId, string? note, CancellationToken ct = default)
    {
        var member = await _context.GroupMembers.FirstOrDefaultAsync(m => m.Id == memberId && m.GroupId == groupId, ct)
            ?? throw new KeyNotFoundException("Mwanachama hayupo kwenye kikundi hiki.");
        var phone = PhoneNumbers.Normalize(rawPhone);

        var taken = await _context.MemberPhones.FirstOrDefaultAsync(p => p.GroupId == groupId && p.Phone == phone, ct);
        if (taken is not null)
            throw new ConflictException(taken.GroupMemberId == memberId
                ? $"Namba {phone} tayari imewekwa kwa mwanachama huyu."
                : $"Namba {phone} tayari imewekwa kwa mwanachama mwingine — namba moja haiwezi kuwa ya watu wawili.");

        var seen = await _context.MkobaRawTransactions.Where(t => t.GroupId == groupId && t.Phone == phone && !t.IsWithdrawal).ToListAsync(ct);
        var mp = new MemberPhone
        {
            Id = Guid.NewGuid(), GroupId = groupId, GroupMemberId = member.Id, Phone = phone,
            MatchedInMkoba = seen.Count > 0, MkobaNameSeen = seen.Select(s => s.PayerName).FirstOrDefault(),
            AddedByUserId = userId, Note = note
        };
        _context.MemberPhones.Add(mp);
        await AddAuditAsync(groupId, member.Id, userId, AuditAction.Created, nameof(MemberPhone), mp.Id, null,
            new { mp.Phone, mp.MatchedInMkoba, mp.MkobaNameSeen, mp.Note }, mp.Phone, "Namba ya simu imeunganishwa na mwanachama", ct);
        await _context.SaveChangesAsync(ct);

        var msg = seen.Count > 0
            ? $"✅ Imelingana na M-Koba: jina la M-Koba ni '{mp.MkobaNameSeen}', malipo {seen.Count} ya jumla {seen.Sum(s => s.Amount):N0}. HAKIKI kuwa ni mtu sahihi."
            : "⚠️ Namba hii HAIJAONEKANA kwenye statement yoyote ya M-Koba iliyoingizwa. Hakiki namba — au mwanachama bado hajalipa kwa namba hii.";
        return new PhoneResult(mp, mp.MatchedInMkoba, mp.MkobaNameSeen, seen.Count, seen.Sum(s => s.Amount), msg);
    }

    public async Task RemovePhoneAsync(Guid groupId, Guid phoneId, Guid actorUserId, CancellationToken ct = default)
    {
        var mp = await _context.MemberPhones.FirstOrDefaultAsync(p => p.Id == phoneId && p.GroupId == groupId, ct)
            ?? throw new KeyNotFoundException("Namba haipo.");
        _context.MemberPhones.Remove(mp);
        await AddAuditAsync(groupId, mp.GroupMemberId, actorUserId, AuditAction.Deleted, nameof(MemberPhone), mp.Id,
            new { mp.Phone, mp.MatchedInMkoba, mp.MkobaNameSeen, mp.Note }, new { removed = true }, mp.Phone,
            "Namba ya simu imeondolewa (malipo yake hayatahesabiwa kwa mwanachama huyu tena)", ct);
        await _context.SaveChangesAsync(ct);
    }

    /// <summary>Orodha ya namba zote zilizoonekana M-Koba + mwanachama aliyeunganishwa (kama yupo).</summary>
    public async Task<List<object>> PhoneOverviewAsync(Guid groupId, CancellationToken ct = default)
    {
        var mapped = await _context.MemberPhones.Where(p => p.GroupId == groupId).ToListAsync(ct);
        var members = await _context.GroupMembers.Include(m => m.User).Where(m => m.GroupId == groupId).ToListAsync(ct);
        var seen = await _context.MkobaRawTransactions.Where(t => t.GroupId == groupId && !t.IsWithdrawal)
            .GroupBy(t => t.Phone).Select(g => new { Phone = g.Key, Name = g.Max(x => x.PayerName), Count = g.Count(), Total = g.Sum(x => x.Amount) })
            .ToListAsync(ct);
        var phones = seen.Select(s => s.Phone).Union(mapped.Select(m => m.Phone)).OrderBy(p => p);
        return phones.Select(p =>
        {
            var s = seen.FirstOrDefault(x => x.Phone == p);
            var m = mapped.FirstOrDefault(x => x.Phone == p);
            var mem = m is null ? null : members.FirstOrDefault(x => x.Id == m.GroupMemberId);
            return (object)new
            {
                phone = p, mkobaName = s?.Name, deposits = s?.Count ?? 0, total = s?.Total ?? 0m,
                phoneId = m?.Id, memberId = mem?.Id, memberName = mem?.User?.FullName,
                state = m is null ? "Unmapped" : s is null ? "NotSeen" : "Linked",
                hali = m is null ? "HAIJAUNGANISHWA — pesa hii haijulikani ni ya nani"
                     : s is null ? "Imewekwa, haijaonekana M-Koba" : "Imeunganishwa"
            };
        }).ToList();
    }

    // =====================================================================
    // 5. MANUAL (pesa ya mkononi / Kianzio) — maker-checker
    // =====================================================================
    /// <summary>
    /// Ukaguzi wa ombi la manual — UNATUMIKA na hakikisho (preview) NA uwasilishaji, ili
    /// kiongozi aone makosa yale yale kabla ya kubofya "Wasilisha".
    /// </summary>
    public async Task<List<string>> ValidateManualPaymentAsync(Guid groupId, Guid memberId, decimal amount, DateTime occurredAtEat,
        bool isJoinFee, CancellationToken ct = default)
    {
        var problems = new List<string>();
        if (amount <= 0) problems.Add("Kiasi lazima kiwe zaidi ya 0.");
        if (amount % 1 != 0) problems.Add("Kiasi kiwe namba kamili (bila senti).");
        if (!await _context.GroupMembers.AnyAsync(m => m.Id == memberId && m.GroupId == groupId, ct))
        {
            problems.Add("Mwanachama hayupo kwenye kikundi hiki.");
            return problems;
        }
        if (occurredAtEat > TruthLedgerService.NowEat().AddMinutes(5))
            problems.Add("Tarehe ya malipo haiwezi kuwa ya baadaye.");

        var policy = TruthLedgerService.ToTruthPolicy(await _truth.GetSettingsAsync(groupId, ct));
        if (policy.ManualEntryCutoff is null)
            problems.Add("Kikundi hakijaweka tarehe ya mwisho ya kumbukumbu za mkononi (ManualEntryCutoff). Iweke kwenye Mipangilio kwanza.");
        else if (occurredAtEat > policy.ManualEntryCutoff.Value)
            problems.Add($"Tarehe {occurredAtEat:dd/MM/yyyy} iko baada ya tarehe ya mwisho ya kumbukumbu za mkononi ({policy.ManualEntryCutoff:dd/MM/yyyy}). Baada ya hapo M-Koba pekee ndiyo ukweli — mwanachama alipe kupitia M-Koba.");
        if (occurredAtEat < new DateTime(policy.GroupFirstPeriod.Year, policy.GroupFirstPeriod.Month, 1).AddMonths(-3))
            problems.Add("Tarehe ni ya kabla ya kikundi kuanza michango.");

        if (isJoinFee && amount > 0)
        {
            var now = await _truth.GetMemberTruthAsync(memberId, null, ct);
            var pendingJf = await _context.FinancialEvents.Where(e => e.MemberId == memberId && e.Type == FinancialEventType.JoiningFeePaid
                                                                     && e.Source == EventSource.Manual && e.Status == FinancialEventStatus.Pending)
                .SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;
            var room = now.Statement.JoinFeeOutstanding - pendingJf;
            if (amount > room)
                problems.Add(room <= 0
                    ? $"Hakuna Kianzio kinachodaiwa (kilichobaki {now.Statement.JoinFeeOutstanding:N0}, kinachosubiri idhini {pendingJf:N0})."
                    : $"Kianzio kilichobaki ni {now.Statement.JoinFeeOutstanding:N0} (na {pendingJf:N0} kinasubiri idhini) — unaweza kuingiza hadi {room:N0} tu.");
        }
        return problems;
    }

    public sealed record MoneySummary(decimal JoinFeePaid, decimal JoinFeeOutstanding, decimal Arrears, decimal Savings,
        decimal TotalDebtIncludingJoinFee, decimal LoanOutstanding, IReadOnlyList<string> MonthsInArrears);
    public sealed record MonthChange(string Month, string StatusBefore, string StatusAfter, decimal OutstandingBefore, decimal OutstandingAfter);
    public sealed record PreviewResult(bool Valid, List<string> Problems, MoneySummary Before, MoneySummary After,
        List<AllocationLine> NewAllocations, List<MonthChange> ChangedMonths, int PendingRequestsForMember);

    /// <summary>
    /// HAKIKISHO: "Nikiingiza malipo haya, nini kitatokea?" — hakuna kinachohifadhiwa.
    /// Inacheza upya historia yote ya mwanachama pamoja na malipo ya kufikirika na kulinganisha kabla/baada.
    /// </summary>
    public async Task<PreviewResult> PreviewManualPaymentAsync(Guid groupId, Guid memberId, decimal amount, DateTime occurredAtEat,
        bool isJoinFee, CancellationToken ct = default)
    {
        var problems = await ValidateManualPaymentAsync(groupId, memberId, amount, occurredAtEat, isJoinFee, ct);
        if (problems.Contains("Mwanachama hayupo kwenye kikundi hiki.")) throw new KeyNotFoundException(problems[0]);

        var before = (await _truth.GetMemberTruthAsync(memberId, null, ct)).Statement;
        var reference = "HAKIKISHO";
        var hypo = amount > 0
            ? new List<TruthPayment> { new(reference, DateTime.SpecifyKind(occurredAtEat, DateTimeKind.Unspecified), amount,
                PaymentSource.ManualHistorical, isJoinFee ? PaymentPurpose.JoinFee : PaymentPurpose.Auto, IsApproved: true) }
            : new List<TruthPayment>();
        // Cutoff inakaguliwa na ValidateManualPaymentAsync; hapa tunaonyesha mgawanyo hata kama kuna tatizo
        var after = (await _truth.GetMemberTruthAsync(memberId, null, ct, hypo)).Statement;

        var changes = new List<MonthChange>();
        foreach (var a in after.Periods)
        {
            var b = before.Periods.FirstOrDefault(x => x.Period == a.Period);
            if (b is null || b.Status != a.Status || b.Outstanding != a.Outstanding)
                changes.Add(new MonthChange(a.Period.Label, b?.Status.ToString() ?? "-", a.Status.ToString(), b?.Outstanding ?? 0, a.Outstanding));
        }
        var pending = await _context.FinancialEvents.CountAsync(e => e.MemberId == memberId && e.Source == EventSource.Manual
                                                                     && e.Status == FinancialEventStatus.Pending, ct);
        var rejectedHypo = after.Rejected.FirstOrDefault(r => r.Payment.Reference == reference);
        if (rejectedHypo is not null && problems.Count == 0) problems.Add(rejectedHypo.Reason); // sababu ya injini, kama ukaguzi haukuiona

        return new PreviewResult(problems.Count == 0, problems, Summ(before), Summ(after),
            after.Allocations.Where(x => x.Reference == reference).ToList(), changes, pending);

        static MoneySummary Summ(TruthStatement s) => new(s.JoinFeePaid, s.JoinFeeOutstanding, s.Arrears, s.Savings,
            s.TotalDebtIncludingJoinFee, s.LoanOutstanding, s.MonthsInArrears.Select(m => m.Label).ToList());
    }

    public const int EvidenceReferenceMaxLength = 200;

    public async Task<FinancialEvent> RequestManualPaymentAsync(Guid groupId, Guid memberId, decimal amount, DateTime occurredAtEat,
        bool isJoinFee, string reason, EvidenceType? evidenceType, string? evidenceReference, Guid requestedByUserId, CancellationToken ct = default)
    {
        // Malipo ya nje ya M-Koba ndiyo hatari zaidi kiukaguzi: yanahitaji maelezo + ushahidi unaoweza kufuatiliwa
        // + kiongozi wa pili kuidhinisha (DecideManualPaymentAsync). Bila hivyo hayahifadhiwi kabisa.
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Eleza malipo haya (mf. 'Kianzio kwa mtunza-hazina').");
        if (evidenceType is null || !Enum.IsDefined(evidenceType.Value))
            throw new ArgumentException("Chagua aina ya ushahidi (risiti, daftari la fedha, kumbukumbu za kikao, tamko la kiongozi, stakabadhi, au nyingine).");
        if (string.IsNullOrWhiteSpace(evidenceReference))
            throw new ArgumentException("Andika ushahidi ulipo (mf. 'Risiti Na. 12' au 'Daftari la fedha uk. 4') ili mkaguzi aweze kuufuatilia.");
        if (evidenceReference.Trim().Length > EvidenceReferenceMaxLength)
            throw new ArgumentException($"Maelezo ya ushahidi yasizidi herufi {EvidenceReferenceMaxLength}.");
        var problems = await ValidateManualPaymentAsync(groupId, memberId, amount, occurredAtEat, isJoinFee, ct);
        if (problems.Contains("Mwanachama hayupo kwenye kikundi hiki.")) throw new KeyNotFoundException(problems[0]);
        if (amount <= 0) throw new ArgumentException(problems[0]);
        if (problems.Count > 0) throw new ConflictException(string.Join(" ", problems));
        var member = await _context.GroupMembers.FirstAsync(m => m.Id == memberId && m.GroupId == groupId, ct);

        var fe = new FinancialEvent
        {
            Id = Guid.NewGuid(), GroupId = groupId, MemberId = member.Id,
            Type = isJoinFee ? FinancialEventType.JoiningFeePaid : FinancialEventType.PaymentReceived,
            Amount = amount, OccurredAt = DateTime.SpecifyKind(occurredAtEat, DateTimeKind.Utc),
            CreatedBy = requestedByUserId, Source = EventSource.Manual,
            SourceReference = $"MANUAL-{Guid.NewGuid():N}"[..20],
            Status = FinancialEventStatus.Pending, Reason = reason.Trim(),
            EvidenceType = evidenceType, EvidenceReference = evidenceReference.Trim()
        };
        _context.FinancialEvents.Add(fe);
        await AddAuditAsync(groupId, member.Id, requestedByUserId, AuditAction.Created, nameof(FinancialEvent), fe.Id, null, Snap(fe),
            fe.SourceReference, fe.Reason, ct);
        await _context.SaveChangesAsync(ct);
        return fe;
    }

    public async Task<FinancialEvent> DecideManualPaymentAsync(Guid groupId, Guid eventId, bool approve, Guid deciderUserId, Guid deciderMemberId, string? note, CancellationToken ct = default)
    {
        var fe = await _context.FinancialEvents.FirstOrDefaultAsync(e => e.Id == eventId && e.GroupId == groupId && e.Source == EventSource.Manual, ct)
            ?? throw new KeyNotFoundException("Ombi halipo.");
        if (fe.Status != FinancialEventStatus.Pending) throw new ConflictException($"Ombi tayari ni {fe.Status}.");
        if (fe.CreatedBy == deciderUserId) throw new ForbiddenException("Aliyeingiza ombi HAWEZI kuliidhinisha mwenyewe (maker-checker).");
        var before = Snap(fe);
        fe.Status = approve ? FinancialEventStatus.Approved : FinancialEventStatus.Rejected;
        fe.ApprovedByGroupMemberId = deciderMemberId;
        fe.ApprovedAt = DateTime.UtcNow;
        fe.ApprovalNote = note;
        await AddAuditAsync(groupId, fe.MemberId, deciderUserId, approve ? AuditAction.GovernanceApproved : AuditAction.GovernanceRejected,
            nameof(FinancialEvent), fe.Id, before, Snap(fe), fe.SourceReference, note, ct);
        await _context.SaveChangesAsync(ct);
        return fe;
    }

    // =====================================================================
    // 6. MSAMAHA WA FAINI / NYONGEZA YA MUDA — maker-checker
    // =====================================================================
    public async Task<TruthAdjustmentRecord> RequestAdjustmentAsync(Guid groupId, Guid? memberId, TruthAdjustmentKind kind,
        int year, int month, int extraDays, string reason, Guid userId, CancellationToken ct = default)
    {
        if (month is < 1 or > 12) throw new ArgumentException("Mwezi 1..12");
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Sababu inahitajika");
        if (kind == TruthAdjustmentKind.GraceExtension && extraDays is < 1 or > 25) throw new ArgumentException("ExtraDays 1..25");
        if (kind == TruthAdjustmentKind.FineWaiver && memberId is null)
            throw new ArgumentException("Msamaha wa faini ni wa mwanachama mmoja. Kwa kikundi kizima tumia GraceExtension.");
        if (memberId is { } mid && !await _context.GroupMembers.AnyAsync(m => m.Id == mid && m.GroupId == groupId, ct))
            throw new KeyNotFoundException("Mwanachama hayupo kwenye kikundi hiki.");

        var rec = new TruthAdjustmentRecord
        {
            Id = Guid.NewGuid(), GroupId = groupId, GroupMemberId = memberId, Kind = kind, Year = year, Month = month,
            ExtraDays = kind == TruthAdjustmentKind.GraceExtension ? extraDays : 0, Reason = reason, RequestedByUserId = userId
        };
        _context.TruthAdjustments.Add(rec);
        await AddAuditAsync(groupId, memberId, userId, AuditAction.Created, nameof(TruthAdjustmentRecord), rec.Id, null, Snap(rec), null, reason, ct);
        await _context.SaveChangesAsync(ct);
        return rec;
    }

    public async Task<TruthAdjustmentRecord> DecideAdjustmentAsync(Guid groupId, Guid id, bool approve, Guid deciderUserId, string? note = null, CancellationToken ct = default)
    {
        var rec = await _context.TruthAdjustments.FirstOrDefaultAsync(a => a.Id == id && a.GroupId == groupId, ct)
            ?? throw new KeyNotFoundException("Ombi halipo.");
        if (rec.Status != TruthApprovalStatus.Pending) throw new ConflictException($"Ombi tayari ni {rec.Status}.");
        if (rec.RequestedByUserId == deciderUserId) throw new ForbiddenException("Aliyeomba HAWEZI kuidhinisha mwenyewe (maker-checker).");
        var before = Snap(rec);
        rec.Status = approve ? TruthApprovalStatus.Approved : TruthApprovalStatus.Rejected;
        rec.DecidedByUserId = deciderUserId;
        rec.DecidedAt = DateTime.UtcNow;
        await AddAuditAsync(groupId, rec.GroupMemberId, deciderUserId, approve ? AuditAction.GovernanceApproved : AuditAction.GovernanceRejected,
            nameof(TruthAdjustmentRecord), rec.Id, before, Snap(rec), null, note ?? rec.Reason, ct);
        await _context.SaveChangesAsync(ct);
        return rec;
    }

    // =====================================================================
    // 7. PENGO LA PDF — kumthibitisha mwenyewe (maker-checker)
    // =====================================================================
    public async Task<MkobaStatementGap> RequestGapResolutionAsync(Guid groupId, Guid gapId, Guid memberId, string evidence, Guid userId, CancellationToken ct = default)
    {
        var gap = await _context.MkobaStatementGaps.FirstOrDefaultAsync(g => g.Id == gapId && g.GroupId == groupId, ct)
            ?? throw new KeyNotFoundException("Pengo halipo.");
        if (gap.Amount <= 0) throw new ConflictException("Pengo hili ni pesa iliyotoka — haliwezi kupewa mwanachama kama malipo.");
        if (gap.ResolutionStatus == TruthApprovalStatus.Approved) throw new ConflictException("Pengo tayari limethibitishwa.");
        if (string.IsNullOrWhiteSpace(evidence)) throw new ArgumentException("Ushahidi unahitajika (mf. SMS ya M-Koba yenye reference, au statement kamili ya Vodacom).");
        if (!await _context.GroupMembers.AnyAsync(m => m.Id == memberId && m.GroupId == groupId, ct))
            throw new KeyNotFoundException("Mwanachama hayupo kwenye kikundi hiki.");
        var before = Snap(gap);
        gap.AssignedGroupMemberId = memberId;
        gap.Evidence = evidence;
        gap.AssumedTimestamp = gap.WindowEnd; // tarehe ya kihafidhina: mwisho wa dirisha la pengo
        gap.ResolutionStatus = TruthApprovalStatus.Pending;
        gap.RequestedByUserId = userId;
        gap.RequestedAt = DateTime.UtcNow;
        await AddAuditAsync(groupId, memberId, userId, AuditAction.Updated, nameof(MkobaStatementGap), gap.Id, before, Snap(gap),
            gap.BeforeReference, evidence, ct);
        await _context.SaveChangesAsync(ct);
        return gap;
    }

    public async Task<MkobaStatementGap> DecideGapResolutionAsync(Guid groupId, Guid gapId, bool approve, Guid deciderUserId, string? note = null, CancellationToken ct = default)
    {
        var gap = await _context.MkobaStatementGaps.FirstOrDefaultAsync(g => g.Id == gapId && g.GroupId == groupId, ct)
            ?? throw new KeyNotFoundException("Pengo halipo.");
        if (gap.ResolutionStatus != TruthApprovalStatus.Pending) throw new ConflictException("Hakuna ombi linalosubiri kwa pengo hili.");
        if (gap.RequestedByUserId == deciderUserId) throw new ForbiddenException("Aliyeomba HAWEZI kuidhinisha mwenyewe (maker-checker).");
        var before = Snap(gap);   // muhimu: kukataa kunafuta ushahidi kwenye pengo — kumbukumbu hii ndiyo inabaki nao
        var assigned = gap.AssignedGroupMemberId;
        if (approve)
        {
            gap.ResolutionStatus = TruthApprovalStatus.Approved;
            gap.ApprovedByUserId = deciderUserId;
            gap.ApprovedAt = DateTime.UtcNow;
        }
        else
        {
            gap.ResolutionStatus = null; gap.AssignedGroupMemberId = null; gap.Evidence = null; gap.AssumedTimestamp = null;
            gap.RequestedByUserId = null; gap.RequestedAt = null;
        }
        await AddAuditAsync(groupId, assigned, deciderUserId, approve ? AuditAction.GovernanceApproved : AuditAction.GovernanceRejected,
            nameof(MkobaStatementGap), gap.Id, before, Snap(gap), gap.BeforeReference, note, ct);
        await _context.SaveChangesAsync(ct);
        return gap;
    }
}
