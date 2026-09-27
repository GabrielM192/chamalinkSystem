using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChamaLink.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupSettingsAndMemberStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Accounts_Groups_GroupId",
                table: "Accounts");

            migrationBuilder.DropIndex(
                name: "IX_Accounts_GroupId",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "GracePeriodDays",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "LoanMultiplier",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "MaxFinesAllowed",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "NewMemberMinContributions",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "NewMemberProbationMonths",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "GroupId",
                table: "Accounts");

            migrationBuilder.RenameColumn(
                name: "WaitingPeriodMonths",
                table: "GroupSettings",
                newName: "DueDateDay");

            migrationBuilder.RenameColumn(
                name: "MaxDebtAmount",
                table: "GroupSettings",
                newName: "MonthlyContributionAmount");

            migrationBuilder.AddColumn<Guid>(
                name: "Id",
                table: "GroupSettings",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<decimal>(
                name: "LatePenaltyAmount",
                table: "GroupSettings",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "Groups",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "AdvanceBalance",
                table: "GroupMembers",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Id",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "LatePenaltyAmount",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "AdvanceBalance",
                table: "GroupMembers");

            migrationBuilder.RenameColumn(
                name: "MonthlyContributionAmount",
                table: "GroupSettings",
                newName: "MaxDebtAmount");

            migrationBuilder.RenameColumn(
                name: "DueDateDay",
                table: "GroupSettings",
                newName: "WaitingPeriodMonths");

            migrationBuilder.AddColumn<int>(
                name: "GracePeriodDays",
                table: "GroupSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LoanMultiplier",
                table: "GroupSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxFinesAllowed",
                table: "GroupSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "NewMemberMinContributions",
                table: "GroupSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "NewMemberProbationMonths",
                table: "GroupSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "GroupId",
                table: "Accounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_GroupId",
                table: "Accounts",
                column: "GroupId");

            migrationBuilder.AddForeignKey(
                name: "FK_Accounts_Groups_GroupId",
                table: "Accounts",
                column: "GroupId",
                principalTable: "Groups",
                principalColumn: "Id");
        }
    }
}
