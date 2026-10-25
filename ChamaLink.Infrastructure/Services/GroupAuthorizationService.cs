using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Infrastructure.Services;

// SECURITY FIX (audit Sprint 1 item 2: "Tengeneza centralized
// authorization policy"). Before this, every controller that cared about
// roles at all (only WithdrawalService did) re-implemented its own
// ad-hoc check. Every financial/leadership operation should now go
// through RequireMembershipAsync or RequireRoleAsync instead of writing
// its own membership/role lookup - one place to get this right, one
// place to fix it if it's ever wrong.
public class GroupAuthorizationService
{
    private readonly ApplicationDbContext _context;

    public GroupAuthorizationService(ApplicationDbContext context)
    {
        _context = context;
    }

    // Confirms the given (authenticated) user is actually a member of
    // this group, and returns their GroupMember row - callers use this
    // as "who is the caller, as a member of this specific group".
    // SASISHO17 (P0-1): uanachama HAI tu. Aliyesimamishwa/aliyetoka/aliyefariki/aliyehifadhiwa hana ruhusa yoyote
    // (chanzo kimoja: Common.MemberAccess). Kusoma taarifa YAKO mwenyewe: RequireOwnRecordAsync.
    public async Task<GroupMember> RequireMembershipAsync(Guid userId, Guid groupId)
        => await Common.MemberAccess.RequireActiveAsync(_context, groupId, userId);

    /// <summary>
    /// Rekodi yako (hali yoyote) — KWA KUSOMA TAARIFA YAKO MWENYEWE TU (mf. aliyetoka anaona hesabu yake ya mwisho).
    /// Usiitumie kwa kitendo chochote cha kuandika wala taarifa za wengine.
    /// </summary>
    public async Task<GroupMember> RequireOwnRecordAsync(Guid userId, Guid groupId)
        => await Common.MemberAccess.FindAsync(_context, groupId, userId, default, tracking: false)
           ?? throw new ChamaLink.Domain.Exceptions.ForbiddenException(Common.MemberAccess.NotMemberMessage);

    // Same as above, but additionally requires the member to hold one of
    // the given leadership roles (e.g. only Chairperson may update
    // settings; only Treasurer/Chairperson may issue a loan).
    // SASISHO16: majina ya nafasi kwa Kiswahili (si majina ya enum ya code kama "Chairperson") kwenye ujumbe wa mtumiaji
    private static string RoleNames(IEnumerable<GroupRole> roles) => string.Join(" au ", roles.Distinct().OrderBy(r => r).Select(r => r switch
    {
        GroupRole.Chairperson => "Mwenyekiti",
        GroupRole.Treasurer => "Mtunza-hazina",
        GroupRole.Secretary => "Katibu",
        GroupRole.MemberRepresentative => "Mwakilishi wa wanachama",
        _ => "Mwanachama"
    }));

    public async Task<GroupMember> RequireRoleAsync(Guid userId, Guid groupId, params GroupRole[] allowedRoles)
    {
        var member = await RequireMembershipAsync(userId, groupId);
        // SASISHO17: nafasi INAYOTUMIKA (kiongozi aliye kwenye kipindi cha kusubiri = Mwanachama)
        return Common.MemberAccess.EnsureRole(member, allowedRoles, $"Huna ruhusa ya kufanya hili. Inahitaji: {RoleNames(allowedRoles)}.");
    }

    // Turns an ApprovalPolicy into the concrete set of roles allowed to
    // act under it. Delegates to the shared ApprovalPolicyResolver (see
    // that file - dedup fix, second audit round item A) rather than
    // keeping its own copy of the same resolution logic.
    public static HashSet<GroupRole> ResolveApprovalRoles(ApprovalPolicy? policy)
    {
        return ApprovalPolicyResolver.ResolveAllowedRoles(policy);
    }

    // Confirms the caller is a member of the group AND holds a role
    // allowed by the given ApprovalPolicy.
    public async Task<GroupMember> RequireApprovalPolicyAsync(Guid userId, Guid groupId, ApprovalPolicy? policy)
    {
        var member = await RequireMembershipAsync(userId, groupId);
        var allowedRoles = ResolveApprovalRoles(policy);

        if (!allowedRoles.Contains(Common.MemberAccess.EffectiveRole(member)))
        {
            throw new ChamaLink.Domain.Exceptions.ForbiddenException($"Huna ruhusa ya kufanya hili. Inahitaji: {RoleNames(allowedRoles)}.");
        }

        return member;
    }

    // Convenience wrapper for the common case: load the group's
    // GroupSettings, pick out one of its GovernanceSettings policies
    // (e.g. g => g.LoanApproval), and enforce it in one call. A group
    // with no GroupSettings row at all falls back to null policy
    // (TreasurerOnly), the same safe default as everywhere else.
    public async Task<GroupMember> RequireGovernanceApprovalAsync(
        Guid userId, Guid groupId, Func<GovernanceSettings, ApprovalPolicy> selectPolicy)
    {
        var settings = await _context.GroupSettings.FirstOrDefaultAsync(s => s.GroupId == groupId);
        var policy = settings != null ? selectPolicy(settings.Governance) : null;
        return await RequireApprovalPolicyAsync(userId, groupId, policy);
    }
}
