using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChamaLink.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWithdrawalGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomApprovalRoles",
                table: "GroupSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CustomRequiredApprovals",
                table: "GroupSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WithdrawalApprovalMode",
                table: "GroupSettings",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "WithdrawalApprovals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WithdrawalId = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleAtDecision = table.Column<string>(type: "text", nullable: false),
                    Approved = table.Column<bool>(type: "boolean", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WithdrawalApprovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WithdrawalApprovals_GroupMembers_GroupMemberId",
                        column: x => x.GroupMemberId,
                        principalTable: "GroupMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WithdrawalApprovals_Withdrawals_WithdrawalId",
                        column: x => x.WithdrawalId,
                        principalTable: "Withdrawals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WithdrawalApprovals_GroupMemberId",
                table: "WithdrawalApprovals",
                column: "GroupMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_WithdrawalApprovals_WithdrawalId_GroupMemberId",
                table: "WithdrawalApprovals",
                columns: new[] { "WithdrawalId", "GroupMemberId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WithdrawalApprovals");

            migrationBuilder.DropColumn(
                name: "CustomApprovalRoles",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "CustomRequiredApprovals",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "WithdrawalApprovalMode",
                table: "GroupSettings");
        }
    }
}
