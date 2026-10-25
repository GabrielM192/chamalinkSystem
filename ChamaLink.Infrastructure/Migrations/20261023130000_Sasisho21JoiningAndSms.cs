using ChamaLink.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChamaLink.Infrastructure.Migrations;

/// <summary>
/// SASISHO21 — SEHEMU YA PILI: LANGO LA KUJIUNGA na SMS ZA NO-REPLY.
///
/// 1. LANGO LA KUJIUNGA
///    Kikundi kinaweza kuamua: "lipa kwanza, kisha uwe mwanachama".
///    Ombi la kujiunga linapata kodi yake (J3-JOIN-0007), kiasi na tarehe ya mwisho.
///    Malipo yanathibitishwa kwenye sanduku la malipo; ndipo kiongozi anaidhinisha
///    uanachama, na pesa inahamishiwa kwenye rekodi mpya.
///    Deni HALIZALIWI: ombi lisilolipwa linaisha (Expired), si deni la milele.
///
/// 2. SMS
///    SmsMessages — mnyororo kamili: foleni -> mtoa-huduma -> mtandao -> simu -> hali.
///    SmsAccounts — salio la CREDITS (hazina), si bajeti ya mwezi.
///
/// Hakuna jedwali la fedha lililopo linabadilishwa; hakuna salio linaloandikwa.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261023130000_Sasisho21JoiningAndSms")]
public partial class Sasisho21JoiningAndSms : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ---------- 1. LANGO LA KUJIUNGA ----------
        migrationBuilder.AddColumn<string>(
            name: "PaymentCode", table: "MembershipRequests",
            type: "character varying(20)", maxLength: 20, nullable: true);
        migrationBuilder.AddColumn<decimal>(
            name: "PaymentAmount", table: "MembershipRequests",
            type: "numeric(18,2)", precision: 18, scale: 2, nullable: true);
        migrationBuilder.AddColumn<DateTime>(
            name: "PaymentDeadline", table: "MembershipRequests",
            type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<DateTime>(
            name: "PaymentConfirmedAt", table: "MembershipRequests",
            type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<Guid>(
            name: "PaymentReceiptId", table: "MembershipRequests",
            type: "uuid", nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "PaymentReference", table: "MembershipRequests",
            type: "character varying(80)", maxLength: 80, nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_MembershipRequests_GroupId_PaymentCode",
            table: "MembershipRequests",
            columns: new[] { "GroupId", "PaymentCode" },
            unique: true,
            filter: "\"PaymentCode\" IS NOT NULL");

        // ---------- 2. RISITI: aina + ombi ----------
        migrationBuilder.AddColumn<string>(
            name: "Kind", table: "PaymentReceipts",
            type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Contribution");
        migrationBuilder.AddColumn<Guid>(
            name: "MembershipRequestId", table: "PaymentReceipts",
            type: "uuid", nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_PaymentReceipts_GroupId_MembershipRequestId",
            table: "PaymentReceipts",
            columns: new[] { "GroupId", "MembershipRequestId" });

        // ---------- 3. SMS ----------
        migrationBuilder.CreateTable(
            name: "SmsMessages",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                GroupMemberId = table.Column<Guid>(type: "uuid", nullable: true),
                Phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                TemplateKey = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                Body = table.Column<string>(type: "character varying(360)", maxLength: 360, nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                SendAfter = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                Attempts = table.Column<int>(type: "integer", nullable: false),
                ProviderReference = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                ProviderStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                FailureReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                SuppressedReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                Credits = table.Column<int>(type: "integer", nullable: false),
                RelatedEntityType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                RelatedEntityId = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                DeliveredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => { table.PrimaryKey("PK_SmsMessages", x => x.Id); });

        migrationBuilder.CreateIndex(
            name: "IX_SmsMessages_GroupId_Status_SendAfter",
            table: "SmsMessages",
            columns: new[] { "GroupId", "Status", "SendAfter" });
        migrationBuilder.CreateIndex(
            name: "IX_SmsMessages_GroupId_CreatedAt",
            table: "SmsMessages",
            columns: new[] { "GroupId", "CreatedAt" });
        migrationBuilder.CreateIndex(
            name: "IX_SmsMessages_ProviderReference",
            table: "SmsMessages",
            column: "ProviderReference");

        migrationBuilder.CreateTable(
            name: "SmsAccounts",
            columns: table => new
            {
                GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                Credits = table.Column<int>(type: "integer", nullable: false),
                TotalPurchased = table.Column<int>(type: "integer", nullable: false),
                TotalSent = table.Column<int>(type: "integer", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table => { table.PrimaryKey("PK_SmsAccounts", x => x.GroupId); });

        // ---------- 4. Sera moja mpya ----------
        migrationBuilder.AddColumn<int>(
            name: "SmsMaxCharsPerMessage", table: "TruthGroupSettings",
            type: "integer", nullable: false, defaultValue: 0);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "SmsMaxCharsPerMessage", table: "TruthGroupSettings");
        migrationBuilder.DropTable(name: "SmsAccounts");
        migrationBuilder.DropTable(name: "SmsMessages");

        migrationBuilder.DropIndex(name: "IX_PaymentReceipts_GroupId_MembershipRequestId", table: "PaymentReceipts");
        migrationBuilder.DropColumn(name: "MembershipRequestId", table: "PaymentReceipts");
        migrationBuilder.DropColumn(name: "Kind", table: "PaymentReceipts");

        migrationBuilder.DropIndex(name: "IX_MembershipRequests_GroupId_PaymentCode", table: "MembershipRequests");
        migrationBuilder.DropColumn(name: "PaymentReference", table: "MembershipRequests");
        migrationBuilder.DropColumn(name: "PaymentReceiptId", table: "MembershipRequests");
        migrationBuilder.DropColumn(name: "PaymentConfirmedAt", table: "MembershipRequests");
        migrationBuilder.DropColumn(name: "PaymentDeadline", table: "MembershipRequests");
        migrationBuilder.DropColumn(name: "PaymentAmount", table: "MembershipRequests");
        migrationBuilder.DropColumn(name: "PaymentCode", table: "MembershipRequests");
    }
}
