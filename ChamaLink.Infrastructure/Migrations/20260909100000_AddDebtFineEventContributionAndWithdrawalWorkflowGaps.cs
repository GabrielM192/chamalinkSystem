using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChamaLink.Infrastructure.Migrations
{
    /// <inheritdoc />
    // BUG FIX: the Fine, Debt, EventContribution and ImportedStatement
    // entities (Sprint 1 + Sprint 2 gaps) were already fully modelled in
    // ApplicationDbContext/OnModelCreating and used throughout
    // DebtService/FineService/AnalyticsService/MkobaImportController, but
    // no migration had ever actually created their tables - so every one
    // of those code paths would fail at runtime with "relation does not
    // exist". Likewise GroupEvent.BeneficiaryGroupMemberId/BeneficiaryName
    // and Withdrawal.Status/RejectionReason/DecisionAt were referenced in
    // code but missing from the database. This migration brings the
    // schema in line with the model that was already written.
    public partial class AddDebtFineEventContributionAndWithdrawalWorkflowGaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // BUG FIX: OnModelCreating specifies precision(18,2) for
            // GroupEvent.TargetAmountPerMember, but the column was
            // originally created as unconstrained "numeric" - align it so
            // future `dotnet ef migrations add` calls don't keep re-diffing
            // this column forever.
            migrationBuilder.AlterColumn<decimal>(
                name: "TargetAmountPerMember",
                table: "GroupEvents",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            // ----- GroupEvent beneficiary tracking -----
            migrationBuilder.AddColumn<Guid>(
                name: "BeneficiaryGroupMemberId",
                table: "GroupEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BeneficiaryName",
                table: "GroupEvents",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_GroupEvents_BeneficiaryGroupMemberId",
                table: "GroupEvents",
                column: "BeneficiaryGroupMemberId");

            migrationBuilder.AddForeignKey(
                name: "FK_GroupEvents_GroupMembers_BeneficiaryGroupMemberId",
                table: "GroupEvents",
                column: "BeneficiaryGroupMemberId",
                principalTable: "GroupMembers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // ----- Withdrawal approval workflow columns -----
            migrationBuilder.DropForeignKey(
                name: "FK_Withdrawals_GroupMembers_ApprovedByGroupMemberId",
                table: "Withdrawals");

            migrationBuilder.AlterColumn<Guid>(
                name: "ApprovedByGroupMemberId",
                table: "Withdrawals",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Withdrawals",
                type: "text",
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.AddColumn<DateTime>(
                name: "DecisionAt",
                table: "Withdrawals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "Withdrawals",
                type: "text",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Withdrawals_GroupMembers_ApprovedByGroupMemberId",
                table: "Withdrawals",
                column: "ApprovedByGroupMemberId",
                principalTable: "GroupMembers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // ----- Fines (Sprint 1 gap: Fine Entity Haipo) -----
            migrationBuilder.CreateTable(
                name: "Fines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AmountPaid = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ReasonType = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    GroupEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    Period = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Fines_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Fines_GroupMembers_GroupMemberId",
                        column: x => x.GroupMemberId,
                        principalTable: "GroupMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Fines_GroupEvents_GroupEventId",
                        column: x => x.GroupEventId,
                        principalTable: "GroupEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(name: "IX_Fines_GroupId", table: "Fines", column: "GroupId");
            migrationBuilder.CreateIndex(name: "IX_Fines_GroupEventId", table: "Fines", column: "GroupEventId");
            migrationBuilder.CreateIndex(
                name: "IX_Fines_GroupMemberId_Period",
                table: "Fines",
                columns: new[] { "GroupMemberId", "Period" });

            // ----- Debts (Sprint 1 gap: Debt Table Haipo) -----
            migrationBuilder.CreateTable(
                name: "Debts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AmountCleared = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    Period = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClearedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Debts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Debts_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Debts_GroupMembers_GroupMemberId",
                        column: x => x.GroupMemberId,
                        principalTable: "GroupMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(name: "IX_Debts_GroupId", table: "Debts", column: "GroupId");
            migrationBuilder.CreateIndex(
                name: "IX_Debts_GroupMemberId_Period",
                table: "Debts",
                columns: new[] { "GroupMemberId", "Period" });

            // ----- EventContributions (Sprint 2 gap: Event Contribution Tracking Haipo) -----
            migrationBuilder.CreateTable(
                name: "EventContributions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpectedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PaidAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastPaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventContributions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventContributions_GroupEvents_GroupEventId",
                        column: x => x.GroupEventId,
                        principalTable: "GroupEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EventContributions_GroupMembers_GroupMemberId",
                        column: x => x.GroupMemberId,
                        principalTable: "GroupMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventContributions_GroupMemberId",
                table: "EventContributions",
                column: "GroupMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_EventContributions_GroupEventId_GroupMemberId",
                table: "EventContributions",
                columns: new[] { "GroupEventId", "GroupMemberId" },
                unique: true);

            // ----- ImportedStatements (Sprint 1 gap #18: Duplicate Import Protection) -----
            migrationBuilder.CreateTable(
                name: "ImportedStatements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileHash = table.Column<string>(type: "text", nullable: false),
                    FileName = table.Column<string>(type: "text", nullable: false),
                    TransactionCount = table.Column<int>(type: "integer", nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportedStatements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportedStatements_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImportedStatements_GroupId_FileHash",
                table: "ImportedStatements",
                columns: new[] { "GroupId", "FileHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ImportedStatements");
            migrationBuilder.DropTable(name: "EventContributions");
            migrationBuilder.DropTable(name: "Debts");
            migrationBuilder.DropTable(name: "Fines");

            migrationBuilder.DropForeignKey(
                name: "FK_Withdrawals_GroupMembers_ApprovedByGroupMemberId",
                table: "Withdrawals");

            migrationBuilder.DropColumn(name: "RejectionReason", table: "Withdrawals");
            migrationBuilder.DropColumn(name: "DecisionAt", table: "Withdrawals");
            migrationBuilder.DropColumn(name: "Status", table: "Withdrawals");

            migrationBuilder.AlterColumn<Guid>(
                name: "ApprovedByGroupMemberId",
                table: "Withdrawals",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Withdrawals_GroupMembers_ApprovedByGroupMemberId",
                table: "Withdrawals",
                column: "ApprovedByGroupMemberId",
                principalTable: "GroupMembers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.DropForeignKey(
                name: "FK_GroupEvents_GroupMembers_BeneficiaryGroupMemberId",
                table: "GroupEvents");

            migrationBuilder.DropIndex(
                name: "IX_GroupEvents_BeneficiaryGroupMemberId",
                table: "GroupEvents");

            migrationBuilder.DropColumn(name: "BeneficiaryName", table: "GroupEvents");
            migrationBuilder.DropColumn(name: "BeneficiaryGroupMemberId", table: "GroupEvents");

            migrationBuilder.AlterColumn<decimal>(
                name: "TargetAmountPerMember",
                table: "GroupEvents",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);
        }
    }
}
