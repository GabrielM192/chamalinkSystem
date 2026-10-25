using ChamaLink.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChamaLink.Infrastructure.Migrations;

/// <summary>
/// SASISHO21 — SANDUKU LA MALIPO.
///
/// Sehemu ya pili ya toleo: risiti (mstari wa statement unaosubiri kuthibitishwa),
/// madai ya wanachama ("nimelipa"), na umiliki uliothibitishwa wa mstari wa M-Koba.
///
/// MAMBO MATATU YA MSINGI:
///   1. Hakuna pesa mpya. PaymentReceipts inaelekeza kwenye MkobaRawTransactions
///      iliyopo; kuthibitisha kunabadilisha AttributedGroupMemberId ya mstari ule.
///   2. MkobaRawTransaction yenye mmiliki HAIMILIKIWI tena kwa namba ya simu —
///      kwa hiyo hakuna kuhesabu mara mbili kwenye Injini ya Ukweli.
///   3. Sera mpya zote zinaanzia 0/false: kikundi kisichozitumia hakiathiriki,
///      na hakuna kiasi cha fedha kilichoandikwa kwenye msimbo.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261023110000_Sasisho21PaymentInbox")]
public partial class Sasisho21PaymentInbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ---- 1. Umiliki wa mstari wa M-Koba ----
        migrationBuilder.AddColumn<Guid>(
            name: "AttributedGroupMemberId", table: "MkobaRawTransactions",
            type: "uuid", nullable: true);
        migrationBuilder.AddColumn<Guid>(
            name: "AttributedByUserId", table: "MkobaRawTransactions",
            type: "uuid", nullable: true);
        migrationBuilder.AddColumn<DateTime>(
            name: "AttributedAt", table: "MkobaRawTransactions",
            type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "AttributionReason", table: "MkobaRawTransactions",
            type: "character varying(300)", maxLength: 300, nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_MkobaRawTransactions_GroupId_AttributedGroupMemberId",
            table: "MkobaRawTransactions",
            columns: new[] { "GroupId", "AttributedGroupMemberId" });

        // ---- 2. Risiti za malipo ----
        migrationBuilder.CreateTable(
            name: "PaymentReceipts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                MkobaTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                Reference = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                Phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                PayerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Description = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                PaymentCodeSeen = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                SuggestedGroupMemberId = table.Column<Guid>(type: "uuid", nullable: true),
                MatchRule = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                SuggestedObligationId = table.Column<Guid>(type: "uuid", nullable: true),
                ConfirmedGroupMemberId = table.Column<Guid>(type: "uuid", nullable: true),
                ConfirmedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                ConfirmedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                SecondApproverUserId = table.Column<Guid>(type: "uuid", nullable: true),
                SecondApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                ClaimedByGroupMemberId = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PaymentReceipts", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PaymentReceipts_GroupId_Status",
            table: "PaymentReceipts",
            columns: new[] { "GroupId", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_PaymentReceipts_GroupId_MkobaTransactionId",
            table: "PaymentReceipts",
            columns: new[] { "GroupId", "MkobaTransactionId" },
            unique: true,
            filter: "\"MkobaTransactionId\" IS NOT NULL");

        // ---- 3. Madai ya wanachama ----
        migrationBuilder.CreateTable(
            name: "PaymentClaims",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                GroupMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                Reference = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                PaymentCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                ReceiptId = table.Column<Guid>(type: "uuid", nullable: true),
                DecidedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                DecisionNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PaymentClaims", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PaymentClaims_GroupId_Status",
            table: "PaymentClaims",
            columns: new[] { "GroupId", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_PaymentClaims_GroupId_GroupMemberId",
            table: "PaymentClaims",
            columns: new[] { "GroupId", "GroupMemberId" });

        migrationBuilder.CreateIndex(
            name: "IX_PaymentClaims_GroupId_Reference",
            table: "PaymentClaims",
            columns: new[] { "GroupId", "Reference" },
            unique: true);

        // ---- 4. Sera za kikundi ----
        migrationBuilder.AddColumn<decimal>(
            name: "PaymentSecondApprovalAmount", table: "TruthGroupSettings",
            type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<bool>(
            name: "ClaimsEnabled", table: "TruthGroupSettings",
            type: "boolean", nullable: false, defaultValue: true);
        migrationBuilder.AddColumn<bool>(
            name: "JoiningPaymentRequired", table: "TruthGroupSettings",
            type: "boolean", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<decimal>(
            name: "JoiningSavingsPortion", table: "TruthGroupSettings",
            type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<int>(
            name: "JoiningPaymentWindowDays", table: "TruthGroupSettings",
            type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<bool>(
            name: "SmsEnabled", table: "TruthGroupSettings",
            type: "boolean", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<int>(
            name: "SmsLowCreditThreshold", table: "TruthGroupSettings",
            type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(
            name: "SmsQuietFromHour", table: "TruthGroupSettings",
            type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(
            name: "SmsQuietToHour", table: "TruthGroupSettings",
            type: "integer", nullable: false, defaultValue: 24);
        migrationBuilder.AddColumn<int>(
            name: "SmsMaxPerMemberPerEvent", table: "TruthGroupSettings",
            type: "integer", nullable: false, defaultValue: 0);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "SmsMaxPerMemberPerEvent", table: "TruthGroupSettings");
        migrationBuilder.DropColumn(name: "SmsQuietToHour", table: "TruthGroupSettings");
        migrationBuilder.DropColumn(name: "SmsQuietFromHour", table: "TruthGroupSettings");
        migrationBuilder.DropColumn(name: "SmsLowCreditThreshold", table: "TruthGroupSettings");
        migrationBuilder.DropColumn(name: "SmsEnabled", table: "TruthGroupSettings");
        migrationBuilder.DropColumn(name: "JoiningPaymentWindowDays", table: "TruthGroupSettings");
        migrationBuilder.DropColumn(name: "JoiningSavingsPortion", table: "TruthGroupSettings");
        migrationBuilder.DropColumn(name: "JoiningPaymentRequired", table: "TruthGroupSettings");
        migrationBuilder.DropColumn(name: "ClaimsEnabled", table: "TruthGroupSettings");
        migrationBuilder.DropColumn(name: "PaymentSecondApprovalAmount", table: "TruthGroupSettings");

        migrationBuilder.DropTable(name: "PaymentClaims");
        migrationBuilder.DropTable(name: "PaymentReceipts");

        migrationBuilder.DropIndex(
            name: "IX_MkobaRawTransactions_GroupId_AttributedGroupMemberId",
            table: "MkobaRawTransactions");

        migrationBuilder.DropColumn(name: "AttributionReason", table: "MkobaRawTransactions");
        migrationBuilder.DropColumn(name: "AttributedAt", table: "MkobaRawTransactions");
        migrationBuilder.DropColumn(name: "AttributedByUserId", table: "MkobaRawTransactions");
        migrationBuilder.DropColumn(name: "AttributedGroupMemberId", table: "MkobaRawTransactions");
    }
}
