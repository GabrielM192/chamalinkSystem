namespace ChamaLink.Domain.Entities;

// Ukonga Rules Specification v1.2, sehemu 6 (Phase 4). This is a
// reporting/optimization layer, NOT business logic itself - it is written
// AFTER Phase 2 (Member Status Engine) and Phase 3 (Fine Engine +
// ARCH-001) have already computed the real numbers for a given member for
// a given month. One row per GroupMember per Month, so Reports/Dashboard
// can do `SELECT * FROM ComplianceSnapshots WHERE GroupId = X AND Month =
// latest` instead of re-scanning LedgerEntries/Debts/Fines every time,
// and so a member's month-by-month trend is directly queryable.
//
// "Compliant kwa mwezi huu" (ConsecutiveMissedMonths = 0) is NOT the same
// as "hana deni" - OutstandingContributionDebt from earlier missed months
// stays exactly where it is (governed by ARCH-001 / DebtAllocationStrategy)
// regardless of the current month's ConsecutiveMissedMonths value. Both
// fields are always shown side by side on purpose - see sehemu 4.
public class ComplianceSnapshot
{
    public Guid Id { get; set; }
    public Guid GroupMemberId { get; set; }
    public Guid GroupId { get; set; }

    // First day of the month this snapshot covers.
    public DateTime Month { get; set; }

    public decimal ExpectedContribution { get; set; }
    public decimal PaidContribution { get; set; }

    // ── Marejesho ya mikopo (Awamu 1 — 2026-09-19) ────────────────────
    // "Lengo" la mwanachama kwa mwezi = ExpectedContribution +
    // ExpectedLoanRepayment. Bila hizi, ripoti za uzingatiaji zilionyesha
    // mwanachama aliyelipa mchango + rejesho kama "mwenye ziada", na
    // aliyelipa rejesho pekee kama mkosoaji — chanzo cha taarifa zisizo
    // za kweli.
    public decimal ExpectedLoanRepayment { get; set; }
    public decimal PaidLoanRepayment { get; set; }

    // Amount-based, not bool (sehemu 6 - a member can pay part of a fine).
    public decimal FineIssuedAmount { get; set; }
    public decimal FinePaidAmount { get; set; }

    // Derived (FineIssuedAmount - FinePaidAmount) - stored on write so
    // Reports/Dashboard don't need to recompute it, but it is never the
    // independently-updated source of truth; ComplianceSnapshotService
    // always sets it alongside the two fields above.
    public decimal OutstandingFineAmount { get; set; }

    // Cumulative, all-time - never decreases (sehemu 4).
    public int TotalMissedMonths { get; set; }

    // Resets to 0 the moment a month is paid in full (sehemu 4) - this is
    // "compliance ya sasa", not history.
    public int ConsecutiveMissedMonths { get; set; }

    // Contribution debt only (NOT fines, NOT loans - sehemu 4: "Kwa nini
    // Debt inagawanywa kwa aina"). Stays untouched by a fully-paid current
    // month; only ARCH-001 debt-clearance payments reduce it.
    public decimal OutstandingContributionDebt { get; set; }

    // Active / Warning / Inactive (= "NonActive" in the spec) for this
    // month - see sehemu 4 status thresholds.
    public MemberStatus Status { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public GroupMember? GroupMember { get; set; }
    public Group? Group { get; set; }
}
