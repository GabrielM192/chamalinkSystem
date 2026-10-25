using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Exceptions;

namespace ChamaLink.Infrastructure.Services;

// Dedup fix (second audit round, item A: "Duplicate Approval Logic"):
// WithdrawalService and GroupAuthorizationService each had their own
// copy of "turn an ApprovalMode into concrete roles + how many are
// needed" - same logic, two places to keep in sync. This is now the one
// place that does it; both services call in here instead.
//
// WithdrawalService needs the full (roles, count) pair, since a
// withdrawal is a multi-vote workflow tallied against WithdrawalApproval
// rows. GroupAuthorizationService's single-actor gates (Loan/Event/
// Import: "is this one caller allowed to do this right now") only need
// the roles half - ResolveAllowedRoles gives them just that without
// discarding the count awkwardly at every call site.
public static class ApprovalPolicyResolver
{
    public static (HashSet<GroupRole> AllowedRoles, int RequiredApprovals) Resolve(ApprovalPolicy? policy)
    {
        // FAIL-CLOSED (Ushauri §4): sera isipowekwa HAKUNA idhini ya kimya kimya ya mtu mmoja — 409 "weka sera kwanza".
        if (policy is null)
            throw new ConflictException("Kikundi hakijaweka sera ya idhini kwa kitendo hiki. Mwenyekiti aweke sera kwenye Mipangilio kwanza.");
        var mode = policy.Mode;

        switch (mode)
        {
            case ApprovalMode.ChairpersonOnly:
                return (new HashSet<GroupRole> { GroupRole.Chairperson }, 1);

            case ApprovalMode.TreasurerAndChairperson:
                return (new HashSet<GroupRole> { GroupRole.Treasurer, GroupRole.Chairperson }, 2);

            case ApprovalMode.TreasurerAndSecretary:
                return (new HashSet<GroupRole> { GroupRole.Treasurer, GroupRole.Secretary }, 2);

            case ApprovalMode.CustomApproval:
                var roles = ParseCustomRoles(policy?.CustomRoles);
                if (roles.Count == 0)
                    // Zamani: ilirudi kwa "Treasurer peke yake" kimya kimya (fail-open). Sasa: sera mbovu = hakuna idhini.
                    throw new ConflictException("Sera ya idhini (CustomApproval) haina nafasi yoyote halali ya kuidhinisha. Irekebishwe kwenye Mipangilio.");
                int required = policy?.CustomRequiredApprovals ?? roles.Count;
                // Manaibu wanaruhusiwa (Katibu/Mtunza-hazina zaidi ya mmoja), kwa hiyo idadi haifungwi kwa idadi ya NAFASI.
                if (required < 1) throw new ConflictException("Idadi ya idhini kwenye sera iwe angalau 1.");
                return (roles, required);

            case ApprovalMode.TreasurerOnly:
            default:
                return (new HashSet<GroupRole> { GroupRole.Treasurer }, 1);
        }
    }

    public static HashSet<GroupRole> ResolveAllowedRoles(ApprovalPolicy? policy) => Resolve(policy).AllowedRoles;

    // SECURITY FIX (audit 9.6: "Custom approval inaweza kuruhusu Member
    // wa kawaida"): a plain Member has no leadership standing in a
    // group, so it must never be assignable as an approver via
    // CustomRoles - even if a Chairperson typed it into settings by
    // mistake, or an attacker tried to widen who can approve their own
    // request. Every other role parses through unchanged.
    private static HashSet<GroupRole> ParseCustomRoles(string? csv)
    {
        var result = new HashSet<GroupRole>();
        if (string.IsNullOrWhiteSpace(csv))
            return result;

        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<GroupRole>(part, true, out var role) && role != GroupRole.Member)
                result.Add(role);
        }

        return result;
    }
}
