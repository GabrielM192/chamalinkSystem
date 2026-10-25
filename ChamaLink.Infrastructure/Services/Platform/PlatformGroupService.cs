using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Exceptions;
using ChamaLink.Infrastructure.Services.Common;
using ChamaLink.Infrastructure.Services.Truth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ChamaLink.Infrastructure.Services.Platform;

/// <summary>
/// SASISHO20 — dashibodi ya vikundi vyote + "afya" ya kila kikundi.
///
/// Kile msimamizi anaona KAWAIDA ni MUHTASARI (jumla za kikundi), si fedha za mtu mmoja.
/// Taarifa za mwanachama mmoja mmoja zinafunguliwa tu kwa **break-glass** iliyoidhinishwa
/// na msimamizi wa pili, kwa saa 24, na kikundi kinaona.
///
/// "Afya" ni ishara (flags) zinazotokana na data halisi — si maoni:
///   NoPolicy, PolicyIncomplete, NoGovernance, NoLeaders, NoStatements,
///   StaleStatements, ManySuspended, PendingApprovalsOld, InvariantMismatch
/// </summary>
public class PlatformGroupService
{
    private readonly ApplicationDbContext _context;
    private readonly TruthLedgerService _truth;
    private readonly PlatformApprovalService _approvals;
    private readonly IConfiguration _config;

    public PlatformGroupService(ApplicationDbContext context, TruthLedgerService truth,
        PlatformApprovalService approvals, IConfiguration config)
    {
        _context = context; _truth = truth; _approvals = approvals; _config = config;
    }

    private int StaleStatementDays => int.TryParse(_config["Platform:StaleStatementDays"], out var v) && v > 0 ? v : 45;
    private int OldApprovalDays => int.TryParse(_config["Platform:OldApprovalDays"], out var v) && v > 0 ? v : 14;
    private int ManySuspendedPercent => int.TryParse(_config["Platform:ManySuspendedPercent"], out var v) && v > 0 ? v : 20;

    public sealed record GroupRow(
        Guid Id, string Name, string? ShortCode, string Type, DateTime CreatedAt,
        int Members, int ActiveMembers, int Leaders, decimal EventDebt,
        DateTime? LastStatementAt, DateTime? LastActivityAt, int PendingApprovals,
        List<string> Flags, string Health);

    public sealed record GroupDetail(
        GroupRow Summary,
        decimal TotalAccepted, decimal Savings, decimal Arrears, decimal LoansOutstanding,
        decimal EventDebt, decimal JoinFeeOutstanding, int MonthsTracked,
        List<IssueRow> Issues, bool BreakGlassActive, DateTime? BreakGlassExpiresAt);

    public sealed record IssueRow(string Code, string Severity, string Detail);

    public sealed record MemberRowExport(Guid MemberId, string? MemberNumber, string? Name, string Status, string Role,
        decimal Balance, decimal Outstanding, decimal Arrears, bool Active);

