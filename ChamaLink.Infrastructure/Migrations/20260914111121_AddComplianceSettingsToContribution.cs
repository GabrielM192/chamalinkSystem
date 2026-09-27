using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChamaLink.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddComplianceSettingsToContribution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DebtAllocationStrategy",
                table: "GroupSettings",
                type: "text",
                nullable: false,
                defaultValue: "CurrentMonthFirst");

            migrationBuilder.AddColumn<int>(
                name: "MaxConsecutiveMissedMonths",
                table: "GroupSettings",
                type: "integer",
                nullable: false,
                defaultValue: 3);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DebtAllocationStrategy",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "MaxConsecutiveMissedMonths",
                table: "GroupSettings");
        }
    }
}
