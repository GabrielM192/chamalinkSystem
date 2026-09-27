namespace ChamaLink.Application.DTOs;

// ── MONTHLY MEMBER STATEMENT (Awamu 1 — 2026-09-19) ───────────────────
//
// Ripoti ya WAJIBU na UTEKELEZAJI kwa mwezi mmoja (Compliance View).
// Tofauti na "Mwezi kwa Mwezi" (Ledger View — pesa ngapi zimeingia),
// hii inajibu: "mwezi huu mwanachama alitakiwa kufanya nini, na
// amefanya nini?"
//
//   LENGO   = mchango unaotarajiwa + rejesho la mkopo linalostahili
//   AMETOA  = jumla ya pesa zote zilizofika kwa mwanachama mwezi huo
//   HALI    = Amelipa / Amelipa Sehemu / Hajalipa (kwa mwezi huu tu —
//             SIYO hali ya maisha yote ya mwanachama)

/// <summary>Safu moja: mwanachama mmoja kwa mwezi huo.</summary>
public class MonthlyStatementRowDto
{
    public Guid GroupMemberId { get; set; }
    public Guid UserId { get; set; }
    public string MemberNumber { get; set; } = string.Empty;
    public string MemberName { get; set; } = string.Empty;

    /// <summary>Hali ya maisha yote (Active/Warning/Inactive) — tofauti
    /// na MonthStatus ambayo ni ya mwezi huu pekee.</summary>
    public string OverallStatus { get; set; } = string.Empty;
    public bool IsNonActive { get; set; }

    // ── LENGO (wajibu wa mwezi) ──
    public decimal ExpectedContribution { get; set; }
    public decimal ExpectedRepayment { get; set; }
    public decimal Lengo { get; set; }

    // ── UTEKELEZAJI ──
    /// <summary>Jumla ya pesa zote zilizofika mwezi huu.</summary>
    public decimal Ametoa { get; set; }

    /// <summary>Sehemu ya Ametoa iliyokwenda kwa wajibu (mchango+rejesho),
    /// isiyozidi Lengo.</summary>
    public decimal PaidObligations { get; set; }

    /// <summary>Lengo − PaidObligations (0 ikiwa amekidhi).</summary>
    public decimal Upungufu { get; set; }

    public decimal FineIssued { get; set; }
    public decimal FinePaid { get; set; }
    public decimal RepaymentPaid { get; set; }

    /// <summary>Kiasi kilichobaki → akiba baada ya wajibu wote.</summary>
    public decimal ToSavings { get; set; }

    // ── DENI BAKI (hali ya sasa) ──
    public decimal LoanBalance { get; set; }
    public decimal ContributionDebt { get; set; }

    /// <summary>"Amelipa" / "Amelipa Sehemu" / "Hajalipa" / "—"</summary>
    public string MonthStatus { get; set; } = string.Empty;
}

/// <summary>Jibu kamili la statement ya mwezi.</summary>
public class MonthlyStatementDto
{
    public Guid GroupId { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Month { get; set; }
    public string MonthName { get; set; } = string.Empty;

    /// <summary>Mchango wa kawaida wa kila mwezi (kutoka settings).</summary>
    public decimal MonthlyContributionTarget { get; set; }

    public List<MonthlyStatementRowDto> Rows { get; set; } = new();

    // ── Jumla za kikundi ──
    public decimal TotalLengo { get; set; }
    public decimal TotalAmetoa { get; set; }
    public decimal TotalUpungufu { get; set; }
    public decimal TotalRepayments { get; set; }
    public decimal TotalSavings { get; set; }

    public int CountAmelipa { get; set; }
    public int CountSehemu { get; set; }
    public int CountHajalipa { get; set; }

    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}
