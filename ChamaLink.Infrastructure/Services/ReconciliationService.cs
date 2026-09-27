using ChamaLink.Application.DTOs;
using ChamaLink.Application.Interfaces;
using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Infrastructure.Services;

// ── RECONCILIATION ENGINE (Ulinganisho) — 2026-09-19 ──────────────────
//
// P0 kwa Ukonga: "Mfumo hauwezi bado kutoa uchambuzi ule ule ambao
// mtunza-hazina anaupata kwenye Excel yake" — chombo hiki ndicho
// kinachofunga pengo hilo. Kinalinganisha:
//
//   1. Taarifa ya M-Koba (PDF)   — ground truth ya pesa zilizofika pochi
//   2. Excel ya mtunza-hazina    — interpretation ya kikundi
//   3. Ledger ya ChamaLink       — kile mfumo umerekodi
//
// kwa kila mwanachama × kila mwezi, na kutoa tofauti + sababu.
//
// MUHIMU: chombo hiki ni READ-ONLY. Hakiandiki kitu kwenye DB; kina-
// parse nyaraka mbili kwa kutumia parsers zile zile za import (ili
// ulinganisho uwe wa haki — parser moja, chanzo kimoja cha ukweli),
// kisha kinasoma LedgerEntries.
//
// Kwa nini read-only? Kikundi kinaweza kuwa kimeingiza M-Koba AU Excel
// (au vyote viwili). Ulinganisho unafanya kazi hata kwa data iliyo-
// ingizwa zamani, bila kuhitaji migration wala kubadilisha mtiririko wa
// import uliopo.
public class ReconciliationService
{
    private readonly ApplicationDbContext _context;
    private readonly IMKobaParserService _mkobaParser;
    private readonly ITreasuryExcelParserService _excelParser;

    public ReconciliationService(
        ApplicationDbContext context,
        IMKobaParserService mkobaParser,
        ITreasuryExcelParserService excelParser)
    {
        _context = context;
        _mkobaParser = mkobaParser;
        _excelParser = excelParser;
    }

    // Aina za entries zinazohesabiwa kama "pesa zilizokusanywa" kwa
    // mwezi. Zinafanana na zile zinazotumwa na njia zote za import
    // (M-Koba STEP 1-4 na Treasury 1-4) pamoja na michango ya mkono.
    private static readonly TransactionType[] CashInTypes =
    {
        TransactionType.Contribution,
        TransactionType.FinePayment,
        TransactionType.JoiningFee,
        TransactionType.EventContribution
    };

