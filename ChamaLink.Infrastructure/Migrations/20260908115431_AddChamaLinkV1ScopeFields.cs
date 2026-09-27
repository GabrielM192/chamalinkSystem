using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChamaLink.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChamaLinkV1ScopeFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LatePenaltyAmount",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "MonthlyContributionAmount",
                table: "GroupSettings");

            migrationBuilder.AddColumn<int>(
                name: "GracePeriodDays",
                table: "GroupSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "JoiningFee",
                table: "GroupSettings",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MinimumReserveBalance",
                table: "GroupSettings",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "WalletWithdrawals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferenceNo = table.Column<string>(type: "text", nullable: false),
                    MemberName = table.Column<string>(type: "text", nullable: false),
                    PhoneNumber = table.Column<string>(type: "text", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TransactionDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsReconciled = table.Column<bool>(type: "boolean", nullable: false),
                    ReconciliationNote = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletWithdrawals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WalletWithdrawals_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Withdrawals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Purpose = table.Column<string>(type: "text", nullable: false),
                    BeneficiaryName = table.Column<string>(type: "text", nullable: false),
                    ApprovedByGroupMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordedByGroupMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferenceNo = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    WalletWithdrawalId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Withdrawals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Withdrawals_GroupMembers_ApprovedByGroupMemberId",
                        column: x => x.ApprovedByGroupMemberId,
                        principalTable: "GroupMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Withdrawals_GroupMembers_RecordedByGroupMemberId",
                        column: x => x.RecordedByGroupMemberId,
                        principalTable: "GroupMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Withdrawals_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Withdrawals_WalletWithdrawals_WalletWithdrawalId",
                        column: x => x.WalletWithdrawalId,
                        principalTable: "WalletWithdrawals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WalletWithdrawals_GroupId_ReferenceNo",
                table: "WalletWithdrawals",
                columns: new[] { "GroupId", "ReferenceNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Withdrawals_ApprovedByGroupMemberId",
                table: "Withdrawals",
                column: "ApprovedByGroupMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_Withdrawals_GroupId",
                table: "Withdrawals",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Withdrawals_RecordedByGroupMemberId",
                table: "Withdrawals",
                column: "RecordedByGroupMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_Withdrawals_WalletWithdrawalId",
                table: "Withdrawals",
                column: "WalletWithdrawalId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Withdrawals");

            migrationBuilder.DropTable(
                name: "WalletWithdrawals");

            migrationBuilder.DropColumn(
                name: "GracePeriodDays",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "JoiningFee",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "MinimumReserveBalance",
                table: "GroupSettings");

            migrationBuilder.AddColumn<decimal>(
                name: "LatePenaltyAmount",
                table: "GroupSettings",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MonthlyContributionAmount",
                table: "GroupSettings",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);
        }
    }
}
