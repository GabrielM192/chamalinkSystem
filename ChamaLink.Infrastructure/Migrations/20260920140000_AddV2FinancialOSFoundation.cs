using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChamaLink.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddV2FinancialOSFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // GroupPolicy — versioned business rules
            migrationBuilder.CreateTable(
                name: "GroupPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    MonthlyContribution = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    JoiningFee = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    LateFine = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    GracePeriodDays = table.Column<int>(type: "integer", nullable: false),
                    MaxConsecutiveMissedMonths = table.Column<int>(type: "integer", nullable: false),
                    AllocationStrategy = table.Column<string>(type: "text", nullable: false),
                    ShortfallStrategy = table.Column<string>(type: "text", nullable: false),
                    ComplianceStrategy = table.Column<string>(type: "text", nullable: false),
                    DebtAllocationStrategy = table.Column<string>(type: "text", nullable: false),
                    LoanEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LoanInterestRate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    LoanInterestType = table.Column<string>(type: "text", nullable: false),
                    LoanRepaymentDays = table.Column<int>(type: "integer", nullable: false),
                    LoanLatePenaltyAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    LoanMaxMultiplier = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    LoanStrategy = table.Column<string>(type: "text", nullable: false),
                    GuarantorRequired = table.Column<bool>(type: "boolean", nullable: false),
                    MinGuarantors = table.Column<int>(type: "integer", nullable: false),
                    LoanCollateralPercent = table.Column<decimal>(type: "numeric", nullable: false),
                    CollateralCanBeUsedForDefault = table.Column<bool>(type: "boolean", nullable: false),
                    WelfareEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    WelfareFineAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    WelfareMode = table.Column<string>(type: "text", nullable: false),
                    BeneficiaryExempted = table.Column<bool>(type: "boolean", nullable: false),
                    WelfareCollectionThreshold = table.Column<decimal>(type: "numeric", nullable: false),
                    WithdrawalApprovalMode = table.Column<string>(type: "text", nullable: false),
                    LoanApprovalMode = table.Column<string>(type: "text", nullable: false),
                    EventApprovalMode = table.Column<string>(type: "text", nullable: false),
                    ImportApprovalMode = table.Column<string>(type: "text", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupPolicies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GroupPolicies_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GroupPolicies_GroupId_Version",
                table: "GroupPolicies",
                columns: new[] { "GroupId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GroupPolicies_GroupId_EffectiveFrom",
                table: "GroupPolicies",
                columns: new[] { "GroupId", "EffectiveFrom" });

            // FinancialEvent — event sourcing foundation
            migrationBuilder.CreateTable(
                name: "FinancialEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    MemberId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    SourceReference = table.Column<string>(type: "text", nullable: true),
                    PolicyVersion = table.Column<int>(type: "integer", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    MetadataJson = table.Column<string>(type: "text", nullable: true),
                    AuditTrailId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinancialEvents_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FinancialEvents_GroupMembers_MemberId",
                        column: x => x.MemberId,
                        principalTable: "GroupMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialEvents_GroupId_MemberId",
                table: "FinancialEvents",
                columns: new[] { "GroupId", "MemberId" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialEvents_CorrelationId",
                table: "FinancialEvents",
                column: "CorrelationId");

            // AuditEvent — immutable audit trail
            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    MemberId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorRole = table.Column<string>(type: "text", nullable: false),
                    ActorMembershipNumber = table.Column<string>(type: "text", nullable: true),
                    Action = table.Column<string>(type: "text", nullable: false),
                    EntityType = table.Column<string>(type: "text", nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BeforeJson = table.Column<string>(type: "text", nullable: true),
                    AfterJson = table.Column<string>(type: "text", nullable: true),
                    ChangesJson = table.Column<string>(type: "text", nullable: true),
                    Source = table.Column<string>(type: "text", nullable: false),
                    SourceReference = table.Column<string>(type: "text", nullable: true),
                    SourceMetadataJson = table.Column<string>(type: "text", nullable: true),
                    PolicyVersion = table.Column<int>(type: "integer", nullable: true),
                    AllocationResultJson = table.Column<string>(type: "text", nullable: true),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    IpAddress = table.Column<string>(type: "text", nullable: true),
                    IsSystemGenerated = table.Column<bool>(type: "boolean", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_GroupId_MemberId",
                table: "AuditEvents",
                columns: new[] { "GroupId", "MemberId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_CorrelationId",
                table: "AuditEvents",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_Timestamp",
                table: "AuditEvents",
                column: "Timestamp");

            // WelfareEvent
            migrationBuilder.CreateTable(
                name: "WelfareEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    BeneficiaryMemberId = table.Column<Guid>(type: "uuid", nullable: true),
                    BeneficiaryName = table.Column<string>(type: "text", nullable: false),
                    RequiredContributionPerMember = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalExpected = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalCollected = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalDisbursed = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CollectionMethod = table.Column<string>(type: "text", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeadlineDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PolicyVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WelfareEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WelfareEvents_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WelfareEvents_GroupMembers_BeneficiaryMemberId",
                        column: x => x.BeneficiaryMemberId,
                        principalTable: "GroupMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WelfareEvents_GroupId",
                table: "WelfareEvents",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_WelfareEvents_BeneficiaryMemberId",
                table: "WelfareEvents",
                column: "BeneficiaryMemberId");

            // WelfareObligation
            migrationBuilder.CreateTable(
                name: "WelfareObligations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WelfareEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    MemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequiredAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PaidAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    WaivedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    WaivedReason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WelfareObligations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WelfareObligations_WelfareEvents_WelfareEventId",
                        column: x => x.WelfareEventId,
                        principalTable: "WelfareEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WelfareObligations_GroupMembers_MemberId",
                        column: x => x.MemberId,
                        principalTable: "GroupMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WelfareObligations_WelfareEventId_MemberId",
                table: "WelfareObligations",
                columns: new[] { "WelfareEventId", "MemberId" },
                unique: true);

            // WelfareContribution
            migrationBuilder.CreateTable(
                name: "WelfareContributions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WelfareObligationId = table.Column<Guid>(type: "uuid", nullable: false),
                    WelfareEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    MemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PaidBy = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    FinancialEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuditTrailId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WelfareContributions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WelfareContributions_FinancialEvents_FinancialEventId",
                        column: x => x.FinancialEventId,
                        principalTable: "FinancialEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WelfareContributions_WelfareObligations_WelfareObligationId",
                        column: x => x.WelfareObligationId,
                        principalTable: "WelfareObligations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // WelfareDisbursement
            migrationBuilder.CreateTable(
                name: "WelfareDisbursements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WelfareEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    BeneficiaryMemberId = table.Column<Guid>(type: "uuid", nullable: true),
                    BeneficiaryName = table.Column<string>(type: "text", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DisbursedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DisbursedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    Method = table.Column<string>(type: "text", nullable: false),
                    Reference = table.Column<string>(type: "text", nullable: true),
                    FinancialEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ApprovedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    AuditTrailId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WelfareDisbursements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WelfareDisbursements_FinancialEvents_FinancialEventId",
                        column: x => x.FinancialEventId,
                        principalTable: "FinancialEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WelfareDisbursements_WelfareEvents_WelfareEventId",
                        column: x => x.WelfareEventId,
                        principalTable: "WelfareEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // MemberStatusHistory — lifecycle audit
            migrationBuilder.CreateTable(
                name: "MemberStatusHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStatus = table.Column<string>(type: "text", nullable: false),
                    ToStatus = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    ChangedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MetadataJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemberStatusHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MemberStatusHistories_GroupMembers_MemberId",
                        column: x => x.MemberId,
                        principalTable: "GroupMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MemberStatusHistories_MemberId",
                table: "MemberStatusHistories",
                column: "MemberId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "MemberStatusHistories");
            migrationBuilder.DropTable(name: "WelfareContributions");
            migrationBuilder.DropTable(name: "WelfareDisbursements");
            migrationBuilder.DropTable(name: "WelfareObligations");
            migrationBuilder.DropTable(name: "WelfareEvents");
            migrationBuilder.DropTable(name: "AuditEvents");
            migrationBuilder.DropTable(name: "FinancialEvents");
            migrationBuilder.DropTable(name: "GroupPolicies");
        }
    }
}