    public async Task<ReconciliationDto> GetReconciliationAsync(
        Guid groupId, int year,
        Stream? mkobaStream, Stream? excelStream)
    {
        var group = await _context.Groups
            .FirstOrDefaultAsync(g => g.Id == groupId)
            ?? throw new Domain.Exceptions.NotFoundException("Kikundi hakijapatikana.");

        var dto = new ReconciliationDto
        {
            GroupId = groupId,
            GroupName = group.Name,
            Year = year,
            HasPdf = mkobaStream != null,
            HasExcel = excelStream != null
        };

        decimal lateFine = group.Settings?.Contribution.LateFine ?? 0m;
        decimal joiningFeeTarget = group.Settings?.Financial.JoiningFee ?? 0m;

        // ── Wanachama (pamoja na simu na majina kwa ajili ya matching) ──
        // Named tuple (siyo anonymous type) ili MatchMkobaMember iweze
        // kupokea orodha hii moja kwa moja.
        var memberEntities = await _context.GroupMembers
            .Include(m => m.User)
            .Where(m => m.GroupId == groupId)
            .OrderBy(m => m.MemberNumber)
            .ToListAsync();

        var members = memberEntities
            .Select(m => (
                Id: m.Id,
                UserId: m.UserId,
                MemberNumber: m.MemberNumber,
                Name: m.User?.FullName ?? "Mwanachama",
                Phone: m.User?.PhoneNumber ?? ""))
            .ToList();

        // Ramani za ufunguo: memberId -> index kwenye members
        var memberIndex = new Dictionary<Guid, int>();
        for (int i = 0; i < members.Count; i++)
            memberIndex[members[i].Id] = i;

        int n = members.Count;
        var pdfByMember = new decimal[n, 13];    // [member][month 1-12]
        var excelByMember = new decimal[n, 13];
        var excelJoining = new decimal[n];       // KIANZIO column (ya mwaka)
        var mkobaWithdrawals = new decimal[n];

        // ═══════════════ CHANZO 1: M-Koba PDF ═══════════════
        if (mkobaStream != null)
        {
            var transactions = await _mkobaParser.ParseStatementAsync(mkobaStream);

            // candidates kwa name-matching fallback (memberEntities tayari
            // zina User kupitia Include)
            var candidatesForName = memberEntities
                .Select(m => (Member: m, User: m.User))
                .ToList();

            decimal unmatchedAmount = 0m;
            int unmatchedCount = 0;

            foreach (var tx in transactions)
            {
                if (tx.TransactionDate.Year != year) continue;
                int month = tx.TransactionDate.Month;
                if (month < 1 || month > 12) continue;

                int? idx = MatchMkobaMember(tx, members);

                if (idx == null)
                {
                    // Jaribu kwa jina (MemberNameMatcher) ikiwa simu haikugonga
                    if (!string.IsNullOrWhiteSpace(tx.MemberName))
                    {
                        var match = MemberNameMatcher.Match(
                            tx.MemberName, candidatesForName, out _);
                        if (match is { IsExact: true } exact &&
                            memberIndex.TryGetValue(exact.MemberId, out var byName))
                            idx = byName;
                    }
                }

                if (idx == null)
                {
                    if (!tx.IsWithdrawal)
                    {
                        unmatchedCount++;
                        unmatchedAmount += tx.Amount;
                    }
                    continue;
                }

                if (tx.IsWithdrawal)
                    mkobaWithdrawals[idx.Value] += tx.Amount;
                else
                    pdfByMember[idx.Value, month] += tx.Amount;
            }

            if (unmatchedCount > 0)
                dto.Warnings.Add(
                    $"M-Koba: miamala {unmatchedCount} (TSH {unmatchedAmount:N0}) " +
                    "haikulingana na mwanachama yeyote - angalia namba za simu.");
        }

        // ═══════════════ CHANZO 2: Excel ya mtunza-hazina ═══════════════
        if (excelStream != null)
        {
            var rows = await _excelParser.ParseAsync(excelStream);

            // memberEntities tayari zimepakiwa na Include(m => m.User) —
            // tunatumia zile zile (hakuna query ya pili; EF haiwezi
            // kutafsiri tuple literal ndani ya expression tree).
            var candidatesList = memberEntities
                .Select(c => (Member: c, User: c.User))
                .ToList();

            foreach (var row in rows)
            {
                var match = MemberNameMatcher.Match(row.ExcelName, candidatesList, out _);

                if (match is not { IsExact: true } exact ||
                    !memberIndex.TryGetValue(exact.MemberId, out var idx))
                {
                    decimal rowTotal = row.MonthlyAmounts
                        .Where(a => a.HasValue).Sum(a => a!.Value);
                    if (rowTotal > 0 || (row.JoiningFee ?? 0) > 0)
                        dto.Warnings.Add(
                            $"Excel: safu '{row.ExcelName}' (TSH {rowTotal:N0}) " +
                            "haikulingana na mwanachama - imeachwa kwenye ulinganisho.");
                    continue;
                }

                for (int m = 1; m <= 12; m++)
                {
                    var amt = row.MonthlyAmounts[m - 1];
                    if (amt.HasValue && amt.Value > 0)
                    {
                        excelByMember[idx, m] += amt.Value;
                        // Michango ya Excel inayotokana na mwaka ulioagizwa
                        // tu; parser hairudishi miaka, hivyo tunachukua
                        // kwamba jedwali ni la mwaka huu.
                    }
                }

                if (row.JoiningFee.HasValue && row.JoiningFee.Value > 0)
                    excelJoining[idx] += row.JoiningFee.Value;

                if ((row.Msiba ?? 0) > 0 || (row.Sherehe ?? 0) > 0)
                    dto.Warnings.Add(
                        $"Excel: '{row.ExcelName}' ana MSIBA/SHEREHE " +
                        $"(TSH {(row.Msiba ?? 0) + (row.Sherehe ?? 0):N0}) - " +
                        "matumizi ya ustawi hayalinganishwi na michango.");
            }
        }

        // ═══════════════ CHANZO 3: Ledger ya ChamaLink ═══════════════
        var yearStart = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var yearEnd = new DateTime(year, 12, 31, 23, 59, 59, DateTimeKind.Utc);

        var entries = await _context.LedgerEntries
            .Where(l => l.GroupId == groupId &&
                        l.CreatedAt >= yearStart && l.CreatedAt <= yearEnd &&
                        CashInTypes.Contains(l.Type))
            .Select(l => new { l.UserId, l.Amount, l.CreatedAt })
            .ToListAsync();

        var ledgerByMember = new decimal[n, 13];
        var ledgerJoining = new decimal[n];

        var ledgerByUser = entries
            .GroupBy(e => e.UserId)
            .ToDictionary(g => g.Key, g => g.ToList());

        for (int i = 0; i < n; i++)
        {
            if (!ledgerByUser.TryGetValue(members[i].UserId, out var userEntries))
                continue;

            foreach (var e in userEntries)
            {
                int month = e.CreatedAt.Month;
                if (month < 1 || month > 12) continue;
                ledgerByMember[i, month] += e.Amount;
            }
        }

        // KIANZIO za mwaka huu (kutoka ledger) - kwa mstari wa KIANZIO.
        var jfEntries = await _context.LedgerEntries
            .Where(l => l.GroupId == groupId &&
                        l.CreatedAt >= yearStart && l.CreatedAt <= yearEnd &&
                        l.Type == TransactionType.JoiningFee)
            .Select(l => new { l.UserId, l.Amount })
            .ToListAsync();

        var jfByUser = jfEntries
            .GroupBy(e => e.UserId)
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));

        for (int i = 0; i < n; i++)
            ledgerJoining[i] = jfByUser.GetValueOrDefault(members[i].UserId, 0m);

        // ═══════════════ JENGA MISTARI YA ULINGANISHO ═══════════════
        var now = DateTime.UtcNow;
        int lastMonth = year == now.Year ? now.Month : 12;

        for (int i = 0; i < n; i++)
        {
            for (int m = 1; m <= lastMonth; m++)
            {
                decimal pdf = pdfByMember[i, m];
                decimal excel = excelByMember[i, m];
                decimal ledger = ledgerByMember[i, m];

                // Ruka mistari isiyo na data kabisa
                if (pdf == 0 && excel == 0 && ledger == 0) continue;

                var row = new ReconciliationRowDto
                {
                    GroupMemberId = members[i].Id,
                    MemberName = members[i].Name,
                    MemberNumber = members[i].MemberNumber,
                    Month = m,
                    MonthLabel = new DateTime(year, m, 1)
                        .ToString("MMM", System.Globalization.CultureInfo
                            .InvariantCulture).ToUpperInvariant(),
                    PdfAmount = pdf,
                    ExcelAmount = excel,
                    LedgerAmount = ledger
                };

                Classify(row, dto.HasPdf, dto.HasExcel, lateFine);
                dto.Rows.Add(row);
            }

            // ── Mstari maalum wa KIANZIO (jumla ya mwaka) ──
            decimal jfLedger = ledgerJoining[i];
            decimal jfExcel = excelJoining[i];

            if (jfLedger > 0 || jfExcel > 0)
            {
                var jfRow = new ReconciliationRowDto
                {
                    GroupMemberId = members[i].Id,
                    MemberName = members[i].Name,
                    MemberNumber = members[i].MemberNumber,
                    Month = 0,
                    MonthLabel = "KIANZIO",
                    PdfAmount = 0m,   // M-Koba haitofautishi KIANZIO na mchango
                    ExcelAmount = jfExcel,
                    LedgerAmount = jfLedger,
                    Difference = jfLedger - jfExcel
                };

                if (!dto.HasExcel)
                {
                    // Hakuna Excel ya kulinganisha - onyesha ledger pekee
                    jfRow.Status = ReconciliationStatus.Sawa;
                    jfRow.Reason = jfLedger >= joiningFeeTarget && joiningFeeTarget > 0
                        ? $"KIANZIO imekamilika ({jfLedger:N0} / {joiningFeeTarget:N0})"
                        : $"KIANZIO mwezi huu: {jfLedger:N0}" +
                          (joiningFeeTarget > 0
                            ? $" (lengo {joiningFeeTarget:N0}, bado {Math.Max(0, joiningFeeTarget - jfLedger):N0})"
                            : "");
                }
                else if (jfRow.Difference == 0)
                {
                    jfRow.Status = ReconciliationStatus.Sawa;
                    jfRow.Reason = "KIANZIO inalingana kati ya Excel na mfumo.";
                }
                else if (jfRow.Difference > 0)
                {
                    // Mfumo una KIANZIO zaidi kuliko Excel - kawaida ni
                    // ziada ya michango iliyopelekwa KIANZIO (waterfall),
                    // ambayo Excel haiwezi kuionyesha kwenye safu ya KIANZIO.
                    jfRow.Status = ReconciliationStatus.MfumoZiada;
                    jfRow.Reason =
                        $"Mfumo una KIANZIO zaidi (+{jfRow.Difference:N0}) kuliko Excel - " +
                        "huenda ni ziada ya michango iliyopelekwa KIANZIO kiotomatiki " +
                        "(sio kosa, lakini thibitisha).";
                }
                else
                {
                    jfRow.Status = ReconciliationStatus.MfumoChini;
                    jfRow.Reason =
                        $"Mfumo una KIANZIO chini ({-jfRow.Difference:N0}) kuliko Excel - " +
                        "huenda KIANZIO ya Excel bado haijaingizwa mfumo.";
                }

                dto.Rows.Add(jfRow);
            }
        }

        // ── Jumla ──
        foreach (var r in dto.Rows.Where(r => r.Month > 0))
        {
            dto.TotalPdf += r.PdfAmount;
            dto.TotalExcel += r.ExcelAmount;
            dto.TotalLedger += r.LedgerAmount;
        }

        dto.CountSawa = dto.Rows.Count(r => r.Status == ReconciliationStatus.Sawa);
        dto.CountNyarakaHazilingani =
            dto.Rows.Count(r => r.Status == ReconciliationStatus.NyarakaHazilingani);
        dto.CountMfumoChini =
            dto.Rows.Count(r => r.Status == ReconciliationStatus.MfumoChini);
        dto.CountMfumoZiada =
            dto.Rows.Count(r => r.Status == ReconciliationStatus.MfumoZiada);

        // ── Withdrawals za M-Koba (muktadha, sio michango) ──
        for (int i = 0; i < n; i++)
        {
            if (mkobaWithdrawals[i] > 0)
                dto.Warnings.Add(
                    $"M-Koba: '{members[i].Name}' ana withdrawals TSH " +
                    $"{mkobaWithdrawals[i]:N0} mwaka {year} (mikopo/matokeo - " +
                    "siyo michango, hazilinganishwi hapa).");
        }

        return dto;
    }

    // ── Uainishaji wa mstari wa mwezi ─────────────────────────────────
    private static void Classify(
        ReconciliationRowDto row, bool hasPdf, bool hasExcel, decimal lateFine)
    {
        decimal pdf = row.PdfAmount;
        decimal excel = row.ExcelAmount;
        decimal ledger = row.LedgerAmount;

        // Chanzo cha matarajio: M-Koba ni ground truth; Excel ni fallback.
        decimal expected = hasPdf ? pdf : excel;
        row.Difference = ledger - (hasPdf && hasExcel ? Math.Max(pdf, excel) : expected);

        bool docsDisagree = hasPdf && hasExcel && pdf != excel;

        if (docsDisagree)
        {
            row.Status = ReconciliationStatus.NyarakaHazilingani;
            decimal gap = Math.Abs(pdf - excel);

            if (lateFine > 0 && gap == lateFine)
                row.Reason =
                    $"PDF na Excel zinapotofautiana kwa TSH {gap:N0} = faini ya kuchelewa. " +
                    "Huenda mmoja amerejelea faini, mwingine la — thibitisha nani alichelewa.";
            else
                row.Reason =
                    $"PDF ({pdf:N0}) na Excel ({excel:N0}) hazilingani " +
                    $"(tofauti TSH {gap:N0}). Mfumo umerekodi {ledger:N0}. " +
                    "Thibitisha kwenye risiti/chapo la benki.";
            return;
        }

        if (row.Difference == 0)
        {
            row.Status = ReconciliationStatus.Sawa;
            row.Reason = hasPdf && hasExcel
                ? "Vyanzo vyote vitatu vinapatana."
                : ledger > 0
                    ? "Nyaraka na mfumo vinapatana."
                    : "Hakuna pesa zilizopokelewa mwezi huu (sawa kote).";
            return;
        }

        if (row.Difference < 0)
        {
            row.Status = ReconciliationStatus.MfumoChini;
            row.Reason =
                $"Mfumo umerekodi TSH {-row.Difference:N0} CHINI kuliko nyaraka. " +
                "Sababu zinazowezekana: (1) mchango bado haujaingizwa mfumo, " +
                "(2) mwanachama hakulinganishwa wakati wa import, " +
                "(3) tarehe ya entry iko mwezi mwingine.";
            return;
        }

        // row.Difference > 0
        row.Status = ReconciliationStatus.MfumoZiada;

        // Import mara mbili? Ikiwa ledger ≈ PDF + Excel (na vyote viwili
        // viko na ni sawa), hii ni dalili ya marudio.
        if (hasPdf && hasExcel && pdf == excel && ledger == pdf + excel)
        {
            row.Reason =
                $"Ledger ({ledger:N0}) = PDF + Excel ({pdf:N0} + {excel:N0}). " +
                "DALILI YA IMPORT MARA MBILI: huenda uliingiza taarifa ya M-Koba " +
                "NA Excel ya mwezi ule ule. Chunguza entries za ledger.";
            return;
        }

        row.Reason =
            $"Mfumo una TSH {row.Difference:N0} ZAIDI kuliko nyaraka. " +
            "Sababu zinazowezekana: mchango wa mkono ulioingizwa moja kwa moja, " +
            "au ziada halisi ya mwanachama.";
    }

    // ── Kulinganisha miamala ya M-Koba na mwanachama kwa simu ─────────
    // Mbinu ile ile ya MkobaImportController: normalisha 0→255, kisha
    // linganisha kamili au kwa tarakimu 9 za mwisho.
    private static int? MatchMkobaMember(
        MKobaTransactionItemDto tx,
        List<(Guid Id, Guid UserId, string MemberNumber, string Name, string Phone)> members)
    {
        if (string.IsNullOrWhiteSpace(tx.PhoneNumber)) return null;

        string cleanPhone = tx.PhoneNumber.Replace(" ", "").Replace("+", "");
        if (cleanPhone.StartsWith("0"))
            cleanPhone = MkobaPlatform.CountryDialCode + cleanPhone.Substring(1);

        string last9 = cleanPhone.Length >= 9
            ? cleanPhone.Substring(cleanPhone.Length - 9)
            : cleanPhone;

        foreach (var m in members)
        {
            var userPhone = (m.Phone ?? "").Replace(" ", "").Replace("+", "");
            if (userPhone.Length == 0) continue;

            if (userPhone == cleanPhone || userPhone.EndsWith(last9))
                return members.IndexOf(m);
        }

        return null;
    }
}
