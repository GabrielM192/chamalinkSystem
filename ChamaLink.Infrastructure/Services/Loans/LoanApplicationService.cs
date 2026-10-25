using System.Text.Json;
using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Exceptions;
using ChamaLink.Domain.Localization;
using ChamaLink.Infrastructure.Services.Benefits;
using ChamaLink.Infrastructure.Services.Common;
using ChamaLink.Infrastructure.Services.Truth;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Infrastructure.Services.Loans;

/// <summary>
/// SASISHO15 (AWAMU L) — mtiririko PEKEE wa kuzaa mkopo:
///   1. Mwanachama anaomba (au kiongozi kwa niaba ya mwanachama asiye na akaunti — kiongozi huyo hapigi kura).
///   2. Mfumo unakagua sifa kutoka SERA YA KIKUNDI (TruthGroupSettings.Loan*): kiasi, malimbikizo, mkopo mwingine,
///      uanachama, kikomo cha michango, na PESA ILIYOPO kwenye M-Koba (ukiondoa mafao/mikopo inayosubiri kulipwa).
///   3. Viongozi huru wanapiga kura (si mkopaji, si aliyeomba, si ndugu waliosajiliwa). Kura zikifika, sifa zinakaguliwa upya.
///   4. Mtunza-hazina anaanzisha utoaji (pesa inakaguliwa tena).
///   5. Kiongozi MWINGINE anathibitisha kwa rejea ya UTOAJI wa M-Koba: kiasi sawa, baada ya idhini, kwa namba ya mkopaji,
///      rejea haijatumika kwingine. Hapo tu ndipo Loan inazaliwa (DisbursedAt = muda wa M-Koba) na Injini ya Ukweli inaiona.
/// Riba na idadi ya miezi vinagandishwa kwenye ombi kutoka kwenye sera — hakuna anayeweza kuweka riba 0% peke yake.
/// </summary>
public class LoanApplicationService
{
    private readonly ApplicationDbContext _context;
    private readonly TruthLedgerService _truth;
    private readonly NotificationService _notify;
    private readonly BenefitService _benefits;

    public LoanApplicationService(ApplicationDbContext context, TruthLedgerService truth, NotificationService notify, BenefitService benefits)
    {
        _context = context; _truth = truth; _notify = notify; _benefits = benefits;
    }

    public const string VoteSubject = "LoanApplication";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public sealed record Check(string Code, bool Passed, string Message, string? Tpl = null, string[]? TArgs = null)
    {
        public static Check Of(string code, bool passed, Loc msg) => new(code, passed, msg.Text, msg.Template, msg.Args.ToArray());
    }

    public sealed record Assessment(bool Eligible, List<Check> Checks, decimal Amount, decimal InterestPercent, decimal InterestAmount,
        decimal TotalRepayable, int RepaymentMonths, decimal MonthlyInstallment, int RequiredApprovals, decimal? MaxAllowed,
        BenefitService.CashPosition? Cash);

    public sealed class ApplyDto
    {
        public Guid? BorrowerMemberId { get; set; }
        public decimal Amount { get; set; }
        public string? Purpose { get; set; }
    }

    // =====================================================================
    // Msaada
    // =====================================================================
    private async Task<GroupMember> RequireMemberAsync(Guid groupId, Guid userId, CancellationToken ct)
        => await MemberAccess.RequireActiveAsync(_context, groupId, userId, ct);

    private Task<GroupMember> RequireLeaderAsync(Guid groupId, Guid userId, CancellationToken ct)
        => MemberAccess.RequireEffectiveLeaderAsync(_context, groupId, userId, ct); // SASISHO17: hai + nje ya kipindi cha kusubiri

    private Task Audit(Guid groupId, Guid? memberId, Guid actor, AuditAction action, string type, Guid id, object? before, object after, string? reference, string? reason, CancellationToken ct)
        => AuditTrail.AddAsync(_context, groupId, memberId, actor, action, type, id, before, after, reference, reason, ct);

    private static string PhoneKey(string? phone)
    {
        var d = new string((phone ?? string.Empty).Where(char.IsDigit).ToArray());
        return d.Length > 9 ? d[^9..] : d;
    }

