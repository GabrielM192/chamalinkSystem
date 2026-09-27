using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChamaLink.Infrastructure.Migrations;

/// <summary>
/// Makes the contribution due day part of the versioned GroupPolicy.
/// Existing groups retain the historical default of day 5 until their
/// policy is explicitly configured.
/// </summary>
public partial class UnifyPolicyDueDate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "DueDateDay",
            table: "GroupPolicies",
            type: "integer",
            nullable: false,
            defaultValue: 5);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "DueDateDay",
            table: "GroupPolicies");
    }
}