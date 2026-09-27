namespace ChamaLink.Application.DTOs;

// Parsed row from a treasurer's monthly-grid Excel ledger (mtunza-hazina
// manual record). One row per member; columns are JAN..DEC monthly
// contribution totals (or null when unpaid / '*'), plus separate KIANZIO
// (joining fee), MSIBA and SHEREHE (welfare payout) columns, and an
// optional phone column used only as a hint (matching is by name).
public class TreasuryExcelRowDto
{
    public string ExcelName { get; set; } = string.Empty;
    public string? Phone { get; set; }

    // 12 monthly amounts. null = no contribution that month (a '*' cell or
    // a blank). A real number = the total deposited that month.
    public decimal?[] MonthlyAmounts { get; set; } = new decimal?[12];

    public decimal? JoiningFee { get; set; }

    // Welfare payouts the group paid TO this member (not contributions).
    // Deferred in v1 - reported in warnings, not posted.
    public decimal? Msiba { get; set; }
    public decimal? Sherehe { get; set; }

    // True when the row contained a "NON ACTIVE MEMBER" marker spanning
    // 3 month columns. NonActiveFromMonth is the 1-based month index where
    // the marker began (the member stopped being active from there).
    public bool IsNonActive { get; set; }
    public int NonActiveFromMonth { get; set; }

    // Raw row number in the sheet, for reference in warnings/overrides.
    public int RowNumber { get; set; }
}

// A candidate name match for a row whose name did not exact-match.
public class TreasuryMatchCandidateDto
{
    public Guid MemberId { get; set; }
    public string MemberName { get; set; } = string.Empty;
    public int Confidence { get; set; } // 0-100
}

// How the importer plans to SPLIT a single month's cell amount into
// structured ledger entries. The treasurer's Excel bundles a late fine
// into the monthly cell (e.g. 15,000 = 10,000 mchango + 5,000 faini),
// so the importer must un-bundle it:
//   - Contribution = min(cell, MonthlyContribution)        -> Contribution ledger entry
//   - Fine         = LateFine, ONLY when excess == LateFine -> Fine entity (issued+paid)
//   - Savings      = any other excess                       -> AdvanceBalance (akiba)
// This is RECORDING the treasurer's manual allocation, not running the
// live waterfall (Option B for historical imports).
public class TreasuryPlannedSplitDto
{
    public int Month { get; set; }              // 1-12
    public string MonthName { get; set; } = string.Empty;
    public decimal? TotalAmount { get; set; }   // raw cell; null = unpaid (*)
    public bool IsUnpaid => !TotalAmount.HasValue || TotalAmount.Value <= 0;
    public decimal Contribution { get; set; }
    public decimal Fine { get; set; }

    // FIX (2026-09-18): ziada sasa inafuata sheria ya kikundi (Ukonga):
    // kwanza KIANZIO isiyolipwa, kisha madeni ya michango, kisha akiba.
    // Zamani `Savings` ilichukua ziada YOTE moja kwa moja.
    //
    // 2026-09-19: mpangilio mpya kamili wa ziada:
    //   REJESHO la mkopo (linalostahili mwezi huo) -> KIANZIO -> madeni -> akiba
    public decimal RepaymentApplied { get; set; }   // sehemu ya ziada → rejesho la mkopo
    public decimal JoiningFeeApplied { get; set; }  // sehemu ya ziada → KIANZIO
    public decimal DebtCleared { get; set; }        // sehemu ya ziada → madeni
    public decimal Savings { get; set; }            // kilichobaki → akiba
    public string SplitNote { get; set; } = string.Empty; // e.g. "fine (alichelewa)", "akiba"
}

// One row of the preview result: what the importer WOULD do if committed.
public class TreasuryImportPreviewRowDto
{
    public int RowNumber { get; set; }
    public string ExcelName { get; set; } = string.Empty;

    public Guid? MatchedMemberId { get; set; }
    public string? MatchedMemberName { get; set; }
    public int MatchConfidence { get; set; } // 0-100; 100 = exact
    public bool IsExactMatch { get; set; }
    public List<TreasuryMatchCandidateDto> Candidates { get; set; } = new();

    public decimal?[] MonthlyAmounts { get; set; } = new decimal?[12];
    public decimal? JoiningFee { get; set; }
    public decimal? Msiba { get; set; }
    public decimal? Sherehe { get; set; }
    public bool IsNonActive { get; set; }

    // Per-month breakdown of how each cell will be split into
    // Contribution / Fine / Savings when committed. Lets the treasurer
    // verify the un-bundling (e.g. confirm 15,000 = mchango + faini)
    // before anything is written.
    public List<TreasuryPlannedSplitDto> PlannedSplits { get; set; } = new();

    public List<string> Warnings { get; set; } = new();
}

public class TreasuryImportPreviewDto
{
    public Guid GroupId { get; set; }
    public int? DetectedYear { get; set; } // if a header/formula hints at it; usually null
    public List<TreasuryImportPreviewRowDto> Rows { get; set; } = new();
    public List<string> Warnings { get; set; } = new();

    public int TotalRows { get; set; }
    public int ExactMatchedRows { get; set; }
    public int FuzzyMatchedRows { get; set; }
    public int UnmatchedRows { get; set; }
}

public class TreasuryImportResultDto
{
    public int TotalRows { get; set; }
    public int ContributionsPosted { get; set; }
    public int JoiningFeesPosted { get; set; }
    public int FinesPosted { get; set; }          // bundled late fines un-bundled from cells
    public int SavingsPosted { get; set; }        // excess-to-akiba entries
    public int DebtClearancesPosted { get; set; } // excess-to-debt entries (FIX 2026-09-18)
    public int RepaymentsPosted { get; set; }     // excess-to-loan-repayment (Awamu 1, 2026-09-19)
    public int EntriesSkippedDuplicate { get; set; }
    public int MembersMarkedInactive { get; set; }
    public int UnmatchedRowsSkipped { get; set; }
    public decimal TotalContributionsAmount { get; set; }
    public decimal TotalJoiningFeesAmount { get; set; }
    public decimal TotalFinesAmount { get; set; }
    public decimal TotalSavingsAmount { get; set; }
    public decimal TotalDebtClearedAmount { get; set; } // FIX 2026-09-18
    public decimal TotalRepaymentsAmount { get; set; }  // Awamu 1, 2026-09-19

    // Members + amounts whose MSIBA/SHEREHE welfare payouts were NOT
    // posted (deferred to v2 - needs GroupEvent model + katiba rules).
    public List<string> MsibaShereheDeferred { get; set; } = new();

    public List<string> SkippedUnmatched { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}
