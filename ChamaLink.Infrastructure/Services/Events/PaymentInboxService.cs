using System.Text.Json;
using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Exceptions;
using ChamaLink.Infrastructure.Services.Benefits;
using ChamaLink.Infrastructure.Services.Common;
using ChamaLink.Infrastructure.Services.Truth;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Infrastructure.Services.Events;

/// <summary>
/// SASISHO21 — SANDUKU LA MALIPO.
///
/// Tatizo: kikundi cha wanachama 1,200 kinakusanya pesa kwenye namba MOJA.
/// Statement ya M-Koba ina mistari 400 kwa mwezi. Ni nani alilipa nini?
///
/// Suluhisho — kanuni tano:
///   1. HAKUNA pesa inayomlipa mtu bila uthibitisho. Mstari usiotambulika
///      unakaa kwenye sanduku (suspense), unaonekana, haumlipi yeyote.
///   2. KODI YA MALIPO ndiyo njia ya kwanza (mf. "J3-E07-0147").
///   3. Kila uamuzi una risiti + namba ya muamala + aliyethibitisha (audit).
///   4. Kiasi kikubwa kinahitaji MKONO WA PILI (sera ya kikundi, si namba ndani ya msimbo).
///   5. Uthibitisho hubadilisha MMILIKI wa mstari wa statement pekee —
///      hakuna pesa mpya na hakuna ledger ya pili.
/// </summary>
public class PaymentInboxService
{
    private readonly ApplicationDbContext _context;
    private readonly EventWalletService _wallet;
    private readonly NotificationService _notify;
    private readonly Sms.SmsService? _sms;

    public PaymentInboxService(ApplicationDbContext context, EventWalletService wallet, NotificationService notify,
        Sms.SmsService? sms = null)
    {
        _context = context;
        _wallet = wallet;
        _notify = notify;
        _sms = sms;
    }

    public sealed record InboxRow(
        Guid ReceiptId, string Source, string Status, string? Reference, decimal Amount, DateTime OccurredAt,
        string? Phone, string? PayerName, string? Description, string? PaymentCodeSeen,
        string? MatchRule, Guid? SuggestedMemberId, string? SuggestedMemberNumber, string? SuggestedName,
        bool NeedsSecondApprover, string? Note);

    public sealed record InboxSummary(
        int Total, int Unmatched, int Suggested, int Confirmed, int Rejected,
        decimal UnmatchedAmount, decimal ConfirmedAmount, int PendingClaims);

    // ==================================================================
    // 1. Kujenga sanduku kutoka statement (inaitwa baada ya import kuidhinishwa)
    // ==================================================================

    /// <summary>
    /// Hupitia amana zote za kikundi zisizokuwa na risiti bado na kuziweka kwenye sanduku:
    ///   • namba inamilikiwa → AutoMatched (haitegemewi mtu; Injini ya Ukweli inaiona tayari)
    ///   • kodi ya malipo inaonekana → Suggested (kanuni "Code")
    ///   • namba inajulikana + kiasi = deni kamili → Suggested ("PhoneExact")
    ///   • namba inajulikana + kiasi &lt; deni → Suggested ("PhonePartial")
    ///   • hakuna → Unmatched
    /// Inarudisha idadi ya mistari mipya. Ni idempotent.
    /// </summary>
    public async Task<int> SyncAsync(Guid groupId, CancellationToken ct = default)
    {
        var receipts = await _context.PaymentReceipts.AsNoTracking()
            .Where(r => r.GroupId == groupId && r.MkobaTransactionId != null)
            .Select(r => r.MkobaTransactionId!.Value)
            .ToListAsync(ct);
        var seen = receipts.ToHashSet();

        var rows = await _context.MkobaRawTransactions.AsNoTracking()
            .Where(t => t.GroupId == groupId && !t.IsWithdrawal)
            .OrderBy(t => t.Timestamp).ThenBy(t => t.RowOrder)
            .ToListAsync(ct);
        var fresh = rows.Where(r => !seen.Contains(r.Id)).ToList();
        if (fresh.Count == 0) return 0;

        var phones = await _context.MemberPhones.AsNoTracking()
            .Where(p => p.GroupId == groupId && p.Status == TruthApprovalStatus.Approved)
            .ToListAsync(ct);
        var phoneOwner = phones
            .GroupBy(p => p.Phone, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.AddedAt).First(), StringComparer.OrdinalIgnoreCase);

