using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChamaLink.Infrastructure.Migrations
{
    /// <inheritdoc />
    // NEW (welfare mode configurability, raised directly by a group admin):
    // groups can now choose whether an event's WelfareDeduction subtracts
    // from a member's own welfare balance (DeductBalance, the default -
    // matches the existing MinimumReserveBalance field's original intent)
    // or adds to it as a mandatory contribution (ContributePot). See
    // WelfareMode in Enums.cs and WelfarePenaltyBackgroundService.cs.
    public partial class AddWelfareModeToGroupSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WelfareMode",
                table: "GroupSettings",
                type: "text",
                nullable: false,
                defaultValue: "DeductBalance");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WelfareMode",
                table: "GroupSettings");
        }
    }
}
