using ChamaLink.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChamaLink.Infrastructure.Migrations;

/// <summary>
/// SASISHO18 — vikundi vinavyochangishana KILA TUKIO.
///
///  1. TruthGroupSettings: sera ya pochi —
///       MemberMinimumBalance, TopUpWindowDays, OverdueEventFineAmount,
///       MaxDeductionPerMemberPerMonth, MaxEventsPerMonth, BeneficiaryContributes.
///     Zote zinaanzia 0/false: hakuna thamani ya fedha iliyoandikwa kwenye code,
///     viongozi huziweka kwenye Mipangilio.
///
///  2. EventObligations: wajibu wa mwanachama mmoja kwa tukio moja (BenefitCase).
///       UNIQUE(BenefitCaseId, GroupMemberId) — tukio moja haliwezi kumkata
///       mwanachama mara mbili hata idhini irudiwe.
///
/// Hakuna Jedwali la tatu: salio la pochi = (pesa zilizokubaliwa kwa mwanachama,
/// kutoka Injini ya Ukweli) − (jumla ya DeductedAmount za wajibu wake).
/// Hakuna kuandika kwenye LedgerEntry — hivyo hakuna "kitabu cha pili".
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261021120000_Sasisho18EventGroups")]
public partial class Sasisho18EventGroups : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ---- 1. Sera ya pochi (GroupSettings) ----
        migrationBuilder.AddColumn<decimal>(
            name: "MemberMinimumBalance", table: "TruthGroupSettings",
            type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m);

        migrationBuilder.AddColumn<int>(
            name: "EventTopUpWindowDays", table: "TruthGroupSettings",
            type: "integer", nullable: false, defaultValue: 0);

        migrationBuilder.AddColumn<decimal>(
            name: "OverdueEventFineAmount", table: "TruthGroupSettings",
            type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m);

        migrationBuilder.AddColumn<decimal>(
            name: "MaxEventDeductionPerMemberPerMonth", table: "TruthGroupSettings",
            type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m);

        migrationBuilder.AddColumn<int>(
            name: "MaxEventsPerMonth", table: "TruthGroupSettings",
            type: "integer", nullable: false, defaultValue: 0);

        migrationBuilder.AddColumn<bool>(
            name: "BeneficiaryContributes", table: "TruthGroupSettings",
            type: "boolean", nullable: false, defaultValue: false);

        // ---- 2. Wajibu wa tukio ----
        migrationBuilder.CreateTable(
            name: "EventObligations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                BenefitCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                GroupMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                RequiredAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                DeductedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                Status = table.Column<string>(type: "text", nullable: false),
                DueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                SettledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                WaivedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                WaiveReason = table.Column<string>(type: "text", nullable: true),
                OverdueFineIssued = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EventObligations", x => x.Id);
                table.ForeignKey(
                    name: "FK_EventObligations_Groups_GroupId",
                    column: x => x.GroupId, principalTable: "Groups", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_EventObligations_GroupMembers_GroupMemberId",
                    column: x => x.GroupMemberId, principalTable: "GroupMembers", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
            });

        // Kinga kuu: tukio moja × mwanachama mmoja = rekodi moja tu.
        migrationBuilder.CreateIndex(
            name: "IX_EventObligations_BenefitCaseId_GroupMemberId",
            table: "EventObligations", columns: new[] { "BenefitCaseId", "GroupMemberId" }, unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_EventObligations_GroupId_GroupMemberId_Status",
            table: "EventObligations", columns: new[] { "GroupId", "GroupMemberId", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_EventObligations_GroupMemberId",
            table: "EventObligations", column: "GroupMemberId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "EventObligations");

        migrationBuilder.DropColumn(name: "MemberMinimumBalance", table: "TruthGroupSettings");
        migrationBuilder.DropColumn(name: "EventTopUpWindowDays", table: "TruthGroupSettings");
        migrationBuilder.DropColumn(name: "OverdueEventFineAmount", table: "TruthGroupSettings");
        migrationBuilder.DropColumn(name: "MaxEventDeductionPerMemberPerMonth", table: "TruthGroupSettings");
        migrationBuilder.DropColumn(name: "MaxEventsPerMonth", table: "TruthGroupSettings");
        migrationBuilder.DropColumn(name: "BeneficiaryContributes", table: "TruthGroupSettings");
    }
}
