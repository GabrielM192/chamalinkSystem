using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChamaLink.Infrastructure.Migrations
{
    /// <inheritdoc />
    // CORRECTED BY HAND (Phase A code review): the version EF scaffolded
    // used CLR default(T) as the SQL default (0 / false / "") for every
    // new column, because ApplicationDbContext never called
    // .HasDefaultValue(...) explicitly - C# property initializers on
    // GroupSettingsModules.cs are not picked up automatically by EF
    // migrations. Fixed here so existing rows land on the SAME defaults
    // documented in GroupSettingsModules.cs / GovernanceSettings, instead
    // of silently getting 0/false/"" which would (a) reset
    // WelfareFineAmount to 0 for every existing group, and (b) store ""
    // for enum columns (LoanInterestType, *ApprovalMode), which throws
    // when EF tries to parse "" back into the enum on read.
    public partial class AddGroupSettingsOwnedTypesPhaseA : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EventApprovalCustomRequiredApprovals",
                table: "GroupSettings",
                type: "integer",
                nullable: true,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "EventApprovalCustomRoles",
                table: "GroupSettings",
                type: "text",
                nullable: true,
                defaultValue: "Treasurer,Chairperson");

            migrationBuilder.AddColumn<string>(
                name: "EventApprovalMode",
                table: "GroupSettings",
                type: "text",
                nullable: false,
                defaultValue: "CustomApproval");

            migrationBuilder.AddColumn<bool>(
                name: "EventsEnabled",
                table: "GroupSettings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "ImportApprovalCustomRequiredApprovals",
                table: "GroupSettings",
                type: "integer",
                nullable: true,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "ImportApprovalCustomRoles",
                table: "GroupSettings",
                type: "text",
                nullable: true,
                defaultValue: "Treasurer,Chairperson");

            migrationBuilder.AddColumn<string>(
                name: "ImportApprovalMode",
                table: "GroupSettings",
                type: "text",
                nullable: false,
                defaultValue: "CustomApproval");

            migrationBuilder.AddColumn<int>(
                name: "LoanApprovalCustomRequiredApprovals",
                table: "GroupSettings",
                type: "integer",
                nullable: true,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "LoanApprovalCustomRoles",
                table: "GroupSettings",
                type: "text",
                nullable: true,
                defaultValue: "Treasurer,Chairperson");

            migrationBuilder.AddColumn<string>(
                name: "LoanApprovalMode",
                table: "GroupSettings",
                type: "text",
                nullable: false,
                defaultValue: "CustomApproval");

            migrationBuilder.AddColumn<bool>(
                name: "LoanEnabled",
                table: "GroupSettings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "LoanInterestType",
                table: "GroupSettings",
                type: "text",
                nullable: false,
                defaultValue: "Flat");

            migrationBuilder.AddColumn<decimal>(
                name: "LoanLatePenaltyAmount",
                table: "GroupSettings",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "LoanMaxMultiplier",
                table: "GroupSettings",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LoanRepaymentDays",
                table: "GroupSettings",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<decimal>(
                name: "WelfareFineAmount",
                table: "GroupSettings",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 5000m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EventApprovalCustomRequiredApprovals",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "EventApprovalCustomRoles",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "EventApprovalMode",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "EventsEnabled",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "ImportApprovalCustomRequiredApprovals",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "ImportApprovalCustomRoles",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "ImportApprovalMode",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "LoanApprovalCustomRequiredApprovals",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "LoanApprovalCustomRoles",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "LoanApprovalMode",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "LoanEnabled",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "LoanInterestType",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "LoanLatePenaltyAmount",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "LoanMaxMultiplier",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "LoanRepaymentDays",
                table: "GroupSettings");

            migrationBuilder.DropColumn(
                name: "WelfareFineAmount",
                table: "GroupSettings");
        }
    }
}