    // ------------------------------------------------------------------
    // Orodha ya vikundi vyote (muhtasari)
    // ------------------------------------------------------------------
    public async Task<List<GroupRow>> ListAsync(CancellationToken ct = default)
    {
        var groups = await _context.Groups.AsNoTracking().OrderBy(g => g.Name).ToListAsync(ct);
        if (groups.Count == 0) return new();

        var ids = groups.Select(g => g.Id).ToList();
        var now = DateTime.UtcNow;

        var memberAgg = await _context.GroupMembers.AsNoTracking()
            .Where(m => ids.Contains(m.GroupId))
            .GroupBy(m => m.GroupId)
            .Select(g => new { GroupId = g.Key, Total = g.Count(), Active = g.Count(m => MemberAccess.ActiveStatuses.Contains(m.Status)) })
            .ToListAsync(ct);

        var leaders = await _context.GroupMembers.AsNoTracking()
            .Where(m => ids.Contains(m.GroupId) && MemberAccess.ActiveStatuses.Contains(m.Status))
            .Select(m => new { m.GroupId, m.Role, m.LeaderPowersFrom })
            .ToListAsync(ct);

        var eventDebt = await _context.EventObligations.AsNoTracking()
            .Where(o => ids.Contains(o.GroupId) && o.Status == EventObligationStatus.Pending)
            .GroupBy(o => o.GroupId)
            .Select(g => new { GroupId = g.Key, Debt = g.Sum(o => o.RequiredAmount - o.DeductedAmount) })
            .ToListAsync(ct);

        var statements = await _context.MkobaRawTransactions.AsNoTracking()
            .Where(t => ids.Contains(t.GroupId))
            .GroupBy(t => t.GroupId)
            .Select(g => new { GroupId = g.Key, Last = g.Max(t => t.Timestamp) })
            .ToListAsync(ct);

        var settings = await _context.TruthGroupSettings.AsNoTracking()
            .Where(s => ids.Contains(s.GroupId)).ToListAsync(ct);
        var governances = await _context.BenefitGovernances.AsNoTracking()
            .Where(g => ids.Contains(g.GroupId) && g.Status == ProposalStatus.Approved)
            .Select(g => g.GroupId).Distinct().ToListAsync(ct);

        var pendingStatus = await _context.MemberStatusChangeRequests.AsNoTracking()
            .Where(r => ids.Contains(r.GroupId) && r.Status == ProposalStatus.Pending)
            .GroupBy(r => r.GroupId).Select(g => new { GroupId = g.Key, Count = g.Count() }).ToListAsync(ct);
        var pendingCases = await _context.BenefitCases.AsNoTracking()
            .Where(c => ids.Contains(c.GroupId) && (c.Status == BenefitCaseStatus.Voting || c.Status == BenefitCaseStatus.AwaitingVerification))
            .GroupBy(c => c.GroupId).Select(g => new { GroupId = g.Key, Count = g.Count() }).ToListAsync(ct);
        var pendingLoans = await _context.LoanApplications.AsNoTracking()
            .Where(l => ids.Contains(l.GroupId) && l.Status == LoanApplicationStatus.Voting)
            .GroupBy(l => l.GroupId).Select(g => new { GroupId = g.Key, Count = g.Count() }).ToListAsync(ct);
        var pendingSettings = await _context.TruthSettingsProposals.AsNoTracking()
            .Where(p => ids.Contains(p.GroupId) && p.Status == ProposalStatus.Pending)
            .GroupBy(p => p.GroupId).Select(g => new { GroupId = g.Key, Count = g.Count() }).ToListAsync(ct);

        var activity = await _context.AuditEvents.AsNoTracking()
            .Where(a => ids.Contains(a.GroupId))
            .GroupBy(a => a.GroupId).Select(g => new { GroupId = g.Key, Last = g.Max(a => a.Timestamp) }).ToListAsync(ct);

        var rows = new List<GroupRow>(groups.Count);
        foreach (var g in groups)
        {
            var m = memberAgg.FirstOrDefault(x => x.GroupId == g.Id);
            var total = m?.Total ?? 0;
            var active = m?.Active ?? 0;
            var leaderCount = leaders.Count(x => x.GroupId == g.Id && MemberAccess.IsEffectiveLeader(
                new GroupMember { Role = x.Role, Status = MemberStatus.Active, LeaderPowersFrom = x.LeaderPowersFrom }));
            var s = settings.FirstOrDefault(x => x.GroupId == g.Id);
            var hasGov = governances.Contains(g.Id);
            var lastStmt = statements.FirstOrDefault(x => x.GroupId == g.Id)?.Last;
            var pending = (pendingStatus.FirstOrDefault(x => x.GroupId == g.Id)?.Count ?? 0)
                          + (pendingCases.FirstOrDefault(x => x.GroupId == g.Id)?.Count ?? 0)
                          + (pendingLoans.FirstOrDefault(x => x.GroupId == g.Id)?.Count ?? 0)
                          + (pendingSettings.FirstOrDefault(x => x.GroupId == g.Id)?.Count ?? 0);

            var flags = new List<string>();
            if (s is null) flags.Add("NoPolicy");
            else
            {
                if (g.Type == GroupType.MonthlySavings && (s.MonthlyContribution <= 0 || s.DueDateDay == 0)) flags.Add("PolicyIncomplete");
                if (g.Type == GroupType.EventBased && s.MemberMinimumBalance <= 0) flags.Add("PolicyIncomplete");
            }
            if (!hasGov) flags.Add("NoGovernance");
            if (active > 0 && leaderCount == 0) flags.Add("NoLeaders");
            if (lastStmt is null) flags.Add("NoStatements");
            else if (lastStmt < now.AddDays(-StaleStatementDays)) flags.Add("StaleStatements");
            if (total >= 3)
            {
                var suspended = await _context.GroupMembers.AsNoTracking()
                    .CountAsync(x => x.GroupId == g.Id && x.Status == MemberStatus.Suspended, ct);
                if (suspended * 100 >= total * ManySuspendedPercent) flags.Add("ManySuspended");
            }
            if (pending > 0 && await HasOldApprovalAsync(g.Id, ct)) flags.Add("PendingApprovalsOld");

            var health = flags.Any(f => f is "NoPolicy" or "NoLeaders") ? "Critical"
                : flags.Count > 0 ? "Warning" : "Ok";

            rows.Add(new GroupRow(g.Id, g.Name, g.ShortCode, g.Type.ToString(), g.CreatedAt,
                total, active, leaderCount, eventDebt.FirstOrDefault(x => x.GroupId == g.Id)?.Debt ?? 0m,
                lastStmt, activity.FirstOrDefault(x => x.GroupId == g.Id)?.Last, pending, flags, health));
        }
        return rows;
    }

