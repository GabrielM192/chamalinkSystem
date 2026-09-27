namespace ChamaLink.Application.DTOs;

public class MKobaImportResultDto
{
    public int TotalSubmitted { get; set; }
    public int SuccessfullyProcessed { get; set; }
    public int IgnoredDuplicates { get; set; }
    public int MembersNotFound { get; set; }
    public decimal TotalFinesDeducted { get; set; }
    public decimal TotalWelfareAdded { get; set; }

    // Awamu 1 (2026-09-19): jumla ya pesa zilizokata marejesho ya mikopo
    // kupitia STEP 3.3 ya waterfall.
    public decimal TotalRepaymentsApplied { get; set; }

    // NEW: counts "Withdraw / Transfer fund" rows found in the statement.
    // These are skipped from the deposit waterfall - see Warnings for details.
    public int WithdrawalsSkipped { get; set; }

    // NEW (audit Stage 2: import atomicity): each transaction row is now
    // processed and committed independently, so one bad row (a database
    // constraint violation, an unexpected null, etc.) no longer aborts
    // the entire statement upload. This counts rows that failed for a
    // reason other than being a plain duplicate - see Warnings for which
    // reference numbers and why.
    public int FailedItems { get; set; }
    public List<string> Warnings { get; set; } = new();
}