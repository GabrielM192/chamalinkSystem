using ChamaLink.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChamaLink.Infrastructure.Migrations;

/// <summary>
/// SASISHO19 — KANUNI ZA J3 TOGETHER FOREVER: hatarini kuondolewa, kuondolewa kwa viongozi,
/// na kurudi kwa mwanachama aliyeondolewa.
///
/// Hakuna jedwali jipya na hakuna mabadiliko ya ledgers. Sera nne tu zinaongezwa kwenye
/// TruthGroupSettings — zote zinaanzia 0/false ili kikundi kisicho na kanuni hizi
/// kisiathirike, na viongozi huziweka kwenye Mipangilio (hakuna namba iliyoandikwa
/// kwenye code):
///
///   EventFineGraceDays             siku za neema BAADA ya faini kabla ya kuingia orodha
///                                  ya "hatarini kuondolewa" (J3: 3 siku)
///   RemovalApprovalsRequired       idadi ya viongozi wanaoidhinisha kuondolewa
///                                  (kanuni: ZAIDI ya wawili; service inasimamisha floor ya 3)
///   ReinstatementFineMultiplier    kizidishi cha faini kwa anayerudi (J3: 2 = faini x mara mbili)
///   ReinstatementRequiresJoiningFee true = anayerudi analipa tena ada ya kujiunga
///
/// Hivyo deni la kurudi = deni lililobaki + (kizidishi x faini) + (ada ya kujiunga kama sera inasema).
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261022090000_Sasisho19AtRisk")]
public partial class Sasisho19AtRisk : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "EventFineGraceDays",
            table: "TruthGroupSettings",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "RemovalApprovalsRequired",
            table: "TruthGroupSettings",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<decimal>(
            name: "ReinstatementFineMultiplier",
            table: "TruthGroupSettings",
            type: "numeric(9,2)",
            precision: 9,
            scale: 2,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<bool>(
            name: "ReinstatementRequiresJoiningFee",
            table: "TruthGroupSettings",
            type: "boolean",
            nullable: false,
            defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ReinstatementRequiresJoiningFee", table: "TruthGroupSettings");
        migrationBuilder.DropColumn(name: "ReinstatementFineMultiplier", table: "TruthGroupSettings");
        migrationBuilder.DropColumn(name: "RemovalApprovalsRequired", table: "TruthGroupSettings");
        migrationBuilder.DropColumn(name: "EventFineGraceDays", table: "TruthGroupSettings");
    }
}