    private async Task<bool> HasOldApprovalAsync(Guid groupId, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-OldApprovalDays);
        return await _context.MemberStatusChangeRequests.AsNoTracking().AnyAsync(r => r.GroupId == groupId && r.Status == ProposalStatus.Pending && r.RequestedAt < cutoff, ct)
               || await _context.BenefitCases.AsNoTracking().AnyAsync(c => c.GroupId == groupId && (c.Status == BenefitCaseStatus.Voting || c.Status == BenefitCaseStatus.AwaitingVerification) && c.CreatedAt < cutoff, ct)
               || await _context.LoanApplications.AsNoTracking().AnyAsync(l => l.GroupId == groupId && l.Status == LoanApplicationStatus.Voting && l.CreatedAt < cutoff, ct)
               || await _context.TruthSettingsProposals.AsNoTracking().AnyAsync(p => p.GroupId == groupId && p.Status == ProposalStatus.Pending && p.RequestedAt < cutoff, ct);
    }

    // ------------------------------------------------------------------
    // Kikundi kimoja: muhtasari + matatizo
    // ------------------------------------------------------------------
    public async Task<GroupDetail> DetailAsync(Guid groupId, Guid actorUserId, CancellationToken ct = default)
    {
        var rows = await ListAsync(ct);
        var summary = rows.FirstOrDefault(r => r.Id == groupId) ?? throw new NotFoundException("Kikundi hakipo.");

        // Injini ya Ukweli inatoa taarifa za kikundi kwa queries 8 bila kujali idadi ya wanachama.
        // Kikundi kisicho na sera (NoPolicy) HAKIKATIZWI kuonekana: msimamizi anahitaji kuona
        // muhtasari + tatizo "sera haipo" ndiyo maana ya dashibodi. Ndiyo maana 409 hapa
        // inageuzwa kuwa orodha tupu + IssueRow(NoPolicy) iliyopo tayari.
        List<TruthLedgerService.MemberTruth> truth;
        try { truth = await _truth.GetGroupTruthAsync(groupId, null, ct); }
        catch (PolicyNotConfiguredException) { truth = new List<TruthLedgerService.MemberTruth>(); }
        var issues = new List<IssueRow>();

        decimal accepted = 0, savings = 0, arrears = 0, loans = 0, joinOutstanding = 0;
        var months = 0;
        foreach (var t in truth)
        {
            var st = t.Statement;
            accepted += st.TotalAccepted;
            savings += st.Savings;
            arrears += st.Arrears;
            loans += st.LoanOutstanding;
            joinOutstanding += st.JoinFeeOutstanding;
            months = Math.Max(months, st.Periods.Count);
            if (Math.Abs(st.InvariantDifference) > 0.01m)
                issues.Add(new IssueRow("InvariantMismatch", "Critical",
                    $"{t.MemberNumber}: tofauti ya hesabu {st.InvariantDifference:N2} (pesa zilizokubaliwa hazitengani sawa na matumizi yake)."));
        }

        foreach (var flag in summary.Flags)
            issues.Add(new IssueRow(flag, flag is "NoPolicy" or "NoLeaders" ? "Critical" : "Warning", DescribeFlag(flag)));

        var bg = await _approvals.HasBreakGlassAsync(actorUserId, groupId, ct);
        DateTime? bgExpires = null;
        if (bg)
            bgExpires = await _context.PlatformApprovals.AsNoTracking()
                .Where(a => a.GroupId == groupId && a.Status == PlatformApprovalStatus.Approved && a.ExpiresAt != null)
                .MaxAsync(a => a.ExpiresAt, ct);

        return new GroupDetail(summary, accepted, savings, arrears, loans,
            summary.EventDebt, joinOutstanding, months, issues, bg, bgExpires);
    }

    private static string DescribeFlag(string flag) => flag switch
    {
        "NoPolicy" => "Kikundi hakijaweka sera ya fedha (TruthGroupSettings). Hakuna hesabu inayoweza kufanyika.",
        "PolicyIncomplete" => "Sera ipo lakini haijakamilika (mchango wa mwezi au kiwango cha chini hakijawekwa).",
        "NoGovernance" => "Hakuna sera ya mafao na idhini iliyoidhinishwa — maombi ya mafao yatakwama.",
        "NoLeaders" => "Hakuna kiongozi hai (Mwenyekiti/Mtunza-hazina/Katibu).",
        "NoStatements" => "Hakuna taarifa ya M-Koba iliyowahi kupakiwa — hesabu haijathibitishwa kwa benki.",
        "StaleStatements" => "Taarifa ya mwisho ya M-Koba ni ya zamani — huenda kuna michango isiyoingizwa.",
        "ManySuspended" => "Wanachama wengi wamesimamishwa — angalia kama kuondolewa kunatumika vibaya.",
        "PendingApprovalsOld" => "Kuna maombi yanasubiri kura kwa muda mrefu — viongozi hawafanyi kazi.",
        _ => flag
    };

    // ------------------------------------------------------------------
    // Break-glass: wanachama mmoja mmoja (mfumo unakataa bila idhini ya mwili wa pili)
    // ------------------------------------------------------------------
    public async Task<List<MemberRowExport>> MembersAsync(Guid groupId, Guid actorUserId, CancellationToken ct = default)
    {
        if (!await _approvals.HasBreakGlassAsync(actorUserId, groupId, ct))
            throw new ForbiddenException("Taarifa za mwanachama mmoja mmoja zinahitaji break-glass iliyoidhinishwa na msimamizi mwingine.");

        await _approvals.MarkBreakGlassUsedAsync(actorUserId, groupId, ct);

        // Kikundi kisicho na sera: wanachama wanaonekana (namba, jina, nafasi) lakini hesabu ni sifuri —
        // hesabu haiwezi kufanyika bila sera, na hilo linaelezwa kwenye muhtasari (NoPolicy).
        List<TruthLedgerService.MemberTruth> truth;
        try { truth = await _truth.GetGroupTruthAsync(groupId, null, ct); }
        catch (PolicyNotConfiguredException) { truth = new List<TruthLedgerService.MemberTruth>(); }

        var members = await _context.GroupMembers.AsNoTracking().Where(m => m.GroupId == groupId)
            .Select(m => new { m.Id, m.MemberNumber, m.Status, m.Role, Name = m.User != null ? m.User.FullName : null })
            .ToListAsync(ct);
        var eventDebt = await _context.EventObligations.AsNoTracking()
            .Where(o => o.GroupId == groupId && o.Status == EventObligationStatus.Pending)
            .GroupBy(o => o.GroupMemberId)
            .Select(g => new { MemberId = g.Key, Debt = g.Sum(o => o.RequiredAmount - o.DeductedAmount) })
            .ToListAsync(ct);

        var truthByMember = truth.ToDictionary(t => t.MemberId);
        return members.Select(m =>
        {
            var t = truthByMember.GetValueOrDefault(m.Id);
            return new MemberRowExport(m.Id, t?.MemberNumber ?? m.MemberNumber, m.Name,
                m.Status.ToString(), m.Role.ToString(),
                t?.Statement.Savings ?? 0m, eventDebt.FirstOrDefault(x => x.MemberId == m.Id)?.Debt ?? 0m,
                t?.Statement.Arrears ?? 0m, MemberAccess.IsActive(m.Status));
        }).OrderBy(r => r.MemberNumber).ToList();
    }
}
