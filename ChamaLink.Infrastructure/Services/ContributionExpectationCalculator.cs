using ChamaLink.Domain;

namespace ChamaLink.Infrastructure.Services;

// TECH-DEBT-004 (tracked, not yet built): the "correct" way to know how
// many months a member genuinely owed a contribution is a full
// MemberStatusHistory (every Active/Warning/Suspended/Inactive/Exited
// transition with its own timestamp) - GroupMember only stores the
// member's CURRENT Status today, with no record of when it last
// changed. Building that history is real schema + migration work,
// deliberately deferred so it doesn't block Member Financial Profile /
// Reports Engine / Loan Portfolio, which deliver more value right now.
//
// Until then, this is the single, isolated place that decides how far
// forward a member's contribution obligation is assumed to run. It
// approximates "when did this member stop being expected to
// contribute" using data that already exists (their own last
// Contribution ledger entry) instead of Status alone:
//   - Active/Warning: obligation is ongoing, so it runs up to "now".
//   - Anything else (Suspended/Inactive/Exited/New with no history):
//     obligation is assumed to have stopped at their last actual
//     contribution (or, if they never contributed at all, at
//     JoinedAt), rather than continuing to grow forever the longer
//     they've been gone. This is a best-effort proxy, not a precise
//     answer - a member who was Active for months, then Suspended,
//     then Active again with no contributions in between would still
//     read as "stopped at their last contribution", which understates
//     their true obligation across the gap. That precision gap is
//     exactly what MemberStatusHistory (TECH-DEBT-004) will close.
//
// Keeping this logic here instead of inline in AnalyticsService means
// that whenever MemberStatusHistory does get built, only this one
// method needs to change - AnalyticsService, ReportsController, and
// any future caller (dashboard, exports, etc.) never need to know how
// the answer is computed, only that they can ask for it.
public static class ContributionExpectationCalculator
{
    public static int CalculateExpectedMonths(
        DateTime joinedAt,
        MemberStatus status,
        DateTime? lastContributionAt,
        DateTime now)
    {
        DateTime asOfDate = (status == MemberStatus.Active || status == MemberStatus.Warning)
            ? now
            : (lastContributionAt ?? joinedAt);

        int months = (asOfDate.Year - joinedAt.Year) * 12 + (asOfDate.Month - joinedAt.Month);
        return Math.Max(0, months) + 1;
    }
}
