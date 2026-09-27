namespace ChamaLink.Application.DTOs;

// ── Awamu 2 (2026-09-19): Data Repair ya Ukonga ────────────────────────
// Baada ya Awamu 1, sheria zimetulia. Data iliyoingizwa wakati wa bug-era
// (kabla ya marekebisho) bado ina drift ya counters na snapshots tupu.
// Huduma hii haitoi data — inaipa mtunza-hazina uwezo wa kuona na kurekebisha.

public record CounterDriftRowDto(
    Guid GroupMemberId,
    string MemberName,
    int OldCount,
    int NewCount,
    decimal OldBalance,
    decimal NewBalance,
    bool HasDrift);

public record MissingSnapshotRowDto(
    DateTime Month,
    int ExpectedMembers,
    int ActualSnapshots,
    int MissingCount);

public record LoanRepaymentGapRowDto(
    Guid GroupMemberId,
    string MemberName,
    DateTime Month,
    decimal ExpectedRepayment,
    decimal PaidRepayment,
    decimal SavingsPostedInMonth,
    string Note);

public record DataRepairPreviewDto(
    Guid GroupId,
    string GroupName,
    DateTime GeneratedAt,
    int MembersChecked,
    List<CounterDriftRowDto> CounterDrifts,
    int CounterDriftCount,
    List<MissingSnapshotRowDto> MissingSnapshots,
    int TotalMissingSnapshots,
    List<LoanRepaymentGapRowDto> LoanRepaymentGaps,
    List<string> Warnings,
    bool HasIssues);

public record DataRepairResultDto(
    Guid GroupId,
    string Action,
    DateTime RepairedAt,
    int MembersChecked,
    int MembersCorrected,
    int SnapshotsCreated,
    int SnapshotsUpdated,
    List<string> Details);
