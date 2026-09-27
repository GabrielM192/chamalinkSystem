using ChamaLink.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChamaLink.Infrastructure.Migrations;

/// <summary>
/// Ushahidi wa malipo ya mkononi: aina (EvidenceType) + mahali ulipo (EvidenceReference).
/// Safu ni nullable ili rekodi za zamani zibaki halali; maombi MAPYA yanazihitaji (TruthSetupService).
/// Imeandikwa kwa mkono kama migrations nyingine za Injini ya Ukweli.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260926100000_ManualPaymentEvidence")]
public partial class ManualPaymentEvidence : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.AddColumn<string>(name: "EvidenceType", table: "FinancialEvents",
            type: "character varying(30)", maxLength: 30, nullable: true);
        mb.AddColumn<string>(name: "EvidenceReference", table: "FinancialEvents",
            type: "character varying(200)", maxLength: 200, nullable: true);

        // Sheria ya akiba sasa ni setting ya kikundi (ilikuwa imeandikwa ndani ya code kama true).
        // Vikundi vilivyopo vinaendelea na tabia ile ile waliyokuwa nayo (true); viongozi wanaweza kuibadilisha.
        mb.AddColumn<bool>(name: "AutoDeductSavings", table: "TruthGroupSettings",
            type: "boolean", nullable: false, defaultValue: true);
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropColumn(name: "AutoDeductSavings", table: "TruthGroupSettings");
        mb.DropColumn(name: "EvidenceReference", table: "FinancialEvents");
        mb.DropColumn(name: "EvidenceType", table: "FinancialEvents");
    }
}
