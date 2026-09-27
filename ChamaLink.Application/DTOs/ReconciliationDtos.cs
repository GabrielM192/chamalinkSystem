namespace ChamaLink.Application.DTOs;

// ── RECONCILIATION ENGINE (Ulinganisho) — 2026-09-19 ──────────────────
//
// Lengo (P0 kwa Ukonga): kuthibitisha kuwa
//
//     M-Koba PDF  =  Excel ya viongozi  =  Ledger ya ChamaLink
//
// kwa kila mwanachama na kila mwezi. Chombo hiki NI READ-ONLY:
// hakinaandiki kitu kwenye database — kinaunganisha nyaraka mbili
// (zinazopakiwa wakati huo huo) na ledger iliyopo, kisha kinaonyesha
// tofauti na sababu zake.
//
// Vyanzo vitatu:
//   PDF    = taarifa ya M-Koba (ground truth ya pesa zilizofika pochi)
//   EXCEL  = jedwali la mtunza-hazina (interpretation ya kikundi)
//   LEDGER = entries za ChamaLink (Contribution + FinePayment +
//            JoiningFee + EventContribution za mwezi huo)

/// <summary>Hali ya mstari mmoja wa ulinganisho.</summary>
public enum ReconciliationStatus
{
    /// <summary>Vyanzo vyote vinapatana (au tofauti ni 0).</summary>
    Sawa = 0,

    /// <summary>PDF na Excel hazilingani kati yake (makosa ya kurekodi).</summary>
    NyarakaHazilingani = 1,

    /// <summary>Ledger ina chini kuliko nyaraka (bado haijaingizwa mfumo).</summary>
    MfumoChini = 2,

    /// <summary>Ledger ina zaidi kuliko nyaraka (mchango wa mkono, au
    /// import mara mbili, au ziada halisi).</summary>
    MfumoZiada = 3
}

/// <summary>Mstari mmoja: mwanachama mmoja × mwezi mmoja.</summary>
public class ReconciliationRowDto
{
    public Guid? GroupMemberId { get; set; }
    public string MemberName { get; set; } = string.Empty;
    public string MemberNumber { get; set; } = string.Empty;

    /// <summary>Mwezi 1-12; 0 = mstari maalum wa KIANZIO (jumla ya mwaka).</summary>
    public int Month { get; set; }

    /// <summary>JAN/FEB/... au "KIANZIO".</summary>
    public string MonthLabel { get; set; } = string.Empty;

    /// <summary>Jumla ya maduhuli (deposits) kwenye taarifa ya M-Koba.</summary>
    public decimal PdfAmount { get; set; }

    /// <summary>Seli ya Excel (mchango + faini iliyofungamanishwa).</summary>
    public decimal ExcelAmount { get; set; }

    /// <summary>Jumla ya entries za ledger mwezi huo.</summary>
    public decimal LedgerAmount { get; set; }

    /// <summary>Ledger − (chanzo kikubwa zaidi kati ya PDF/Excel).</summary>
    public decimal Difference { get; set; }

    public ReconciliationStatus Status { get; set; }

    /// <summary>Maelezo ya kibinadamu ya sababu ya tofauti.</summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Jibu kamili la ulinganisho.</summary>
public class ReconciliationDto
{
    public Guid GroupId { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public int Year { get; set; }

    /// <summary>Je, faili la M-Koba PDF liliwasilishwa?</summary>
    public bool HasPdf { get; set; }

    /// <summary>Je, faili la Excel liliwasilishwa?</summary>
    public bool HasExcel { get; set; }

    public List<ReconciliationRowDto> Rows { get; set; } = new();

    /// <summary>Jumla za vyanzo vyote vitatu (michango ya miezi tu).</summary>
    public decimal TotalPdf { get; set; }
    public decimal TotalExcel { get; set; }
    public decimal TotalLedger { get; set; }

    /// <summary>Idadi ya mistari kwa kila hali.</summary>
    public int CountSawa { get; set; }
    public int CountNyarakaHazilingani { get; set; }
    public int CountMfumoChini { get; set; }
    public int CountMfumoZiada { get; set; }

    /// <summary>
    /// Maonyo: safu za nyaraka zisizolingana na mwanachama yeyote,
    /// withdrawals za M-Koba, n.k.
    /// </summary>
    public List<string> Warnings { get; set; } = new();

    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}