    private static DateTime EatDate(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), MkobaPlatform.ResolveTimeZone()).Date;

    private async Task<string> MemberName(Guid memberId, CancellationToken ct)
        => await _context.GroupMembers.AsNoTracking().Where(m => m.Id == memberId).Select(m => m.User != null ? m.User.FullName : m.MemberNumber).FirstOrDefaultAsync(ct) ?? "?";

    private async Task<HashSet<Guid>> RelatedAsync(Guid groupId, Guid memberId, CancellationToken ct)
    {
        var rows = await _context.MemberRelations.AsNoTracking()
            .Where(r => r.GroupId == groupId && (r.Status == MemberRelationStatus.Active || r.Status == MemberRelationStatus.EndPending)
                        && (r.MemberAId == memberId || r.MemberBId == memberId))
            .Select(r => new { r.MemberAId, r.MemberBId }).ToListAsync(ct);
        return rows.Select(r => r.MemberAId == memberId ? r.MemberBId : r.MemberAId).ToHashSet();
    }

    private async Task<string> NextNumberAsync(Guid groupId, int year, CancellationToken ct)
    {
        var code = await _context.Groups.Where(g => g.Id == groupId).Select(g => g.ShortCode).FirstOrDefaultAsync(ct);
        var prefix = $"{(string.IsNullOrWhiteSpace(code) ? "GRP" : code)}-MK-{year}-";
        var nums = await _context.LoanApplications.Where(c => c.GroupId == groupId && c.ApplicationNumber.StartsWith(prefix)).Select(c => c.ApplicationNumber).ToListAsync(ct);
        var max = nums.Select(n => int.TryParse(n[prefix.Length..], out var x) ? x : 0).DefaultIfEmpty(0).Max();
        return $"{prefix}{max + 1:000}";
    }

    /// <summary>Wapiga kura: viongozi hai, si mkopaji, si aliyeomba, si ndugu wa mkopaji (uhusiano ulioidhinishwa).</summary>
    private async Task<List<GroupMember>> EligibleVotersAsync(LoanApplication a, CancellationToken ct)
    {
        var related = await RelatedAsync(a.GroupId, a.BorrowerGroupMemberId, ct);
        return (await _context.GroupMembers.AsNoTracking()
            .Where(m => m.GroupId == a.GroupId && NotificationService.InGroupStatuses.Contains(m.Status) && NotificationService.LeaderRoles.Contains(m.Role)
                        && m.Id != a.BorrowerGroupMemberId && m.UserId != a.RequestedByUserId && !related.Contains(m.Id))
            .ToListAsync(ct)).Where(m => MemberAccess.IsEffectiveLeader(m)).ToList(); // SASISHO17: kipindi cha kusubiri
    }

    // =====================================================================
    // 1. SERA
    // =====================================================================
    public async Task<object> PolicyAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        var me = await RequireMemberAsync(groupId, userId, ct);
        var isLeader = MemberAccess.IsEffectiveLeader(me);
        // SASISHO16: "kuzima mikopo" = hakuna mikopo MIPYA. Mikopo iliyopo (na historia ya maombi) bado inahudumiwa
        // na Injini ya Ukweli, kwa hiyo ukurasa wa Mikopo unaonekana kama kuna shughuli yoyote ya mikopo.
        var hasActivity = await _context.Loans.AsNoTracking().AnyAsync(l => l.GroupId == groupId, ct)
                          || await _context.LoanApplications.AsNoTracking().AnyAsync(a => a.GroupId == groupId, ct);
        var s = await _context.TruthGroupSettings.AsNoTracking().FirstOrDefaultAsync(x => x.GroupId == groupId, ct);
        if (s is null)
            return new { configured = false, enabled = false, loansEnabled = false, hasActivity, visible = hasActivity, isLeader,
                missing = isLeader ? new List<string> { "Mipangilio ya kikundi haijawekwa" } : new List<string>() };
        var missing = MissingPolicy(s);
        var enabled = s.LoansEnabled && missing.Count == 0;
        return new
        {
            configured = missing.Count == 0, enabled, loansEnabled = s.LoansEnabled, hasActivity, visible = enabled || hasActivity, isLeader,
            // SASISHO16: orodha ya kinachokosekana kwenye sera ni kazi ya viongozi — mwanachama haioni
            missing = isLeader ? missing : new List<string>(),
            interestPercent = s.LoanInterestPercent, repaymentMonths = s.LoanRepaymentMonths, maxAmount = s.LoanMaxAmount,
            maxMultipleOfContributions = s.LoanMaxMultipleOfContributions, minMembershipMonths = s.LoanMinMembershipMonths,
            allowWithArrears = s.LoanAllowWithArrears, allowMultipleActive = s.LoanAllowMultipleActive, minCashReserve = s.LoanMinCashReserve,
            requiredApprovals = s.LoanRequiredApprovals, approvalValidDays = s.LoanApprovalValidDays, latePenaltyAmount = s.LoanLatePenaltyAmount
        };
    }

    public static List<string> MissingPolicy(TruthGroupSettings s)
    {
        var m = new List<string>();
        if (s.LoanInterestPercent is null) m.Add("riba kwa asilimia (weka 0 kama hakuna riba)");
        if (s.LoanRepaymentMonths is null or < 1) m.Add("miezi ya kurejesha");
        if (s.LoanRequiredApprovals is null or < 1) m.Add("kura za viongozi zinazohitajika");
        if (s.LoanApprovalValidDays is null or < 1) m.Add("siku ambazo idhini inadumu kabla ya utoaji");
        if (s.LoanMaxAmount is null && s.LoanMaxMultipleOfContributions is null) m.Add("kikomo cha mkopo (kiasi cha juu au mara ya michango)");
        return m;
    }

    // =====================================================================
    // 2. SIFA (hakuna kinachohifadhiwa)
    // =====================================================================
    public async Task<Assessment> AssessAsync(Guid groupId, Guid borrowerId, decimal amount, Guid? excludeApplicationId, CancellationToken ct = default)
    {
        var checks = new List<Check>();
        var s = await _context.TruthGroupSettings.AsNoTracking().FirstOrDefaultAsync(x => x.GroupId == groupId, ct)
                ?? throw new PolicyNotConfiguredException(groupId, "Kikundi hakijaweka mipangilio ya fedha. Viongozi waweke kanuni kwanza.");
        var missing = MissingPolicy(s);
        checks.Add(Check.Of("POLICY", missing.Count == 0, missing.Count == 0
            ? Loc.L($"Sera ya mikopo imekamilika.")
            : Loc.L($"Sera ya mikopo haijakamilika: {string.Join("; ", missing)}.")));
        checks.Add(Check.Of("LOANS_ENABLED", s.LoansEnabled, s.LoansEnabled ? Loc.L($"Mikopo imefunguliwa na viongozi.") : Loc.L($"Mikopo haijafunguliwa kwenye kikundi hiki.")));
        checks.Add(Check.Of("AMOUNT", amount > 0, amount > 0 ? Loc.L($"Kiasi kinachoombwa: {amount:N0}.") : Loc.L($"Kiasi lazima kiwe zaidi ya sifuri.")));

        var borrower = await _context.GroupMembers.AsNoTracking().FirstOrDefaultAsync(m => m.Id == borrowerId && m.GroupId == groupId, ct)
                       ?? throw new NotFoundException("Mkopaji si mwanachama wa kikundi hiki.");
        var active = NotificationService.InGroupStatuses.Contains(borrower.Status);
        checks.Add(Check.Of("BORROWER_STATUS", active, active ? Loc.L($"Mkopaji yuko kikundini.") : Loc.L($"Mkopaji hayuko kikundini (hali: {borrower.Status}).")));

        var nowEat = TruthLedgerService.NowEat();
        var t = await _truth.GetMemberTruthAsync(borrowerId, null, ct);
        var st = t.Statement;

        if (s.LoanMinMembershipMonths is int minMonths)
        {
            var months = (nowEat.Year - t.EffectiveJoin.Year) * 12 + nowEat.Month - t.EffectiveJoin.Month - (nowEat.Day < t.EffectiveJoin.Day ? 1 : 0);
            checks.Add(Check.Of("MIN_MEMBERSHIP", months >= minMonths, Loc.L($"Uanachama: miezi {months} (sera inahitaji angalau {minMonths}).")));
        }

        if (!s.LoanAllowWithArrears)
        {
            var debt = st.Arrears + st.JoinFeeOutstanding + st.LoanOverdue;
            checks.Add(Check.Of("NO_ARREARS", debt <= 0, debt <= 0
                ? Loc.L($"Hana malimbikizo wala Kianzio kinachodaiwa.")
                : Loc.L($"Ana deni la {debt:N0} (malimbikizo {st.Arrears:N0}, Kianzio {st.JoinFeeOutstanding:N0}, mkopo uliochelewa {st.LoanOverdue:N0}). Sera hairuhusu kukopa ukiwa na deni.")));
        }

        var openApps = await _context.LoanApplications.AsNoTracking().Where(a => a.GroupId == groupId && a.BorrowerGroupMemberId == borrowerId && a.Id != excludeApplicationId
                && (a.Status == LoanApplicationStatus.Voting || a.Status == LoanApplicationStatus.Approved || a.Status == LoanApplicationStatus.DisbursementInitiated))
            .Select(a => a.ApplicationNumber).ToListAsync(ct);
        if (!s.LoanAllowMultipleActive)
        {
            var outstanding = st.LoanOutstanding;
            var ok = outstanding <= 0 && openApps.Count == 0;
            checks.Add(Check.Of("NO_ACTIVE_LOAN", ok, ok
                ? Loc.L($"Hana mkopo mwingine unaodaiwa wala ombi jingine.")
                : outstanding > 0 ? Loc.L($"Ana mkopo unaodaiwa ({outstanding:N0}). Sera inaruhusu mkopo mmoja kwa wakati.")
                                  : Loc.L($"Ana ombi jingine la mkopo ambalo halijafungwa: {string.Join(", ", openApps)}.")));
        }

        decimal? maxAllowed = s.LoanMaxAmount;
        if (s.LoanMaxMultipleOfContributions is decimal mult)
        {
            var byContrib = Math.Round(st.TotalContributionPaid * mult, 2);
            maxAllowed = maxAllowed is null ? byContrib : Math.Min(maxAllowed.Value, byContrib);
            checks.Add(Check.Of("MAX_MULTIPLE", amount <= byContrib, Loc.L($"Kikomo kwa michango: mara {mult:0.##} ya michango aliyolipa ({st.TotalContributionPaid:N0}) = {byContrib:N0}.")));
        }
        if (s.LoanMaxAmount is decimal maxAmt)
            checks.Add(Check.Of("MAX_AMOUNT", amount <= maxAmt, Loc.L($"Kikomo cha mkopo mmoja: {maxAmt:N0}.")));

        var cash = await _benefits.CashPositionAsync(groupId, null, amount, ct, excludeLoanApplicationId: excludeApplicationId);
        var afterLoan = cash.Available is { } av ? av - amount : (decimal?)null;
        // SASISHO17 (P0-4): salio la zamani (au sera ya umri haijawekwa) halihesabiwi kama pesa iliyopo
        var cashOk = !cash.Stale && afterLoan is { } al && al >= s.LoanMinCashReserve;
        checks.Add(Check.Of("CASH_AVAILABLE", cashOk, cash.Available is null || cash.Stale
            ? BenefitService.CashShortReason(cash)
            : Loc.L($"Pesa iliyopo {cash.Available:N0} (salio {cash.MkobaBalance:N0} − zinazosubiri kulipwa {cash.Reserved:N0}); baada ya mkopo {afterLoan:N0}; akiba ya lazima {s.LoanMinCashReserve:N0}.")));

        var rate = s.LoanInterestPercent ?? 0m;
        var months2 = s.LoanRepaymentMonths ?? 0;
        var interest = Math.Round(amount * rate / 100m, 0, MidpointRounding.AwayFromZero);
        var total = amount + interest;
        var inst = months2 > 0 ? Math.Round(total / months2, 0, MidpointRounding.AwayFromZero) : total;
        return new Assessment(checks.All(c => c.Passed), checks, amount, rate, interest, total, months2, inst, s.LoanRequiredApprovals ?? 0, maxAllowed, cash);
    }

    private static Exception Failed(string title, IEnumerable<Check> failed)
        => new ConflictException(Loc.L($"{title} {string.Join(" ", failed.Select(f => f.Message))}"));

    // =====================================================================
    // 3. KUOMBA
    // =====================================================================
    public async Task<Assessment> PreviewAsync(Guid groupId, ApplyDto dto, Guid userId, CancellationToken ct = default)
    {
        var borrower = await ResolveBorrowerAsync(groupId, dto, userId, ct);
        return await AssessAsync(groupId, borrower.Id, dto.Amount, null, ct);
    }

    private async Task<GroupMember> ResolveBorrowerAsync(Guid groupId, ApplyDto dto, Guid userId, CancellationToken ct)
    {
        var me = await RequireMemberAsync(groupId, userId, ct);
        if (dto.BorrowerMemberId is null || dto.BorrowerMemberId == me.Id) return me;
        if (!MemberAccess.IsEffectiveLeader(me))
            throw new ForbiddenException("Mwanachama anaomba mkopo kwa ajili yake mwenyewe tu.");
        return await _context.GroupMembers.AsNoTracking().FirstOrDefaultAsync(m => m.Id == dto.BorrowerMemberId && m.GroupId == groupId, ct)
               ?? throw new NotFoundException("Mkopaji si mwanachama wa kikundi hiki.");
    }

    public async Task<LoanApplication> ApplyAsync(Guid groupId, ApplyDto dto, Guid userId, CancellationToken ct = default)
    {
        var borrower = await ResolveBorrowerAsync(groupId, dto, userId, ct);
        if (dto.Purpose is { Length: > 500 }) throw new ValidationException("Maelezo ya matumizi yasizidi herufi 500.");
        await using var tx = await DbTx.BeginAsync(_context, ct);
        await DbTx.LockAsync(_context, groupId, DbTx.LoanApplicationOpen, ct);
        var a = await AssessAsync(groupId, borrower.Id, dto.Amount, null, ct);
        if (!a.Eligible) throw Failed("Ombi la mkopo haliwezi kuwasilishwa:", a.Checks.Where(c => !c.Passed));

        var app = new LoanApplication
        {
            Id = Guid.NewGuid(), GroupId = groupId, ApplicationNumber = await NextNumberAsync(groupId, TruthLedgerService.NowEat().Year, ct),
            BorrowerGroupMemberId = borrower.Id, RequestedByUserId = userId, Amount = dto.Amount, Purpose = dto.Purpose?.Trim(),
            InterestPercent = a.InterestPercent, InterestAmount = a.InterestAmount, RepaymentMonths = a.RepaymentMonths,
            RequiredApprovals = a.RequiredApprovals, EligibilityJson = JsonSerializer.Serialize(a.Checks, Json), Status = LoanApplicationStatus.Voting
        };
        var voters = await EligibleVotersAsync(app, ct);
        if (voters.Count < app.RequiredApprovals)
            throw new ConflictException(Loc.L($"Hakuna viongozi huru wa kutosha kupiga kura: wanaoruhusiwa {voters.Count}, sera inahitaji {app.RequiredApprovals}. Mkopaji, aliyeomba na ndugu zake waliosajiliwa hawapigi kura — teua naibu kiongozi au mwanachama aombe mwenyewe."));
        _context.LoanApplications.Add(app);
        await Audit(groupId, borrower.Id, userId, AuditAction.LoanApplicationSubmitted, nameof(LoanApplication), app.Id, null, Snap(app), app.ApplicationNumber, app.Purpose, ct);
        var who = await MemberName(borrower.Id, ct);
        await _notify.NotifyAsync(groupId, voters.Select(v => v.Id), "Loan", Loc.L($"Ombi la mkopo {app.ApplicationNumber}"),
            Loc.L($"{who} ameomba mkopo wa {app.Amount:N0} (riba {app.InterestPercent:0.##}%, miezi {app.RepaymentMonths}). Pitia na upige kura."), $"/mikopo?ombi={app.Id}", ct);
        await _context.SaveChangesAsync(ct);
        await DbTx.CommitAsync(tx, ct);
        return app;
    }

    private static object Snap(LoanApplication a) => new
    {
        a.ApplicationNumber, a.BorrowerGroupMemberId, a.Amount, a.InterestPercent, a.InterestAmount, a.RepaymentMonths, a.RequiredApprovals,
        Status = a.Status.ToString(), a.MkobaReference, a.LoanId
    };

    private async Task<LoanApplication> LoadAsync(Guid groupId, Guid id, CancellationToken ct)
        => await _context.LoanApplications.FirstOrDefaultAsync(x => x.Id == id && x.GroupId == groupId, ct) ?? throw new NotFoundException("Ombi la mkopo halipo.");

    /// <summary>Idhini iliyopitwa na muda (haikutolewa ndani ya siku za sera) inaisha yenyewe — pesa haitoki kwa idhini ya zamani.</summary>
    private async Task<bool> ExpireIfStaleAsync(LoanApplication a, Guid userId, CancellationToken ct)
    {
        if (a.Status != LoanApplicationStatus.Approved || a.ApprovalExpiresAt is null || a.ApprovalExpiresAt >= DateTime.UtcNow) return false;
        a.Status = LoanApplicationStatus.Expired; a.ClosedAt = DateTime.UtcNow; a.CloseReason = "Idhini imeisha muda kabla ya utoaji";
        await Audit(a.GroupId, a.BorrowerGroupMemberId, userId, AuditAction.LoanApplicationExpired, nameof(LoanApplication), a.Id, null, Snap(a), a.ApplicationNumber, a.CloseReason, ct);
        await _context.SaveChangesAsync(ct);
        return true;
    }

    // =====================================================================
    // 4. KURA
    // =====================================================================
    /// <summary>SASISHO17 (P0-3): kura + ukaguzi wa pesa wakati wa idhini ndani ya kufuli za Decision + CashCommitment.</summary>
    public Task<LoanApplication> VoteAsync(Guid groupId, Guid id, bool approve, string? reason, Guid userId, CancellationToken ct = default)
        => DbTx.SerializedAsync(_context, groupId, new[] { DbTx.Decision, DbTx.CashCommitment }, () => VoteCoreAsync(groupId, id, approve, reason, userId, ct), ct);

    private async Task<LoanApplication> VoteCoreAsync(Guid groupId, Guid id, bool approve, string? reason, Guid userId, CancellationToken ct)
    {
        var me = await RequireLeaderAsync(groupId, userId, ct);
        var a = await LoadAsync(groupId, id, ct);
        if (a.Status != LoanApplicationStatus.Voting) throw new ConflictException("Ombi hili haliko kwenye kura.");
        if (me.Id == a.BorrowerGroupMemberId) throw new ForbiddenException("Mkopaji HAWEZI kupiga kura kwenye mkopo wake mwenyewe.");
        if (a.RequestedByUserId == userId) throw new ForbiddenException("Aliyewasilisha ombi hapigi kura juu yake.");
        var voters = await EligibleVotersAsync(a, ct);
        if (voters.All(v => v.Id != me.Id)) throw new ForbiddenException("Wewe ni ndugu wa mkopaji (mgongano wa maslahi) — huwezi kupiga kura.");
        if (!approve && string.IsNullOrWhiteSpace(reason)) throw new ValidationException("Eleza sababu ya kukataa.");
        if (await _context.GovernanceVotes.AnyAsync(v => v.SubjectType == VoteSubject && v.SubjectId == a.Id && v.GroupMemberId == me.Id, ct))
            throw new ConflictException("Umeshapiga kura kwenye ombi hili.");
        _context.GovernanceVotes.Add(new GovernanceVote
        {
            Id = Guid.NewGuid(), GroupId = groupId, SubjectType = VoteSubject, SubjectId = a.Id, GroupMemberId = me.Id, UserId = userId,
            Approve = approve, Reason = reason?.Trim(), RoleAtVote = MemberAccess.EffectiveRole(me)
        });
        await Audit(groupId, a.BorrowerGroupMemberId, userId, AuditAction.VoteCast, nameof(LoanApplication), a.Id, null, new { Approve = approve, Reason = reason }, a.ApplicationNumber, reason, ct);

        var votes = await _context.GovernanceVotes.AsNoTracking().Where(v => v.SubjectType == VoteSubject && v.SubjectId == a.Id).ToListAsync(ct);
        var yes = votes.Count(v => v.Approve) + (approve ? 1 : 0);
        var no = votes.Count(v => !v.Approve) + (approve ? 0 : 1);
        var who = await MemberName(a.BorrowerGroupMemberId, ct);
        if (yes >= a.RequiredApprovals)
        {
            // Sifa zinakaguliwa UPYA wakati wa idhini (deni/pesa vinaweza kuwa vimebadilika tangu ombi)
            var re = await AssessAsync(groupId, a.BorrowerGroupMemberId, a.Amount, a.Id, ct);
            if (re.Eligible)
            {
                var s = await _context.TruthGroupSettings.AsNoTracking().FirstAsync(x => x.GroupId == groupId, ct);
                a.Status = LoanApplicationStatus.Approved; a.ApprovedAt = DateTime.UtcNow;
                a.ApprovalExpiresAt = DateTime.UtcNow.AddDays(s.LoanApprovalValidDays ?? 0);
                await Audit(groupId, a.BorrowerGroupMemberId, userId, AuditAction.LoanApplicationApproved, nameof(LoanApplication), a.Id, null, Snap(a), a.ApplicationNumber, null, ct);
                await _notify.NotifyAsync(groupId, new[] { a.BorrowerGroupMemberId }, "Loan", Loc.L($"Mkopo {a.ApplicationNumber} umeidhinishwa"),
                    Loc.L($"Ombi lako la {a.Amount:N0} limeidhinishwa na viongozi {yes}. Mtunza-hazina atatuma pesa kupitia M-Koba."), $"/mikopo?ombi={a.Id}", ct);
                await _notify.NotifyLeadersAsync(groupId, "Loan", Loc.L($"Mkopo {a.ApplicationNumber}: anzisha utoaji"),
                    Loc.L($"{who}: {a.Amount:N0} umeidhinishwa. Mtunza-hazina atume kupitia M-Koba, kisha kiongozi mwingine athibitishe kwa rejea."), $"/mikopo?ombi={a.Id}", ct, GroupRole.Treasurer);
            }
            else
            {
                a.Status = LoanApplicationStatus.Rejected; a.ClosedAt = DateTime.UtcNow;
                a.CloseReason = Cut("Kura zimetosha lakini masharti hayakutimia wakati wa idhini: " + string.Join(" ", re.Checks.Where(c => !c.Passed).Select(c => c.Message)), 500);
                a.EligibilityJson = JsonSerializer.Serialize(re.Checks, Json);
                await Audit(groupId, a.BorrowerGroupMemberId, userId, AuditAction.LoanApplicationRejected, nameof(LoanApplication), a.Id, null, Snap(a), a.ApplicationNumber, a.CloseReason, ct);
                await _notify.NotifyAsync(groupId, new[] { a.BorrowerGroupMemberId }, "Loan", Loc.L($"Mkopo {a.ApplicationNumber} haukuidhinishwa"), Loc.L($"{a.CloseReason}"), $"/mikopo?ombi={a.Id}", ct);
            }
        }
        else if (voters.Count - no < a.RequiredApprovals)
        {
            a.Status = LoanApplicationStatus.Rejected; a.ClosedAt = DateTime.UtcNow; a.CloseReason = Cut($"Limekataliwa na viongozi ({no}): {reason}", 500);
            await Audit(groupId, a.BorrowerGroupMemberId, userId, AuditAction.LoanApplicationRejected, nameof(LoanApplication), a.Id, null, Snap(a), a.ApplicationNumber, a.CloseReason, ct);
            await _notify.NotifyAsync(groupId, new[] { a.BorrowerGroupMemberId }, "Loan", Loc.L($"Mkopo {a.ApplicationNumber} umekataliwa"), Loc.L($"Sababu: {reason}"), $"/mikopo?ombi={a.Id}", ct);
        }
        await _context.SaveChangesAsync(ct);
        return a;
    }

    private static string Cut(string s, int n) => s.Length <= n ? s : s[..n];

    // =====================================================================
    // 5. UTOAJI (hatua 2: kuanzisha → kuthibitisha kwa M-Koba)
    // =====================================================================
    /// <summary>
    /// Anayeanzisha: Mtunza-hazina asiye mkopaji/ndugu. Kama hakuna Mtunza-hazina huru (mf. yeye ndiye mkopaji na hakuna naibu),
    /// Mwenyekiti huru anaruhusiwa — kesi haikwami (somo la F-M3), na bado mthibitishaji ni mtu mwingine.
    /// </summary>
    public async Task<(bool Can, string? Why)> CanInitiateAsync(LoanApplication a, GroupMember me, CancellationToken ct)
    {
        if (a.Status != LoanApplicationStatus.Approved) return (false, "Ombi halijaidhinishwa (au tayari limeanzishwa).");
        if (me.Id == a.BorrowerGroupMemberId) return (false, "Mkopaji hawezi kujitolea pesa mwenyewe.");
        var related = await RelatedAsync(a.GroupId, a.BorrowerGroupMemberId, ct);
        if (related.Contains(me.Id)) return (false, "Wewe ni ndugu wa mkopaji (mgongano wa maslahi).");
        var myRole = MemberAccess.EffectiveRole(me);
        if (myRole == GroupRole.Treasurer) return (true, null);
        var treasurers = await _context.GroupMembers.AsNoTracking().Where(m => m.GroupId == a.GroupId && m.Role == GroupRole.Treasurer
            && NotificationService.InGroupStatuses.Contains(m.Status) && m.Id != a.BorrowerGroupMemberId && !related.Contains(m.Id)).ToListAsync(ct);
        var freeTreasurer = treasurers.Any(m => MemberAccess.IsEffectiveLeader(m));
        if (myRole == GroupRole.Chairperson && !freeTreasurer) return (true, null);
        return (false, "Utoaji unaanzishwa na Mtunza-hazina (Mwenyekiti tu kama hakuna Mtunza-hazina huru).");
    }

    public async Task<LoanApplication> InitiateDisbursementAsync(Guid groupId, Guid id, Guid userId, CancellationToken ct = default)
    {
        var me = await RequireLeaderAsync(groupId, userId, ct);
        var a = await LoadAsync(groupId, id, ct);
        if (await ExpireIfStaleAsync(a, userId, ct)) throw new ConflictException("Idhini ya ombi hili imeisha muda kabla ya utoaji. Mwanachama aombe upya.");
        var (can, why) = await CanInitiateAsync(a, me, ct);
        if (!can) throw a.Status == LoanApplicationStatus.Approved ? new ForbiddenException(why!) : new ConflictException(why!);
        await using var tx = await DbTx.BeginAsync(_context, ct);
        await DbTx.LockAsync(_context, groupId, DbTx.LoanDisbursement, ct);
        await DbTx.LockAsync(_context, groupId, DbTx.CashCommitment, ct); // SASISHO17 (P0-3): mpangilio wa kupanda (0x3A9B < 0x3A9D)
        if (_context.Database.IsRelational()) await _context.Entry(a).ReloadAsync(ct);
        if (a.Status != LoanApplicationStatus.Approved) throw new ConflictException("Ombi halijaidhinishwa (au tayari limeanzishwa).");
        var re = await AssessAsync(groupId, a.BorrowerGroupMemberId, a.Amount, a.Id, ct);
        var cash = re.Checks.First(c => c.Code == "CASH_AVAILABLE");
        if (!cash.Passed) throw new ConflictException(Loc.L($"Pesa haitoshi kwa sasa: {cash.Message}"));
        a.Status = LoanApplicationStatus.DisbursementInitiated; a.DisbursementInitiatedByUserId = userId; a.DisbursementInitiatedAt = DateTime.UtcNow;
        await Audit(groupId, a.BorrowerGroupMemberId, userId, AuditAction.LoanDisbursementInitiated, nameof(LoanApplication), a.Id, null, Snap(a), a.ApplicationNumber, null, ct);
        await _notify.NotifyLeadersAsync(groupId, "Loan", Loc.L($"Mkopo {a.ApplicationNumber}: thibitisha utoaji"),
            Loc.L($"Utoaji wa {a.Amount:N0} umeanzishwa. Pesa ikionekana M-Koba, kiongozi MWINGINE athibitishe kwa rejea ya utoaji."), $"/mikopo?ombi={a.Id}", ct);
        await _context.SaveChangesAsync(ct);
        await DbTx.CommitAsync(tx, ct);
        return a;
    }

    public async Task<LoanApplication> ConfirmDisbursementAsync(Guid groupId, Guid id, string mkobaReference, bool acknowledgeOtherRecipient, string? note, Guid userId, CancellationToken ct = default)
    {
        var me = await RequireLeaderAsync(groupId, userId, ct);
        var a = await LoadAsync(groupId, id, ct);
        if (a.Status != LoanApplicationStatus.DisbursementInitiated) throw new ConflictException("Utoaji lazima uanzishwe kwanza na Mtunza-hazina.");
        if (me.Id == a.BorrowerGroupMemberId) throw new ForbiddenException("Mkopaji hawezi kuthibitisha mkopo wake mwenyewe.");
        if (a.DisbursementInitiatedByUserId == userId) throw new ForbiddenException("Aliyeanzisha utoaji HAWEZI kuuthibitisha — mthibitishaji lazima awe mtu mwingine.");
        if ((await RelatedAsync(groupId, a.BorrowerGroupMemberId, ct)).Contains(me.Id)) throw new ForbiddenException("Wewe ni ndugu wa mkopaji (mgongano wa maslahi).");

        var reference = (mkobaReference ?? string.Empty).Trim().ToUpperInvariant();
        if (reference.Length == 0) throw new ValidationException("Weka reference ya M-Koba ya utoaji.");
        var row = await _context.MkobaRawTransactions.AsNoTracking().FirstOrDefaultAsync(t => t.GroupId == groupId && t.Reference == reference, ct)
                  ?? throw new NotFoundException($"Reference {reference} haipo kwenye PDF za M-Koba zilizoingizwa. Ingiza PDF yenye muamala huo kwanza.");
        if (!row.IsWithdrawal) throw new ConflictException($"{reference} ni pesa iliyoingia (Deposit), si utoaji.");
        var used = await WithdrawalRegistry.UsedByOtherAsync(_context, groupId, reference, a.Id, ct);
        if (used is not null) throw new ConflictException($"Utoaji {reference} tayari unaeleza {used.Label}.");
        if (row.Amount != a.Amount) throw new ConflictException($"Kiasi cha M-Koba ({row.Amount:N0}) si sawa na mkopo ulioidhinishwa ({a.Amount:N0}).");
        var approvedEat = EatDate(a.ApprovedAt!.Value);
        if (row.Timestamp.Date < approvedEat) throw new ConflictException($"Muamala wa M-Koba ({row.Timestamp:dd/MM/yyyy}) ulifanyika KABLA ya idhini ({approvedEat:dd/MM/yyyy}).");
        // SASISHO17 (P0-5): namba za mkopaji zilizokuwa zinatumika siku ya muamala tu
        var phones = (await MemberPhonesAt.ListAsync(_context, groupId, a.BorrowerGroupMemberId, row.Timestamp, ct)).Select(PhoneKey).ToHashSet();
        var toOther = !phones.Contains(PhoneKey(row.Phone));
        if (toOther && (!acknowledgeOtherRecipient || string.IsNullOrWhiteSpace(note)))
            throw new ConflictException($"Muamala {row.Reference} ulienda kwa namba {row.Phone} ({row.PayerName}) ambayo SI namba iliyoidhinishwa ya mkopaji. Kama ni sahihi, thibitisha tena ukieleza kwa nini.");

        await using var tx = await DbTx.BeginAsync(_context, ct);
        var borrower = await _context.GroupMembers.AsNoTracking().FirstAsync(m => m.Id == a.BorrowerGroupMemberId, ct);
        var initiator = await _context.GroupMembers.AsNoTracking().FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == a.DisbursementInitiatedByUserId, ct);
        var disbursed = DateTime.SpecifyKind(row.Timestamp, DateTimeKind.Utc); // saa za ukutani za EAT kama M-Koba (Injini inazisoma hivyo)
        var loan = new Loan
        {
            Id = Guid.NewGuid(), GroupId = groupId, GroupMemberId = borrower.Id, UserId = borrower.UserId,
            PrincipalAmount = a.Amount, InterestRate = a.InterestPercent, InterestAmount = a.InterestAmount, Purpose = a.Purpose,
            DisbursedAt = disbursed, DueDate = disbursed.AddMonths(Math.Max(1, a.RepaymentMonths) - 1),
            IssuedByGroupMemberId = initiator?.Id ?? me.Id, LoanApplicationId = a.Id, DisbursementReference = row.Reference
        };
        _context.Loans.Add(loan);
        a.Status = LoanApplicationStatus.Disbursed; a.MkobaReference = row.Reference; a.DisbursedAt = disbursed;
        a.DisbursementConfirmedByUserId = userId; a.DisbursementConfirmedAt = DateTime.UtcNow; a.LoanId = loan.Id;
        a.RecipientNote = toOther ? Cut(note!.Trim(), 300) : null;
        await Audit(groupId, a.BorrowerGroupMemberId, userId, AuditAction.LoanDisbursementConfirmed, nameof(LoanApplication), a.Id, null, Snap(a), row.Reference,
            toOther ? Loc.L($"Namba ya mpokeaji {row.Phone} si ya mkopaji: {note!.Trim()}").Text : null, ct);
        var who = await MemberName(a.BorrowerGroupMemberId, ct);
        await _notify.NotifyAllMembersAsync(groupId, "LoanDisbursed", Loc.L($"Mkopo {a.ApplicationNumber} umetolewa"),
            Loc.L($"{who} amepewa mkopo wa {a.Amount:N0} (jumla ya kurejesha {a.TotalRepayable:N0} kwa miezi {a.RepaymentMonths}). M-Koba ref {row.Reference}."), $"/mikopo?ombi={a.Id}", ct);
        await _context.SaveChangesAsync(ct);
        await DbTx.CommitAsync(tx, ct);
        return a;
    }

    public async Task<LoanApplication> CancelAsync(Guid groupId, Guid id, string reason, Guid userId, CancellationToken ct = default)
    {
        var me = await RequireMemberAsync(groupId, userId, ct);
        var a = await LoadAsync(groupId, id, ct);
        if (a.Status is not (LoanApplicationStatus.Voting or LoanApplicationStatus.Approved or LoanApplicationStatus.DisbursementInitiated))
            throw new ConflictException("Ombi hili limeshafungwa.");
        var isLeader = MemberAccess.IsEffectiveLeader(me);
        if (!isLeader && me.Id != a.BorrowerGroupMemberId && a.RequestedByUserId != userId) throw new ForbiddenException("Huwezi kughairi ombi la mtu mwingine.");
        if (string.IsNullOrWhiteSpace(reason)) throw new ValidationException("Eleza sababu ya kughairi.");
        a.Status = LoanApplicationStatus.Cancelled; a.ClosedAt = DateTime.UtcNow; a.ClosedByUserId = userId; a.CloseReason = Cut(reason.Trim(), 500);
        await Audit(groupId, a.BorrowerGroupMemberId, userId, AuditAction.LoanApplicationCancelled, nameof(LoanApplication), a.Id, null, Snap(a), a.ApplicationNumber, a.CloseReason, ct);
        await _context.SaveChangesAsync(ct);
        return a;
    }

    // =====================================================================
    // 6. "UMESHINDIKANA" (write-off) — watu wawili
    // =====================================================================
    public async Task<Loan> RequestDefaultAsync(Guid groupId, Guid loanId, string reason, Guid userId, CancellationToken ct = default)
    {
        var me = await RequireLeaderAsync(groupId, userId, ct);
        var loan = await _context.Loans.FirstOrDefaultAsync(l => l.Id == loanId && l.GroupId == groupId, ct) ?? throw new NotFoundException("Mkopo haukupatikana.");
        if (loan.Status != LoanStatus.Active) throw new ConflictException("Mkopo huu si hai.");
        if (loan.DefaultRequestedByUserId is not null) throw new ConflictException("Tayari kuna ombi la kutangaza mkopo huu umeshindikana.");
        if (me.Id == loan.GroupMemberId) throw new ForbiddenException("Mkopaji hawezi kuomba mkopo wake utangazwe umeshindikana.");
        if (string.IsNullOrWhiteSpace(reason)) throw new ValidationException("Eleza kwa nini mkopo umeshindikana.");
        loan.DefaultRequestedByUserId = userId; loan.DefaultRequestedAt = DateTime.UtcNow; loan.DefaultReason = Cut(reason.Trim(), 500);
        await Audit(groupId, loan.GroupMemberId, userId, AuditAction.LoanDefaultRequested, nameof(Loan), loan.Id, null, new { loan.DefaultReason }, loan.DisbursementReference, loan.DefaultReason, ct);
        await _notify.NotifyLeadersAsync(groupId, "Loan", Loc.L($"Ombi: mkopo umeshindikana"), Loc.L($"Kiongozi ameomba mkopo utangazwe umeshindikana. Kiongozi mwingine aamue."), "/mikopo", ct);
        await _context.SaveChangesAsync(ct);
        return loan;
    }

    /// <summary>SASISHO15: imepangwa mstari mmoja (DbTx.Decision) — maamuzi mawili ya wakati mmoja hayawezi kuidhinisha mara mbili.</summary>
    public Task<Loan> DecideDefaultAsync(Guid groupId, Guid loanId, bool approve, string? note, Guid userId, CancellationToken ct = default)
        => DbTx.SerializedAsync(_context, groupId, DbTx.Decision, () => DecideDefaultCoreAsync(groupId, loanId, approve, note, userId, ct), ct);

    private async Task<Loan> DecideDefaultCoreAsync(Guid groupId, Guid loanId, bool approve, string? note, Guid userId, CancellationToken ct = default)
    {
        var me = await RequireLeaderAsync(groupId, userId, ct);
        var loan = await _context.Loans.FirstOrDefaultAsync(l => l.Id == loanId && l.GroupId == groupId, ct) ?? throw new NotFoundException("Mkopo haukupatikana.");
        if (loan.DefaultRequestedByUserId is null || loan.Status != LoanStatus.Active) throw new ConflictException("Hakuna ombi la kuamua.");
        var cancelling = loan.DefaultRequestedByUserId == userId;
        if (cancelling && approve) throw new ForbiddenException("Aliyeomba HAWEZI kuidhinisha mwenyewe (maker-checker). Unaweza kughairi tu.");
        if (me.Id == loan.GroupMemberId) throw new ForbiddenException("Mkopaji hawezi kuamua kuhusu mkopo wake.");
        if (!cancelling && (await RelatedAsync(groupId, loan.GroupMemberId, ct)).Contains(me.Id)) throw new ForbiddenException("Wewe ni ndugu wa mkopaji (mgongano wa maslahi).");
        if (approve) { loan.Status = LoanStatus.Defaulted; loan.DefaultDecidedByUserId = userId; loan.DefaultDecidedAt = DateTime.UtcNow; }
        else { loan.DefaultRequestedByUserId = null; loan.DefaultRequestedAt = null; loan.DefaultReason = null; }
        await Audit(groupId, loan.GroupMemberId, userId, AuditAction.LoanDefaultDecided, nameof(Loan), loan.Id, null, new { Approve = approve, Status = loan.Status.ToString() }, loan.DisbursementReference, note, ct);
        await _context.SaveChangesAsync(ct);
        return loan;
    }

    // =====================================================================
    // 7. KUSOMA
    // =====================================================================
    public async Task<List<object>> ListAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        var me = await RequireMemberAsync(groupId, userId, ct);
        var isLeader = MemberAccess.IsEffectiveLeader(me);
        var q = _context.LoanApplications.AsNoTracking().Where(a => a.GroupId == groupId);
        // Uwazi: kila mwanachama anaona mikopo ILIYOTOLEWA (matokeo ya fedha); maombi yanayoendelea yanaonekana kwa viongozi na mhusika
        if (!isLeader) q = q.Where(a => a.Status == LoanApplicationStatus.Disbursed || a.BorrowerGroupMemberId == me.Id || a.RequestedByUserId == userId);
        var list = await q.OrderByDescending(a => a.CreatedAt).Take(200).ToListAsync(ct);
        var names = await _context.GroupMembers.AsNoTracking().Where(m => m.GroupId == groupId)
            .Select(m => new { m.Id, m.UserId, Name = m.User != null ? m.User.FullName : m.MemberNumber, m.MemberNumber }).ToListAsync(ct);
        var ids = list.Select(a => a.Id).ToList();
        var votes = await _context.GovernanceVotes.AsNoTracking().Where(v => v.SubjectType == VoteSubject && ids.Contains(v.SubjectId)).ToListAsync(ct);
        return list.Select(a => (object)new
        {
            a.Id, a.ApplicationNumber, status = a.Status.ToString(), a.Amount, a.InterestPercent, a.InterestAmount, totalRepayable = a.TotalRepayable, a.RepaymentMonths,
            borrowerId = a.BorrowerGroupMemberId, borrower = names.FirstOrDefault(n => n.Id == a.BorrowerGroupMemberId)?.Name,
            a.CreatedAt, a.ApprovedAt, a.DisbursedAt, a.MkobaReference, a.Purpose,
            approvals = votes.Count(v => v.SubjectId == a.Id && v.Approve), rejections = votes.Count(v => v.SubjectId == a.Id && !v.Approve), a.RequiredApprovals
        }).ToList();
    }

    public async Task<object> GetAsync(Guid groupId, Guid id, Guid userId, CancellationToken ct = default)
    {
        var me = await RequireMemberAsync(groupId, userId, ct);
        var a = await LoadAsync(groupId, id, ct);
        await ExpireIfStaleAsync(a, userId, ct);
        var isLeader = MemberAccess.IsEffectiveLeader(me);
        if (!isLeader && a.Status != LoanApplicationStatus.Disbursed && a.BorrowerGroupMemberId != me.Id && a.RequestedByUserId != userId)
            throw new ForbiddenException("Ombi hili linaonekana kwa viongozi na mkopaji tu mpaka mkopo utolewe.");
        var names = await _context.GroupMembers.AsNoTracking().Where(m => m.GroupId == groupId)
            .Select(m => new { m.Id, m.UserId, Name = m.User != null ? m.User.FullName : m.MemberNumber }).ToListAsync(ct);
        string? UserName(Guid? uid) => uid is null ? null : names.FirstOrDefault(n => n.UserId == uid)?.Name;
        var votes = await _context.GovernanceVotes.AsNoTracking().Where(v => v.SubjectType == VoteSubject && v.SubjectId == a.Id).OrderBy(v => v.At).ToListAsync(ct);
        var voters = a.Status == LoanApplicationStatus.Voting ? await EligibleVotersAsync(a, ct) : new List<GroupMember>();
        var canVote = a.Status == LoanApplicationStatus.Voting && voters.Any(v => v.Id == me.Id) && votes.All(v => v.GroupMemberId != me.Id);
        var (canInitiate, initiateWhy) = isLeader ? await CanInitiateAsync(a, me, ct) : (false, null);
        var canConfirm = isLeader && a.Status == LoanApplicationStatus.DisbursementInitiated && a.DisbursementInitiatedByUserId != userId && me.Id != a.BorrowerGroupMemberId
                         && !(await RelatedAsync(groupId, a.BorrowerGroupMemberId, ct)).Contains(me.Id);
        List<object> candidates = new();
        if (canConfirm)
        {
            var approvedEat = EatDate(a.ApprovedAt!.Value);
            var usedRefs = (await WithdrawalRegistry.UsesAsync(_context, groupId, ct)).Select(u => u.Reference).ToHashSet();
            candidates = (await _context.MkobaRawTransactions.AsNoTracking().Where(t => t.GroupId == groupId && t.IsWithdrawal && t.Amount == a.Amount).ToListAsync(ct))
                .Where(t => t.Timestamp.Date >= approvedEat && !usedRefs.Contains(t.Reference))
                .Select(t => (object)new { t.Reference, t.Timestamp, t.Amount, t.PayerName, t.Phone }).ToList();
        }
        var inst = a.RepaymentMonths > 0 ? Math.Round(a.TotalRepayable / a.RepaymentMonths, 0, MidpointRounding.AwayFromZero) : a.TotalRepayable;
        return new
        {
            a.Id, a.ApplicationNumber, status = a.Status.ToString(), a.Amount, a.Purpose, a.InterestPercent, a.InterestAmount, totalRepayable = a.TotalRepayable,
            a.RepaymentMonths, monthlyInstallment = inst, a.RequiredApprovals,
            borrowerId = a.BorrowerGroupMemberId, borrower = names.FirstOrDefault(n => n.Id == a.BorrowerGroupMemberId)?.Name,
            requestedBy = UserName(a.RequestedByUserId), a.CreatedAt, a.ApprovedAt, a.ApprovalExpiresAt,
            initiatedBy = UserName(a.DisbursementInitiatedByUserId), a.DisbursementInitiatedAt,
            a.MkobaReference, a.DisbursedAt, confirmedBy = UserName(a.DisbursementConfirmedByUserId), a.DisbursementConfirmedAt, a.RecipientNote,
            a.LoanId, closedBy = UserName(a.ClosedByUserId), a.ClosedAt, a.CloseReason,
            eligibility = a.EligibilityJson is null ? null : JsonSerializer.Deserialize<List<Check>>(a.EligibilityJson, Json),
            votes = votes.Select(v => new { voter = names.FirstOrDefault(n => n.Id == v.GroupMemberId)?.Name, role = v.RoleAtVote.ToString(), v.Approve, v.Reason, v.At }),
            eligibleVoters = voters.Count,
            canVote, canInitiate, initiateWhy = a.Status == LoanApplicationStatus.Approved ? initiateWhy : null, canConfirm, candidates,
            canCancel = a.Status is LoanApplicationStatus.Voting or LoanApplicationStatus.Approved or LoanApplicationStatus.DisbursementInitiated
                        && (isLeader || a.BorrowerGroupMemberId == me.Id || a.RequestedByUserId == userId)
        };
    }

    /// <summary>Mikopo iliyotolewa (hai/iliyokwisha) pamoja na maombi ya "umeshindikana" — kwa viongozi na wanachama (uwazi).</summary>
    public async Task<List<object>> LoansAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        var me = await RequireMemberAsync(groupId, userId, ct);
        var isLeader = MemberAccess.IsEffectiveLeader(me);
        var loans = await _context.Loans.AsNoTracking().Where(l => l.GroupId == groupId).OrderByDescending(l => l.DisbursedAt).ToListAsync(ct);
        var names = await _context.GroupMembers.AsNoTracking().Where(m => m.GroupId == groupId)
            .Select(m => new { m.Id, m.UserId, Name = m.User != null ? m.User.FullName : m.MemberNumber }).ToListAsync(ct);
        var truth = await _truth.GetGroupTruthAsync(groupId, null, ct);
        return loans.Select(l =>
        {
            var line = truth.FirstOrDefault(t => t.MemberId == l.GroupMemberId)?.Statement.Loans.FirstOrDefault(x => x.LoanId == l.Id.ToString());
            return (object)new
            {
                l.Id, borrowerId = l.GroupMemberId, borrower = names.FirstOrDefault(n => n.Id == l.GroupMemberId)?.Name, l.PrincipalAmount, l.InterestRate, l.InterestAmount,
                totalRepayable = l.PrincipalAmount + l.InterestAmount, repaid = line?.Repaid ?? 0m, outstanding = line?.Outstanding ?? (l.PrincipalAmount + l.InterestAmount),
                l.DisbursedAt, l.DueDate, status = l.Status.ToString(), l.DisbursementReference, l.LoanApplicationId,
                defaultRequestedBy = l.DefaultRequestedByUserId is { } u ? names.FirstOrDefault(n => n.UserId == u)?.Name : null, l.DefaultReason,
                canRequestDefault = isLeader && l.Status == LoanStatus.Active && l.DefaultRequestedByUserId is null && me.Id != l.GroupMemberId,
                canDecideDefault = isLeader && l.Status == LoanStatus.Active && l.DefaultRequestedByUserId is not null && l.DefaultRequestedByUserId != userId && me.Id != l.GroupMemberId,
                canCancelDefault = l.Status == LoanStatus.Active && l.DefaultRequestedByUserId == userId
            };
        }).ToList();
    }
}
