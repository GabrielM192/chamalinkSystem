using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChamaLink.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationTypeAndProductConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OrganizationType",
                table: "Groups",
                type: "text",
                nullable: false,
                defaultValue: "Mkoba");

            migrationBuilder.AddColumn<string>(
                name: "ShortfallStrategy",
                table: "GroupSettings",
                type: "text",
                nullable: false,
                defaultValue: "CreateDebt");

            migrationBuilder.AddColumn<string>(
                name: "ComplianceStrategy",
                table: "GroupSettings",
                type: "text",
                nullable: false,
                defaultValue: "MonthlyTarget");

            migrationBuilder.AddColumn<string>(
                name: "LoanStrategy",
                table: "GroupSettings",
                type: "text",
                nullable: false,
                defaultValue: "DirectIssue");

            migrationBuilder.AddColumn<bool>(
                name: "GuarantorRequired",
                table: "GroupSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MinGuarantors",
                table: "GroupSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OrganizationType",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "ShortfallStrategy",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "ComplianceStrategy",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "LoanStrategy",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "GuarantorRequired",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "MinGuarantors",
                table: "GroupSettings");
        }
    }
}
