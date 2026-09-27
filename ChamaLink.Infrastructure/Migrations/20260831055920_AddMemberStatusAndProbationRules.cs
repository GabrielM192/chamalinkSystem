using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChamaLink.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberStatusAndProbationRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GracePeriodDays",
                table: "GroupSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxDebtAmount",
                table: "GroupSettings",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

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

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "GroupMembers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalContributionsCount",
                table: "GroupMembers",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GracePeriodDays",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "MaxDebtAmount",
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
                name: "Status",
                table: "GroupMembers");

            migrationBuilder.DropColumn(
                name: "TotalContributionsCount",
                table: "GroupMembers");
        }
    }
}