        var members = await _context.GroupMembers.AsNoTracking()
            .Where(m => m.GroupId == groupId)
            .Select(m => new { m.Id, m.MemberNumber })
            .ToDictionaryAsync(m => m.Id, m => m.MemberNumber, ct);

        // Wajibu wazi kwa mwanachama (kwa kupendekeza kiasi)
        var open = await _context.EventObligations.AsNoTracking()
            .Where(o => o.GroupId == groupId && o.Status == EventObligationStatus.Pending)
            .ToListAsync(ct);
        var openByMember = open.GroupBy(o => o.GroupMemberId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.CreatedAt).ToList());
        var byCode = open.Where(o => o.PaymentCode != null)
            .ToDictionary(o => o.PaymentCode!, o => o, StringComparer.OrdinalIgnoreCase);

        var added = 0;
        foreach (var t in fresh)
        {
            var receipt = new PaymentReceipt
            {
                Id = Guid.NewGuid(), GroupId = groupId, Source = PaymentReceiptSource.Statement,
                MkobaTransactionId = t.Id, Reference = t.Reference, Phone = t.Phone, PayerName = t.PayerName,
                Amount = t.Amount, OccurredAt = t.Timestamp, Description = t.Description,
                CreatedAt = DateTime.UtcNow
            };

            // Umiliki uliothibitishwa tayari (mf. uliingizwa kabla ya toleo hili)
            if (t.AttributedGroupMemberId != null)
            {
                receipt.Status = PaymentReceiptStatus.Confirmed;
                receipt.ConfirmedGroupMemberId = t.AttributedGroupMemberId;
                receipt.ConfirmedAt = t.AttributedAt;
                receipt.MatchRule = "Attributed";
                receipt.Note = t.AttributionReason;
                _context.PaymentReceipts.Add(receipt); added++;
                continue;
            }

            // SASISHO21 (Sehemu ya 2): kodi ya KUJIUNGA ina nafasi yake — mtu bado si mwanachama.
            var joinCode = PaymentCode.FindJoin(t.Description);
            if (joinCode is not null)
            {
                var req = await _context.MembershipRequests.AsNoTracking()
                    .FirstOrDefaultAsync(r => r.GroupId == groupId && r.PaymentCode == joinCode
                                              && (r.Status == MembershipRequestStatus.AwaitingPayment || r.Status == MembershipRequestStatus.Paid), ct);
                if (req is not null)
                {
                    receipt.Kind = PaymentKind.Joining;
                    receipt.MembershipRequestId = req.Id;
                    receipt.PaymentCodeSeen = joinCode;
                    receipt.Status = PaymentReceiptStatus.Suggested;
                    receipt.MatchRule = "JoinCode";
                    receipt.Note = $"Ada ya kujiunga ({req.PaymentAmount:N0})";
                    _context.PaymentReceipts.Add(receipt); added++;
                    continue;
                }
            }

            var code = PaymentCode.Find(t.Description);
            if (code is not null)
            {
                receipt.PaymentCodeSeen = code;
                if (byCode.TryGetValue(code, out var obligation))
                {
                    receipt.Status = PaymentReceiptStatus.Suggested;
                    receipt.MatchRule = "Code";
                    receipt.SuggestedGroupMemberId = obligation.GroupMemberId;
                    receipt.SuggestedObligationId = obligation.Id;
                    _context.PaymentReceipts.Add(receipt); added++;
                    continue;
                }
            }

            if (phoneOwner.TryGetValue(t.Phone ?? string.Empty, out var link))
            {
                // Namba ni ya mwanachama — pesa tayari inaonekana kwake. Rekodi tu.
                receipt.Status = PaymentReceiptStatus.AutoMatched;
                receipt.MatchRule = "Phone";
                receipt.ConfirmedGroupMemberId = link.GroupMemberId;
                receipt.ConfirmedAt = t.Timestamp;

                if (openByMember.TryGetValue(link.GroupMemberId, out var debts) && debts.Count > 0)
                {
                    var total = debts.Sum(d => d.OutstandingAmount);
                    var o = debts.FirstOrDefault(d => d.OutstandingAmount == t.Amount) ?? debts[0];
                    receipt.SuggestedGroupMemberId = link.GroupMemberId;
                    receipt.SuggestedObligationId = o.Id;
                    receipt.MatchRule = t.Amount == total || t.Amount == o.OutstandingAmount ? "PhoneExact" : "PhonePartial";
                }

                _context.PaymentReceipts.Add(receipt); added++;
                continue;
            }

            receipt.Status = PaymentReceiptStatus.Unmatched;
            receipt.MatchRule = code is null ? "None" : "CodeUnknown";
            _context.PaymentReceipts.Add(receipt); added++;
        }

        await _context.SaveChangesAsync(ct);
        return added;
    }

    // ==================================================================
    // 2. Kuona sanduku
    // ==================================================================

    public async Task<List<InboxRow>> ListAsync(Guid groupId, string? status, CancellationToken ct = default)
    {
        var q = _context.PaymentReceipts.AsNoTracking().Where(r => r.GroupId == groupId);
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<PaymentReceiptStatus>(status, true, out var st))
            q = q.Where(r => r.Status == st);

        var rows = await q.OrderByDescending(r => r.OccurredAt).Take(500).ToListAsync(ct);
        var settings = await _context.TruthGroupSettings.AsNoTracking().FirstOrDefaultAsync(s => s.GroupId == groupId, ct);
        var threshold = settings?.PaymentSecondApprovalAmount ?? 0m;

        var ids = rows.Where(r => r.SuggestedGroupMemberId != null).Select(r => r.SuggestedGroupMemberId!.Value)
            .Concat(rows.Where(r => r.ConfirmedGroupMemberId != null).Select(r => r.ConfirmedGroupMemberId!.Value))
            .Distinct().ToList();
        var members = await MemberLabelsAsync(groupId, ids, ct);

        return rows.Select(r =>
        {
            var mid = r.ConfirmedGroupMemberId ?? r.SuggestedGroupMemberId;
            members.TryGetValue(mid ?? Guid.Empty, out var label);
            return new InboxRow(
                r.Id, r.Source.ToString(), r.Status.ToString(), r.Reference, r.Amount, r.OccurredAt,
                r.Phone, r.PayerName, r.Description, r.PaymentCodeSeen, r.MatchRule,
                mid, label.Number, label.Name,
                threshold > 0 && r.Amount >= threshold && r.Status != PaymentReceiptStatus.Confirmed,
                r.Note);
        }).ToList();
    }

    public async Task<InboxSummary> SummaryAsync(Guid groupId, CancellationToken ct = default)
    {
        var rows = await _context.PaymentReceipts.AsNoTracking()
            .Where(r => r.GroupId == groupId && r.Source == PaymentReceiptSource.Statement)
            .Select(r => new { r.Status, r.Amount }).ToListAsync(ct);
        var claims = await _context.PaymentClaims.CountAsync(c => c.GroupId == groupId && c.Status == PaymentClaimStatus.Pending, ct);

        return new InboxSummary(
            rows.Count,
            rows.Count(r => r.Status == PaymentReceiptStatus.Unmatched),
            rows.Count(r => r.Status == PaymentReceiptStatus.Suggested),
            rows.Count(r => r.Status == PaymentReceiptStatus.Confirmed || r.Status == PaymentReceiptStatus.AutoMatched),
            rows.Count(r => r.Status == PaymentReceiptStatus.Rejected),
            rows.Where(r => r.Status == PaymentReceiptStatus.Unmatched).Sum(r => r.Amount),
            rows.Where(r => r.Status == PaymentReceiptStatus.Confirmed || r.Status == PaymentReceiptStatus.AutoMatched).Sum(r => r.Amount),
            claims);
    }

    /// <summary>
    /// Meza ya pande tatu: statement ilivyokuja · madai ya wanachama · kile mfumo kinachotarajia.
    /// </summary>
    public async Task<object> ThreeWayAsync(Guid groupId, CancellationToken ct = default)
    {
        var summary = await SummaryAsync(groupId, ct);

        var claims = await _context.PaymentClaims.AsNoTracking()
            .Where(c => c.GroupId == groupId)
            .GroupBy(c => c.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Amount = g.Sum(x => x.Amount) })
            .ToListAsync(ct);

        var obligations = await _context.EventObligations.AsNoTracking()
            .Where(o => o.GroupId == groupId)
            .GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Outstanding = g.Sum(x => x.RequiredAmount - x.DeductedAmount) })
            .ToListAsync(ct);

        return new
        {
            statement = summary,
            claims = claims.Select(c => new { status = c.Status.ToString(), count = c.Count, amount = c.Amount }),
            expected = obligations.Select(o => new { status = o.Status.ToString(), count = o.Count, outstanding = o.Outstanding })
        };
    }

    // ==================================================================
    // 3. Kuthibitisha / kukataa
    // ==================================================================

    /// <summary>
    /// Kiongozi anathibitisha kwamba mstari wa statement ni wa mwanachama X.
    ///
    /// Sheria:
    ///   • Mwanachama lazima awe hai kwenye kikundi.
    ///   • Mstari hauwezi kuwa umethibitishwa tayari (hakuna kuhamisha pesa kimya kimya).
    ///   • Kiasi ≥ PaymentSecondApprovalAmount kinahitaji kiongozi wa PILI (4-eyes):
    ///     aliyeanza hawezi kuthibitisha mwenyewe.
    /// Baada ya kuthibitisha: umiliki unawekwa, deni la zamani linalipwa (waterfall ile ile),
    /// na mwanachama anajulishwa ndani ya mfumo.
    /// </summary>
    public async Task<object> ConfirmAsync(Guid groupId, Guid receiptId, Guid memberId, string? note, Guid userId,
        bool markSecondApproval = false, CancellationToken ct = default)
    {
        var actor = await MemberAccess.RequireEffectiveLeaderAsync(_context, groupId, userId, ct,
            "Kuthibitisha malipo ni kazi ya viongozi.");

        var settings = await _context.TruthGroupSettings.AsNoTracking().FirstOrDefaultAsync(s => s.GroupId == groupId, ct);
        var threshold = settings?.PaymentSecondApprovalAmount ?? 0m;

        var r = await _context.PaymentReceipts.FirstOrDefaultAsync(x => x.Id == receiptId && x.GroupId == groupId, ct)
                ?? throw new NotFoundException("Risiti haipo.");

        if (r.Status == PaymentReceiptStatus.Confirmed)
            throw new ConflictException("Risiti hii imethibitishwa tayari.");
        if (r.Status == PaymentReceiptStatus.Rejected)
            throw new ConflictException("Risiti hii ilikataliwa. Kama ni kosa, ongeza mpya badala ya kuibadilisha.");

        // SAFU YA USALAMA: mstari ambao tayari unamilikiwa (kwa namba au kwa uamuzi wa awali)
        // HAUHAMISHWI kimya kimya. Kubadilisha mmiliki ni marekebisho yenye kumbukumbu,
        // si kitufe cha "confirm" — vinginevyo pesa inaweza kuhamishwa mara mbili.
        if (r.ConfirmedGroupMemberId is { } owner && owner != memberId)
            throw new ConflictException("Mstari huu tayari unamilikiwa na mwanachama mwingine. " +
                                        "Kuhamisha umiliki ni marekebisho yenye kumbukumbu, si uthibitisho.");

        // ---- Malipo ya kujiunga: hayana mwanachama bado ----
        if (r.Kind == PaymentKind.Joining && r.MembershipRequestId is { } reqId)
            return await ConfirmJoiningAsync(groupId, r, reqId, note, userId, ct);

        if (memberId == Guid.Empty)
            throw new ValidationException("Chagua mwanachama anayemiliki malipo haya.");

        var member = await _context.GroupMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == memberId && m.GroupId == groupId, ct)
            ?? throw new NotFoundException("Mwanachama hakapatikana kwenye kikundi hiki.");
        if (!MemberAccess.IsActive(member.Status))
            throw new ValidationException("Mwanachama huyu hayupo hai kwenye kikundi — pesa haiwezi kupewa akaunti yake.");

        var needsSecond = threshold > 0 && r.Amount > threshold;
        var cleanNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim()[..Math.Min(note.Trim().Length, 500)];

        if (needsSecond && actor.UserId != userId)
            throw new ForbiddenException("Mchakato wa idhini umeshindikana. Jaribu tena.");

        // Hatua ya pili (4-eyes): kiongozi wa kwanza anafungua ombi, kiongozi MWINGINE anakamilisha.
        // Aliyeanza hawezi kujithibitisha mwenyewe — hata akiita endpoint hii mara ya pili.
        if (needsSecond)
        {
            if (r.ConfirmedByUserId is null)
            {
                r.ConfirmedByUserId = userId;
                r.Note = cleanNote;
                r.SuggestedGroupMemberId = memberId;
                r.MatchRule = r.MatchRule ?? "Manual";
                await AuditTrail.AddAsync(_context, groupId, memberId, userId, AuditAction.PaymentReceiptSecondApproved,
                    nameof(PaymentReceipt), r.Id, null,
                    new { step = "initiated", memberId, r.Amount, needsSecondApprover = true },
                    r.Reference, "Kuthibitisha malipo yasiyotambulika (kimeanzishwa, kinasubiri mkono wa pili)", ct);
                await _context.SaveChangesAsync(ct);
                return new { status = "InasubiriMkonoWapili", message = "Kiasi hiki kinahitaji kiongozi wa pili kuthibitisha." };
            }

            if (r.ConfirmedByUserId == userId)
                throw new ForbiddenException("Aliyeanzisha uthibitisho HAWEZI kuwa mthibitishaji wa pili. Kiongozi mwingine anahitajika.");

            r.SecondApproverUserId = userId;
            r.SecondApprovedAt = DateTime.UtcNow;
        }

        r.Status = PaymentReceiptStatus.Confirmed;
        r.ConfirmedGroupMemberId = memberId;
        r.ConfirmedAt = DateTime.UtcNow;
        r.Note = cleanNote ?? r.Note;

        Guid? txId = r.MkobaTransactionId;
        if (txId is not null)
        {
            var tx = await _context.MkobaRawTransactions.FirstOrDefaultAsync(t => t.Id == txId && t.GroupId == groupId, ct)
                     ?? throw new NotFoundException("Mstari wa statement haupo.");
            if (tx.AttributedGroupMemberId is not null && tx.AttributedGroupMemberId != memberId)
                throw new ConflictException("Mstari huu unamilikiwa na mwanachama mwingine tayari.");
            tx.AttributedGroupMemberId = memberId;
            tx.AttributedByUserId = userId;
            tx.AttributedAt = DateTime.UtcNow;
            tx.AttributionReason = $"Sanduku la malipo: {r.MatchRule ?? "Manual"}" +
                                   (string.IsNullOrWhiteSpace(r.PaymentCodeSeen) ? "" : $" ({r.PaymentCodeSeen})");
        }

        await _context.SaveChangesAsync(ct);

        // Deni la zamani kwanza (waterfall ile ile ya injini)
        await _wallet.SweepAsync(groupId, memberId, ct);

        var owed = await _context.EventObligations.AsNoTracking()
            .Where(o => o.GroupId == groupId && o.GroupMemberId == memberId && o.Status == EventObligationStatus.Pending)
            .SumAsync(o => o.RequiredAmount - o.DeductedAmount, ct);
        var balance = await _wallet.BalanceAsync(groupId, memberId, ct);

        await _notify.NotifyAsync(groupId, new[] { member.Id }, "PaymentConfirmed", "Malipo yako yamethibitishwa",
            $"Malipo ya {r.Amount:N0} yamethibitishwa. Salio lako ni {balance:N0}" +
            (owed > 0 ? $" na deni lililobaki ni {owed:N0}." : " na huna deni."), "/", ct);

        await AuditTrail.AddAsync(_context, groupId, memberId, userId, AuditAction.WelfareContributionPaid,
            nameof(PaymentReceipt), r.Id, null,
            new { r.Reference, r.Amount, memberId, balance, owed, r.MatchRule, secondApprover = r.SecondApproverUserId },
            r.Reference, r.Note ?? "Uthibitisho wa malipo kwenye sanduku", ct);
        await _context.SaveChangesAsync(ct);

        // SASISHO21 (Sehemu ya 2): SMS ya uthibitisho — mwanachama anajua salio lake.
        if (_sms is not null)
        {
            var groupName = await _context.Groups.AsNoTracking()
                .Where(g => g.Id == groupId).Select(g => g.Name).FirstOrDefaultAsync(ct) ?? "Kikundi";
            await _sms.QueuePaymentConfirmedAsync(groupId, memberId, r.Amount, balance, r.PaymentCodeSeen, groupName, ct);
        }

        return new { status = "Imethibitishwa", balance, owed };
    }

    /// <summary>
    /// KUTHIBITISHA MALIPO YA KUJIUNGA.
    ///
    /// Pesa haimfanyi mtu mwanachama bado — inafanya ombi lake kuwa "Paid".
    /// Kiongozi ndiye anaidhinisha uanachama; ndipo pesa inapohamishiwa
    /// kwenye rekodi mpya (20,000 akiba + 10,000 kianzio kwa sera ya J3).
    /// </summary>
    private async Task<object> ConfirmJoiningAsync(Guid groupId, PaymentReceipt r, Guid requestId, string? note, Guid userId,
        CancellationToken ct)
    {
        var req = await _context.MembershipRequests.Include(x => x.User)
            .FirstOrDefaultAsync(x => x.Id == requestId && x.GroupId == groupId, ct)
            ?? throw new NotFoundException("Ombi la kujiunga halipo.");

        if (req.Status == MembershipRequestStatus.Approved)
            throw new ConflictException("Ombi hili limeshakubaliwa — mwanachama tayari yupo.");
        if (req.Status is MembershipRequestStatus.Rejected or MembershipRequestStatus.Cancelled or MembershipRequestStatus.Expired)
            throw new ConflictException("Ombi hili limefungwa; malipo hayawezi kuunganishwa nalo.");

        r.Status = PaymentReceiptStatus.Confirmed;
        r.ConfirmedByUserId = userId;
        r.ConfirmedAt = DateTime.UtcNow;
        r.Note = string.IsNullOrWhiteSpace(note) ? r.Note : note!.Trim()[..Math.Min(note.Trim().Length, 500)];

        req.Status = MembershipRequestStatus.Paid;
        req.PaymentConfirmedAt = DateTime.UtcNow;
        req.PaymentReceiptId = r.Id;
        req.PaymentReference = r.Reference;

        await _context.SaveChangesAsync(ct);

        await AuditTrail.AddAsync(_context, groupId, null, userId, AuditAction.PaymentReceiptConfirmed,
            nameof(PaymentReceipt), r.Id, null,
            new { r.Reference, r.Amount, requestId, applicant = req.User?.FullName, status = "Paid" },
            r.Reference, "Ada ya kujiunga imethibitishwa (mwanachama bado hajaundwa)", ct);
        await _context.SaveChangesAsync(ct);

        // Muombaji bado si mwanachama — hana kituo cha arifa. Viongozi wanajulishwa
        // ili wakamilishe uanachama; muombaji anaona hali kwenye "Maombi yangu".
        var leaders = await _context.GroupMembers.AsNoTracking()
            .Where(m => m.GroupId == groupId && MemberAccess.LeaderRoles.Contains(m.Role)
                        && MemberAccess.ActiveStatuses.Contains(m.Status))
            .Select(m => m.Id).ToListAsync(ct);
        await _notify.NotifyAsync(groupId, leaders, "JoiningPaymentConfirmed", "Ada ya kujiunga imelipwa",
            $"Malipo ya {r.Amount:N0} kwa {req.User?.FullName ?? "muombaji"} yamethibitishwa. " +
            "Kamilisheni uanachama kwenye Maombi ya Kujiunga.", "/wanachama/maombi", ct);

        return new
        {
            status = "Imethibitishwa",
            joining = true,
            requestId,
            ujumbe = "Ombi sasa linasubiri kiongozi kukamilisha uanachama (mwanachama hajaundwa bado)."
        };
    }

    /// <summary>
    /// Ombi linakubaliwa: mstari wa statement unamilikiwa na mwanachama MPYA.
    /// Injini ya Ukweli inagawanya: kianzio kwanza, kilichobaki akiba.
    /// </summary>
    public async Task AttachJoiningPaymentAsync(Guid groupId, Guid receiptId, Guid memberId, Guid userId, CancellationToken ct = default)
    {
        var r = await _context.PaymentReceipts.FirstOrDefaultAsync(x => x.Id == receiptId && x.GroupId == groupId, ct);
        if (r is null) return;

        r.ConfirmedGroupMemberId = memberId;
        if (r.MkobaTransactionId is { } txId)
        {
            var tx = await _context.MkobaRawTransactions.FirstOrDefaultAsync(t => t.Id == txId && t.GroupId == groupId, ct);
            if (tx is not null && tx.AttributedGroupMemberId is null)
            {
                tx.AttributedGroupMemberId = memberId;
                tx.AttributedByUserId = userId;
                tx.AttributedAt = DateTime.UtcNow;
                tx.AttributionReason = "Ada ya kujiunga (ombi limekubaliwa)";
            }
        }
        await _context.SaveChangesAsync(ct);

        // Waterfall ile ile: deni la zamani kwanza (mwanachama mpya hana deni), kilichobaki akiba.
        await _wallet.SweepAsync(groupId, memberId, ct);
    }

    public async Task<object> RejectAsync(Guid groupId, Guid receiptId, string reason, Guid userId, CancellationToken ct = default)
    {
        await MemberAccess.RequireEffectiveLeaderAsync(_context, groupId, userId, ct, "Kukataa risiti ni kazi ya viongozi.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
            throw new ValidationException("Sababu ya kukataa inahitajika (angalau herufi 5).");

        var r = await _context.PaymentReceipts.FirstOrDefaultAsync(x => x.Id == receiptId && x.GroupId == groupId, ct)
                ?? throw new NotFoundException("Risiti haipo.");
        if (r.Status == PaymentReceiptStatus.Confirmed)
            throw new ConflictException("Risiti iliyothibitishwa haiwezi kukataliwa. Rekebisha kwa marekebisho yenye kumbukumbu.");

        r.Status = PaymentReceiptStatus.Rejected;
        r.Note = reason.Trim()[..Math.Min(reason.Trim().Length, 500)];
        await AuditTrail.AddAsync(_context, groupId, r.SuggestedGroupMemberId, userId, AuditAction.PaymentReceiptRejected,
            nameof(PaymentReceipt), r.Id, null, new { r.Reference, r.Amount, reason = r.Note },
            r.Reference, "Risiti ya malipo imekataliwa", ct);
        await _context.SaveChangesAsync(ct);
        return new { status = "Imekataliwa" };
    }

    // ==================================================================
    // 4. Madai ya mwanachama — "Nimelipa"
    // ==================================================================

    public async Task<PaymentClaim> ClaimAsync(Guid groupId, decimal amount, string reference, string? code, string? note, Guid userId, CancellationToken ct = default)
    {
        var me = await MemberAccess.RequireActiveAsync(_context, groupId, userId, ct, tracking: false);

        // Sera ya kikundi inaweza kuzima madai kabisa (kiongozi anasikiliza mdomo kwa mdomo).
        var settings = await _context.TruthGroupSettings.AsNoTracking().FirstOrDefaultAsync(s => s.GroupId == groupId, ct);
        if (settings is { ClaimsEnabled: false })
            throw new ValidationException("Kikundi hiki hakiruhusu madai ya malipo kupitia mfumo. Wasiliana na mtunza hazina.");

        if (amount <= 0) throw new ValidationException("Kiasi kinapaswa kuwa kikubwa kuliko sifuri.");
        var r = (reference ?? string.Empty).Trim();
        if (r.Length < 4) throw new ValidationException("Weka namba ya muamala (kutoka SMS ya M-Pesa/Tigo/Airtel).");

        var dup = await _context.PaymentClaims.AnyAsync(c => c.GroupId == groupId && c.Reference == r, ct);
        if (dup) throw new ConflictException("Dai la namba hii ya muamala lipo tayari.");

        var claim = new PaymentClaim
        {
            Id = Guid.NewGuid(), GroupId = groupId, GroupMemberId = me.Id, Amount = amount, Reference = r,
            PaymentCode = code, Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()[..Math.Min(note.Trim().Length, 500)],
            Status = PaymentClaimStatus.Pending, CreatedAt = DateTime.UtcNow
        };
        _context.PaymentClaims.Add(claim);

        var leaders = await _context.GroupMembers.AsNoTracking()
            .Where(m => m.GroupId == groupId && MemberAccess.LeaderRoles.Contains(m.Role)
                        && MemberAccess.ActiveStatuses.Contains(m.Status))
            .Select(m => m.Id).ToListAsync(ct);
        await _notify.NotifyAsync(groupId, leaders, "PaymentClaimed", "Dai jipya la malipo",
            $"Mwanachama {me.MemberNumber} anadai amelipa {amount:N0} (kumbukumbu {r}). Hakiki kwenye sanduku la malipo.",
            "/hazina/malipo", ct);
        await _context.SaveChangesAsync(ct);
        return claim;
    }

    /// <summary>Mwanachama anaona madai yake; viongozi wanaona yote.</summary>
    public async Task<List<object>> ClaimsAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        var me = await MemberAccess.RequireActiveAsync(_context, groupId, userId, ct, tracking: false);
        var leader = MemberAccess.IsEffectiveLeader(me);

        var q = _context.PaymentClaims.AsNoTracking().Where(c => c.GroupId == groupId);
        if (!leader) q = q.Where(c => c.GroupMemberId == me.Id);

        var rows = await q.OrderByDescending(c => c.CreatedAt).Take(200).ToListAsync(ct);
        var labels = await MemberLabelsAsync(groupId, rows.Select(c => c.GroupMemberId).Distinct().ToList(), ct);

        return rows.Select(c => (object)new
        {
            id = c.Id, memberId = c.GroupMemberId,
            memberNumber = labels.TryGetValue(c.GroupMemberId, out var l) ? l.Number : null,
            name = labels.TryGetValue(c.GroupMemberId, out var l2) ? l2.Name : null,
            amount = c.Amount, reference = c.Reference, paymentCode = c.PaymentCode,
            status = c.Status.ToString(), note = c.DecisionNote, createdAt = c.CreatedAt, decidedAt = c.DecidedAt,
            // SASISHO21: mwanachama anajua lipi ni lake bila kupokea orodha ya wengine.
            mine = c.GroupMemberId == me.Id
        }).ToList();
    }

    /// <summary>
    /// Kiongozi anahakiki dai: ama analinganisha na mstari wa statement (na kuthibitisha risiti),
    /// au anakataa. Dai lililothibitishwa haliongezi pesa yoyote mpya — linaunganisha tu.
    /// </summary>
    public async Task<object> DecideClaimAsync(Guid groupId, Guid claimId, bool approve, string? note, Guid? receiptId, Guid userId, CancellationToken ct = default)
    {
        await MemberAccess.RequireEffectiveLeaderAsync(_context, groupId, userId, ct, "Kuhakiki madai ya malipo ni kazi ya viongozi.");

        var claim = await _context.PaymentClaims.FirstOrDefaultAsync(c => c.Id == claimId && c.GroupId == groupId, ct)
                    ?? throw new NotFoundException("Dai halipo.");
        if (claim.Status != PaymentClaimStatus.Pending) throw new ConflictException($"Dai hili tayari ni {claim.Status}.");

        claim.DecidedByUserId = userId;
        claim.DecidedAt = DateTime.UtcNow;
        claim.DecisionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim()[..Math.Min(note.Trim().Length, 500)];

        if (!approve)
        {
            claim.Status = PaymentClaimStatus.Rejected;
            await _notify.NotifyAsync(groupId, new[] { claim.GroupMemberId }, "PaymentClaimRejected",
                "Dai lako la malipo halijathibitishwa",
                $"Dai la {claim.Amount:N0} (kumbukumbu {claim.Reference}) halijathibitishwa. " +
                (string.IsNullOrWhiteSpace(claim.DecisionNote) ? "" : $"Sababu: {claim.DecisionNote}"), "/", ct);
            await _context.SaveChangesAsync(ct);
            return new { status = "Imekataliwa" };
        }

        // Kuthibitisha dai BILA mstari wa statement = pesa haijaonekana. Hairuhusiwi.
        if (receiptId is null)
            throw new ValidationException("Hakiki kwanza mstari wa statement unaolingana na dai hili, kisha uthibitishe risiti. " +
                                          "Dai peke yake haliwezi kuwa malipo.");

        claim.Status = PaymentClaimStatus.Verified;
        claim.ReceiptId = receiptId;
        await _context.SaveChangesAsync(ct);
        return new { status = "Imehakikiwa", receiptId };
    }

    // ==================================================================
    // 5. Msaidizi
    // ==================================================================

    private async Task<Dictionary<Guid, (string? Number, string? Name)>> MemberLabelsAsync(
        Guid groupId, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return new();
        var members = await _context.GroupMembers.AsNoTracking()
            .Where(m => m.GroupId == groupId && ids.Contains(m.Id))
            .Select(m => new { m.Id, m.MemberNumber, m.UserId })
            .ToListAsync(ct);
        var users = await _context.Users.AsNoTracking()
            .Where(u => members.Select(m => m.UserId).Contains(u.Id))
            .Select(u => new { u.Id, u.FullName })
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        return members.ToDictionary(m => m.Id, m => ((string?)m.MemberNumber, users.GetValueOrDefault(m.UserId)));
    }

    /// <summary>Rejea ya audit inayotumika kwenye risiti (inaonekana kwenye kumbukumbu).</summary>
    public static string Describe(PaymentReceipt r)
        => JsonSerializer.Serialize(new { r.Reference, r.Amount, r.Status, r.MatchRule });
}
