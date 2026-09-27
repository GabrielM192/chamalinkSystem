using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChamaLink.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LinkWithdrawalToBeneficiaryAndEvent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BeneficiaryGroupMemberId",
                table: "Withdrawals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GroupEventId",
                table: "Withdrawals",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Withdrawals_BeneficiaryGroupMemberId",
                table: "Withdrawals",
                column: "BeneficiaryGroupMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_Withdrawals_GroupEventId",
                table: "Withdrawals",
                column: "GroupEventId");

            migrationBuilder.AddForeignKey(
                name: "FK_Withdrawals_GroupEvents_GroupEventId",
                table: "Withdrawals",
                column: "GroupEventId",
                principalTable: "GroupEvents",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Withdrawals_GroupMembers_BeneficiaryGroupMemberId",
                table: "Withdrawals",
                column: "BeneficiaryGroupMemberId",
                principalTable: "GroupMembers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Withdrawals_GroupEvents_GroupEventId",
                table: "Withdrawals");

            migrationBuilder.DropForeignKey(
                name: "FK_Withdrawals_GroupMembers_BeneficiaryGroupMemberId",
                table: "Withdrawals");

            migrationBuilder.DropIndex(
                name: "IX_Withdrawals_BeneficiaryGroupMemberId",
                table: "Withdrawals");

            migrationBuilder.DropIndex(
                name: "IX_Withdrawals_GroupEventId",
                table: "Withdrawals");

            migrationBuilder.DropColumn(
                name: "BeneficiaryGroupMemberId",
                table: "Withdrawals");

            migrationBuilder.DropColumn(
                name: "GroupEventId",
                table: "Withdrawals");
        }
    }
}
