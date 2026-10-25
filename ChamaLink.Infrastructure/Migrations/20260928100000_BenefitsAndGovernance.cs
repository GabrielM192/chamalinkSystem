using ChamaLink.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChamaLink.Infrastructure.Migrations;

/// <summary>
/// Ushauri 2026-09-28 (Mafao na Ulinzi):
///  - Mafao: BenefitGovernances, BenefitRates, BenefitCases, BenefitEvidence, GovernanceVotes.
///  - Hali ya mwanachama kwa kura: MemberStatusChangeRequests. Maelezo ya utoaji wa M-Koba: MkobaWithdrawalExplanations.
///  - Arifa: MemberNotifications. Maker-checker ya kanuni za fedha: TruthSettingsProposals.
///  - MemberPhones: idhini ya watu wawili + kipindi (ValidFrom/ValidTo). Namba zilizopo zinabaki "Approved"
///    (zilishatumika kwenye ripoti); namba moja sasa inaweza kuwa ya watu wawili kwa VIPINDI TOFAUTI (index si unique tena).
///  - TruthGroupSettings.LateFinePercent (faini kwa asilimia ya mchango, Katiba 13).
///  - Index ya ukuaji: MkobaRawTransactions (GroupId, Timestamp).
/// DDL imezalishwa kutoka kwenye model (GenerateCreateScript) ili ilingane nayo kabisa.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260928100000_BenefitsAndGovernance")]
public partial class BenefitsAndGovernance : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        // ---- MemberPhones: idhini + kipindi ----
        mb.Sql("""
            ALTER TABLE "MemberPhones" ADD COLUMN "Status" character varying(20) NOT NULL DEFAULT 'Pending';
            ALTER TABLE "MemberPhones" ADD COLUMN "ValidFrom" timestamp with time zone NULL;
            ALTER TABLE "MemberPhones" ADD COLUMN "ValidTo" timestamp with time zone NULL;
            ALTER TABLE "MemberPhones" ADD COLUMN "ApprovedByUserId" uuid NULL;
            ALTER TABLE "MemberPhones" ADD COLUMN "ApprovedAt" timestamp with time zone NULL;
            ALTER TABLE "MemberPhones" ADD COLUMN "PendingValidTo" timestamp with time zone NULL;
            ALTER TABLE "MemberPhones" ADD COLUMN "PendingEndReason" character varying(500) NULL;
            ALTER TABLE "MemberPhones" ADD COLUMN "EndRequestedByUserId" uuid NULL;
            ALTER TABLE "MemberPhones" ADD COLUMN "EndRequestedAt" timestamp with time zone NULL;
            -- Namba zilizopo tayari zinatumika kwenye ripoti: zinabaki zikihesabiwa (zimeidhinishwa kihistoria)
            UPDATE "MemberPhones" SET "Status" = 'Approved', "ApprovedAt" = "AddedAt";
            ALTER TABLE "MemberPhones" ALTER COLUMN "Status" DROP DEFAULT;
            DROP INDEX IF EXISTS "IX_MemberPhones_GroupId_Phone";
            CREATE INDEX "IX_MemberPhones_GroupId_Phone" ON "MemberPhones" ("GroupId", "Phone");
            """);

        mb.Sql("""ALTER TABLE "TruthGroupSettings" ADD COLUMN "LateFinePercent" numeric(5,2) NULL;""");

        mb.Sql("""
CREATE TABLE "BenefitCases" (
    "Id" uuid NOT NULL,
    "GroupId" uuid NOT NULL,
    "CaseNumber" character varying(40) NOT NULL,
    "Category" character varying(20) NOT NULL,
    "Relationship" character varying(20) NOT NULL,
    "BeneficiaryGroupMemberId" uuid NOT NULL,
    "SubjectName" character varying(200) NOT NULL,
    "EventDate" timestamp with time zone NOT NULL,
    "Description" character varying(1000),
    "Status" character varying(40) NOT NULL,
    "IsHistorical" boolean NOT NULL,
    "BenefitRateId" uuid,
    "GovernanceId" uuid,
    "PerMemberAmount" numeric(18,2) NOT NULL,
    "Headcount" integer NOT NULL,
    "GrossAmount" numeric(18,2) NOT NULL,
    "JoinFeeDeducted" numeric(18,2) NOT NULL,
    "ArrearsDeducted" numeric(18,2) NOT NULL,
    "NetAmount" numeric(18,2) NOT NULL,
    "HeadcountRuleUsed" character varying(200),
    "CountedMemberIdsJson" text,
    "EligibilityJson" text,
    "SnapshotAt" timestamp with time zone,
    "VotingDeadline" timestamp with time zone,
    "ApprovedAt" timestamp with time zone,
    "MkobaReference" character varying(40),
    "PaidAt" timestamp with time zone,
    "PaidConfirmedByUserId" uuid,
    "PaidConfirmedAt" timestamp with time zone,
    "CreatedByUserId" uuid NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "ClosedReason" character varying(500),
    "ClosedByUserId" uuid,
    "ClosedAt" timestamp with time zone,
    CONSTRAINT "PK_BenefitCases" PRIMARY KEY ("Id")
);

CREATE TABLE "BenefitGovernances" (
    "Id" uuid NOT NULL,
    "GroupId" uuid NOT NULL,
    "Status" character varying(20) NOT NULL,
    "RequiredApprovals" integer NOT NULL,
    "MinLeaderApprovals" integer NOT NULL,
    "AllowMemberApprovers" boolean NOT NULL,
    "RejectionsToReject" integer NOT NULL,
    "VotingDays" integer NOT NULL,
    "HeadcountRule" character varying(40) NOT NULL,
    "HeadcountPaidWithinDays" integer,
    "ExcludeBeneficiaryFromHeadcount" boolean NOT NULL,
    "FundingSource" character varying(30) NOT NULL,
    "DeductJoinFee" boolean NOT NULL,
    "DeductArrears" boolean NOT NULL,
    "RequireNoArrearsForEligibility" boolean NOT NULL,
    "MinMembershipDays" integer NOT NULL,
    "ClaimWindowDays" integer NOT NULL,
    "PaymentConfirmationDays" integer NOT NULL,
    "StatusChangeApprovals" integer NOT NULL,
    "ExpulsionVotePercent" integer NOT NULL,
    "RequestedByUserId" uuid NOT NULL,
    "RequestedAt" timestamp with time zone NOT NULL,
    "DecidedByUserId" uuid,
    "DecidedAt" timestamp with time zone,
    "Note" character varying(500),
    CONSTRAINT "PK_BenefitGovernances" PRIMARY KEY ("Id")
);

CREATE TABLE "BenefitRates" (
    "Id" uuid NOT NULL,
    "GroupId" uuid NOT NULL,
    "Category" character varying(20) NOT NULL,
    "Relationship" character varying(20) NOT NULL,
    "PerMemberAmount" numeric(18,2) NOT NULL,
    "AcceptedEvidence" character varying(300) NOT NULL,
    "MaxClaimsPerMember" integer,
    "IsDisabled" boolean NOT NULL,
    "EffectiveFrom" timestamp with time zone NOT NULL,
    "Status" character varying(20) NOT NULL,
    "RequestedByUserId" uuid NOT NULL,
    "RequestedAt" timestamp with time zone NOT NULL,
    "DecidedByUserId" uuid,
    "DecidedAt" timestamp with time zone,
    "Note" character varying(500),
    CONSTRAINT "PK_BenefitRates" PRIMARY KEY ("Id")
);

CREATE TABLE "GovernanceVotes" (
    "Id" uuid NOT NULL,
    "GroupId" uuid NOT NULL,
    "SubjectType" character varying(40) NOT NULL,
    "SubjectId" uuid NOT NULL,
    "GroupMemberId" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "Approve" boolean NOT NULL,
    "Reason" character varying(500),
    "RoleAtVote" character varying(30) NOT NULL,
    "At" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_GovernanceVotes" PRIMARY KEY ("Id")
);

CREATE TABLE "MemberNotifications" (
    "Id" uuid NOT NULL,
    "GroupId" uuid NOT NULL,
    "RecipientGroupMemberId" uuid NOT NULL,
    "Kind" character varying(40) NOT NULL,
    "Title" character varying(200) NOT NULL,
    "Body" character varying(1000) NOT NULL,
    "Link" character varying(200),
    "CreatedAt" timestamp with time zone NOT NULL,
    "ReadAt" timestamp with time zone,
    CONSTRAINT "PK_MemberNotifications" PRIMARY KEY ("Id")
);

CREATE TABLE "MemberStatusChangeRequests" (
    "Id" uuid NOT NULL,
    "GroupId" uuid NOT NULL,
    "GroupMemberId" uuid NOT NULL,
    "FromStatus" character varying(30) NOT NULL,
    "ToStatus" character varying(30) NOT NULL,
    "IsExpulsion" boolean NOT NULL,
    "EffectiveDate" timestamp with time zone NOT NULL,
    "Reason" character varying(500) NOT NULL,
    "EvidenceNote" character varying(500),
    "RequiredApprovals" integer NOT NULL,
    "LeadersOnly" boolean NOT NULL,
    "RejectionsToReject" integer NOT NULL,
    "Status" character varying(20) NOT NULL,
    "GovernanceId" uuid,
    "RequestedByUserId" uuid NOT NULL,
    "RequestedAt" timestamp with time zone NOT NULL,
    "DecidedAt" timestamp with time zone,
    CONSTRAINT "PK_MemberStatusChangeRequests" PRIMARY KEY ("Id")
);

CREATE TABLE "MkobaWithdrawalExplanations" (
    "Id" uuid NOT NULL,
    "GroupId" uuid NOT NULL,
    "MkobaReference" character varying(40) NOT NULL,
    "Kind" character varying(30) NOT NULL,
    "Description" character varying(500) NOT NULL,
    "Status" character varying(20) NOT NULL,
    "RequestedByUserId" uuid NOT NULL,
    "RequestedAt" timestamp with time zone NOT NULL,
    "DecidedByUserId" uuid,
    "DecidedAt" timestamp with time zone,
    "DecisionNote" character varying(500),
    CONSTRAINT "PK_MkobaWithdrawalExplanations" PRIMARY KEY ("Id")
);

CREATE TABLE "TruthSettingsProposals" (
    "Id" uuid NOT NULL,
    "GroupId" uuid NOT NULL,
    "PayloadJson" character varying(4000) NOT NULL,
    "Status" character varying(20) NOT NULL,
    "RequestedByUserId" uuid NOT NULL,
    "RequestedAt" timestamp with time zone NOT NULL,
    "DecidedByUserId" uuid,
    "DecidedAt" timestamp with time zone,
    "Note" character varying(500),
    "DecisionNote" character varying(500),
    CONSTRAINT "PK_TruthSettingsProposals" PRIMARY KEY ("Id")
);

CREATE TABLE "BenefitEvidence" (
    "Id" uuid NOT NULL,
    "GroupId" uuid NOT NULL,
    "BenefitCaseId" uuid NOT NULL,
    "DocumentType" character varying(40) NOT NULL,
    "DocumentNumber" character varying(100) NOT NULL,
    "FileName" character varying(255) NOT NULL,
    "ContentType" character varying(100) NOT NULL,
    "SizeBytes" bigint NOT NULL,
    "Sha256" character varying(64) NOT NULL,
    "StorageKey" character varying(300) NOT NULL,
    "UploadedByUserId" uuid NOT NULL,
    "UploadedAt" timestamp with time zone NOT NULL,
    "Status" character varying(20) NOT NULL,
    "VerifiedByUserId" uuid,
    "VerifiedAt" timestamp with time zone,
    "ReviewNote" character varying(500),
    CONSTRAINT "PK_BenefitEvidence" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_BenefitEvidence_BenefitCases_BenefitCaseId" FOREIGN KEY ("BenefitCaseId") REFERENCES "BenefitCases" ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_BenefitCases_BeneficiaryGroupMemberId" ON "BenefitCases" ("BeneficiaryGroupMemberId");

CREATE UNIQUE INDEX "IX_BenefitCases_GroupId_CaseNumber" ON "BenefitCases" ("GroupId", "CaseNumber");

CREATE UNIQUE INDEX "IX_BenefitCases_GroupId_MkobaReference" ON "BenefitCases" ("GroupId", "MkobaReference") WHERE "MkobaReference" IS NOT NULL;

CREATE INDEX "IX_BenefitCases_GroupId_Status" ON "BenefitCases" ("GroupId", "Status");

CREATE INDEX "IX_BenefitEvidence_BenefitCaseId" ON "BenefitEvidence" ("BenefitCaseId");

CREATE INDEX "IX_BenefitEvidence_GroupId_DocumentType_DocumentNumber" ON "BenefitEvidence" ("GroupId", "DocumentType", "DocumentNumber");

CREATE INDEX "IX_BenefitEvidence_GroupId_Sha256" ON "BenefitEvidence" ("GroupId", "Sha256");

CREATE INDEX "IX_BenefitGovernances_GroupId_Status" ON "BenefitGovernances" ("GroupId", "Status");

CREATE INDEX "IX_BenefitRates_GroupId_Category_Relationship_EffectiveFrom" ON "BenefitRates" ("GroupId", "Category", "Relationship", "EffectiveFrom");

CREATE INDEX "IX_GovernanceVotes_GroupId" ON "GovernanceVotes" ("GroupId");

CREATE UNIQUE INDEX "IX_GovernanceVotes_SubjectType_SubjectId_GroupMemberId" ON "GovernanceVotes" ("SubjectType", "SubjectId", "GroupMemberId");

CREATE INDEX "IX_MemberNotifications_RecipientGroupMemberId_ReadAt" ON "MemberNotifications" ("RecipientGroupMemberId", "ReadAt");

CREATE INDEX "IX_MemberStatusChangeRequests_GroupId_Status" ON "MemberStatusChangeRequests" ("GroupId", "Status");

CREATE INDEX "IX_MemberStatusChangeRequests_GroupMemberId" ON "MemberStatusChangeRequests" ("GroupMemberId");

CREATE INDEX IF NOT EXISTS "IX_MkobaRawTransactions_GroupId_Timestamp" ON "MkobaRawTransactions" ("GroupId", "Timestamp");

CREATE INDEX "IX_MkobaWithdrawalExplanations_GroupId_MkobaReference" ON "MkobaWithdrawalExplanations" ("GroupId", "MkobaReference");

CREATE INDEX "IX_TruthSettingsProposals_GroupId_Status" ON "TruthSettingsProposals" ("GroupId", "Status");
""");
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.Sql("""
            DROP TABLE IF EXISTS "BenefitEvidence";
            DROP TABLE IF EXISTS "BenefitCases";
            DROP TABLE IF EXISTS "BenefitRates";
            DROP TABLE IF EXISTS "BenefitGovernances";
            DROP TABLE IF EXISTS "GovernanceVotes";
            DROP TABLE IF EXISTS "MemberStatusChangeRequests";
            DROP TABLE IF EXISTS "MkobaWithdrawalExplanations";
            DROP TABLE IF EXISTS "MemberNotifications";
            DROP TABLE IF EXISTS "TruthSettingsProposals";
            DROP INDEX IF EXISTS "IX_MkobaRawTransactions_GroupId_Timestamp";
            ALTER TABLE "TruthGroupSettings" DROP COLUMN IF EXISTS "LateFinePercent";
            DROP INDEX IF EXISTS "IX_MemberPhones_GroupId_Phone";
            ALTER TABLE "MemberPhones" DROP COLUMN "Status", DROP COLUMN "ValidFrom", DROP COLUMN "ValidTo", DROP COLUMN "ApprovedByUserId",
                DROP COLUMN "ApprovedAt", DROP COLUMN "PendingValidTo", DROP COLUMN "PendingEndReason", DROP COLUMN "EndRequestedByUserId",
                DROP COLUMN "EndRequestedAt";
            CREATE UNIQUE INDEX "IX_MemberPhones_GroupId_Phone" ON "MemberPhones" ("GroupId", "Phone");
            """);
    }
}
