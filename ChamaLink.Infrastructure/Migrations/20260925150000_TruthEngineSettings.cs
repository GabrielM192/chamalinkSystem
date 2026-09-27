using System;
using ChamaLink.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChamaLink.Infrastructure.Migrations;

/// <summary>
/// Mipangilio ya ContributionTruthEngine kwenye GroupPolicy (versioned).
/// Mipangilio ya Ukonga inawekwa na viongozi (PUT /api/truth/{groupId}/settings); angalia docs/truth-engine/SCHEMA_YA_DB.md
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260925150000_TruthEngineSettings")]
public partial class TruthEngineSettings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(name: "ContributionStartDate", table: "GroupPolicies",
            type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "ManualEntryCutoff", table: "GroupPolicies",
            type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "FoundingMembersCutoff", table: "GroupPolicies",
            type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<bool>(name: "FineOnPartialPayment", table: "GroupPolicies",
            type: "boolean", nullable: false, defaultValue: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ContributionStartDate", table: "GroupPolicies");
        migrationBuilder.DropColumn(name: "ManualEntryCutoff", table: "GroupPolicies");
        migrationBuilder.DropColumn(name: "FoundingMembersCutoff", table: "GroupPolicies");
        migrationBuilder.DropColumn(name: "FineOnPartialPayment", table: "GroupPolicies");
    }
}
