using System;
using ChamaLink.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChamaLink.Infrastructure.Migrations;

/// <summary>
/// Injini ya Ukweli v2 — meza za DATA GHAFI:
///   MkobaRawTransactions (mistari ya PDF), MemberPhones (namba ↔ mwanachama),
///   MkobaStatementGaps (mistari iliyokosekana kwenye PDF), TruthAdjustments (msamaha/neema).
/// Imeandikwa kwa mkono (snapshot ya EF haiko sawa na DB). Endesha: dotnet ef database update
/// au tumia docs/truth-engine/migration_v2.sql.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260925170000_TruthEngineRawData")]
public partial class TruthEngineRawData : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.CreateTable(
            name: "TruthGroupSettings",
            columns: t => new
            {
                GroupId = t.Column<Guid>(type: "uuid", nullable: false),
                MonthlyContribution = t.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                LateFine = t.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                JoiningFee = t.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                DueDateDay = t.Column<int>(type: "integer", nullable: false),
                GracePeriodDays = t.Column<int>(type: "integer", nullable: false),
                FineOnPartialPayment = t.Column<bool>(type: "boolean", nullable: false),
                ContributionStartDate = t.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                FoundingMembersCutoff = t.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ManualEntryCutoff = t.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                LoanLatePenaltyAmount = t.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                UpdatedAt = t.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedByUserId = t.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: t => t.PrimaryKey("PK_TruthGroupSettings", x => x.GroupId));

        mb.CreateTable(
            name: "MkobaRawTransactions",
            columns: t => new
            {
                Id = t.Column<Guid>(type: "uuid", nullable: false),
                GroupId = t.Column<Guid>(type: "uuid", nullable: false),
                Reference = t.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                Timestamp = t.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Phone = t.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                PayerName = t.Column<string>(type: "text", nullable: false),
                Amount = t.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                Balance = t.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                IsWithdrawal = t.Column<bool>(type: "boolean", nullable: false),
                Description = t.Column<string>(type: "text", nullable: false),
                RowOrder = t.Column<int>(type: "integer", nullable: false),
                StatementFileHash = t.Column<string>(type: "text", nullable: false),
                ImportedAt = t.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ImportedByUserId = t.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: t => t.PrimaryKey("PK_MkobaRawTransactions", x => x.Id));
        mb.CreateIndex("IX_MkobaRawTransactions_GroupId_Reference", "MkobaRawTransactions", new[] { "GroupId", "Reference" }, unique: true);
        mb.CreateIndex("IX_MkobaRawTransactions_GroupId_Phone", "MkobaRawTransactions", new[] { "GroupId", "Phone" });

        mb.CreateTable(
            name: "MemberPhones",
            columns: t => new
            {
                Id = t.Column<Guid>(type: "uuid", nullable: false),
                GroupId = t.Column<Guid>(type: "uuid", nullable: false),
                GroupMemberId = t.Column<Guid>(type: "uuid", nullable: false),
                Phone = t.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                MkobaNameSeen = t.Column<string>(type: "text", nullable: true),
                MatchedInMkoba = t.Column<bool>(type: "boolean", nullable: false),
                AddedByUserId = t.Column<Guid>(type: "uuid", nullable: false),
                AddedAt = t.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Note = t.Column<string>(type: "text", nullable: true)
            },
            constraints: t => t.PrimaryKey("PK_MemberPhones", x => x.Id));
        mb.CreateIndex("IX_MemberPhones_GroupId_Phone", "MemberPhones", new[] { "GroupId", "Phone" }, unique: true);
        mb.CreateIndex("IX_MemberPhones_GroupMemberId", "MemberPhones", "GroupMemberId");

        mb.CreateTable(
            name: "MkobaStatementGaps",
            columns: t => new
            {
                Id = t.Column<Guid>(type: "uuid", nullable: false),
                GroupId = t.Column<Guid>(type: "uuid", nullable: false),
                AfterReference = t.Column<string>(type: "text", nullable: true),
                BeforeReference = t.Column<string>(type: "text", nullable: false),
                WindowStart = t.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                WindowEnd = t.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Amount = t.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                DetectedAt = t.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                AssignedGroupMemberId = t.Column<Guid>(type: "uuid", nullable: true),
                Evidence = t.Column<string>(type: "text", nullable: true),
                AssumedTimestamp = t.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ResolutionStatus = t.Column<string>(type: "text", nullable: true),
                RequestedByUserId = t.Column<Guid>(type: "uuid", nullable: true),
                RequestedAt = t.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ApprovedByUserId = t.Column<Guid>(type: "uuid", nullable: true),
                ApprovedAt = t.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: t => t.PrimaryKey("PK_MkobaStatementGaps", x => x.Id));
        mb.CreateIndex("IX_MkobaStatementGaps_GroupId_BeforeReference", "MkobaStatementGaps", new[] { "GroupId", "BeforeReference" }, unique: true);

        mb.CreateTable(
            name: "TruthAdjustments",
            columns: t => new
            {
                Id = t.Column<Guid>(type: "uuid", nullable: false),
                GroupId = t.Column<Guid>(type: "uuid", nullable: false),
                GroupMemberId = t.Column<Guid>(type: "uuid", nullable: true),
                Kind = t.Column<string>(type: "text", nullable: false),
                Year = t.Column<int>(type: "integer", nullable: false),
                Month = t.Column<int>(type: "integer", nullable: false),
                ExtraDays = t.Column<int>(type: "integer", nullable: false),
                Reason = t.Column<string>(type: "text", nullable: false),
                Status = t.Column<string>(type: "text", nullable: false),
                RequestedByUserId = t.Column<Guid>(type: "uuid", nullable: false),
                RequestedAt = t.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                DecidedByUserId = t.Column<Guid>(type: "uuid", nullable: true),
                DecidedAt = t.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: t => t.PrimaryKey("PK_TruthAdjustments", x => x.Id));
        mb.CreateIndex("IX_TruthAdjustments_GroupId_Year_Month", "TruthAdjustments", new[] { "GroupId", "Year", "Month" });
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropTable("TruthAdjustments");
        mb.DropTable("MkobaStatementGaps");
        mb.DropTable("MemberPhones");
        mb.DropTable("MkobaRawTransactions");
        mb.DropTable("TruthGroupSettings");
    }
}
