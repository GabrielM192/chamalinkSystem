using ChamaLink.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChamaLink.Infrastructure.Migrations;

/// <summary>
/// SASISHO21 — KODI YA MALIPO (sehemu ya kwanza).
///
/// Shida: katika kikundi cha wanachama 1,200, kila mtu anatuma pesa kwenye
/// namba moja. Bila kumbukumbu, hazina anasoma ujumbe mmoja mmoja na kukisia
/// ni nani amelipa.
///
/// Suluhisho: kila wajibu wa tukio unapata kodi ya kudumu, mf. "J3-E07-0147".
/// Mwanachama anaiandika kwenye maelezo ya malipo; mfumo unaitumia kulinganisha
/// statement ya miamala.
///
/// Mabadiliko:
///   • EventObligations.PaymentCode — varchar(20), nullable.
///     NULL = wajibu wa kabla ya toleo hili; hujazwa na PaymentCodeBackfill.
///   • Index ya kipekee (GroupId, PaymentCode) — kodi moja haiwezi kuwakilisha
///     wajibu wawili kwenye kikundi kimoja. PostgreSQL huruhusu NULL nyingi
///     kwenye index ya kipekee, hivyo wajibu wa zamani hauathiriki.
///
/// Hakuna jedwali jipya, hakuna ledger ya pili, na hakuna kiasi kilichoandikwa
/// kwenye msimbo. Kodi ni LEBO ya utambulisho pekee.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261023100000_Sasisho21PaymentCode")]
public partial class Sasisho21PaymentCode : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "PaymentCode",
            table: "EventObligations",
            type: "character varying(20)",
            maxLength: 20,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_EventObligations_GroupId_PaymentCode",
            table: "EventObligations",
            columns: new[] { "GroupId", "PaymentCode" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_EventObligations_GroupId_PaymentCode",
            table: "EventObligations");

        migrationBuilder.DropColumn(
            name: "PaymentCode",
            table: "EventObligations");
    }
}
