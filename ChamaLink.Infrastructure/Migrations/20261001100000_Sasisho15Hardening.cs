using ChamaLink.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChamaLink.Infrastructure.Migrations;

/// <summary>
/// SASISHO15 — ugumu wa mfumo mzima (ripoti ya majaribio + ukaguzi wa nje):
///  - AWAMU L (mikopo): jedwali LoanApplications (ombi → kura → utoaji → uthibitisho wa M-Koba) na sera ya mikopo
///    ndani ya TruthGroupSettings (inabadilishwa kwa pendekezo + kiongozi wa pili). Mikopo imefungwa mpaka sera iwekwe.
///  - Loans: kiungo na ombi, rejea ya utoaji wa M-Koba (ya kipekee), na "umeshindikana" kwa watu wawili.
///  - MemberRelations: kuongeza uhusiano kunahitaji kiongozi wa pili (Pending → Active). Uhusiano uliopo unabaki Active.
///  - Users: kufungiwa kwa majaribio mabaya kwenye DB (si RAM) + TokenVersion (kufuta vikao vyote).
///  - RefreshTokens: vikao vya HttpOnly cookie (hash tu, rotation, kugundua kuibiwa).
///  - BenefitCases/LoanApplications: kinga ya ushindani kupitia safu ya mfumo xmin (hakuna DDL).
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261001100000_Sasisho15Hardening")]
public partial class Sasisho15Hardening : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.Sql("""
            CREATE TABLE IF NOT EXISTS "LoanApplications" (
                "Id" uuid NOT NULL,
                "GroupId" uuid NOT NULL,
                "ApplicationNumber" character varying(40) NOT NULL,
                "BorrowerGroupMemberId" uuid NOT NULL,
                "RequestedByUserId" uuid NOT NULL,
                "Amount" numeric(18,2) NOT NULL,
                "Purpose" character varying(500),
                "InterestPercent" numeric(9,4) NOT NULL,
                "InterestAmount" numeric(18,2) NOT NULL,
                "RepaymentMonths" integer NOT NULL,
                "RequiredApprovals" integer NOT NULL,
                "EligibilityJson" text,
                "Status" character varying(30) NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "ApprovedAt" timestamp with time zone,
                "ApprovalExpiresAt" timestamp with time zone,
                "DisbursementInitiatedByUserId" uuid,
                "DisbursementInitiatedAt" timestamp with time zone,
                "MkobaReference" character varying(60),
                "DisbursedAt" timestamp with time zone,
                "DisbursementConfirmedByUserId" uuid,
                "DisbursementConfirmedAt" timestamp with time zone,
                "RecipientNote" character varying(300),
                "LoanId" uuid,
                "ClosedByUserId" uuid,
                "ClosedAt" timestamp with time zone,
                "CloseReason" character varying(500),
                CONSTRAINT "PK_LoanApplications" PRIMARY KEY ("Id")
            );
            CREATE INDEX IF NOT EXISTS "IX_LoanApplications_BorrowerGroupMemberId" ON "LoanApplications" ("BorrowerGroupMemberId");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_LoanApplications_GroupId_ApplicationNumber" ON "LoanApplications" ("GroupId", "ApplicationNumber");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_LoanApplications_GroupId_MkobaReference" ON "LoanApplications" ("GroupId", "MkobaReference") WHERE "MkobaReference" IS NOT NULL;
            CREATE INDEX IF NOT EXISTS "IX_LoanApplications_GroupId_Status" ON "LoanApplications" ("GroupId", "Status");

            CREATE TABLE IF NOT EXISTS "RefreshTokens" (
                "Id" uuid NOT NULL,
                "UserId" uuid NOT NULL,
                "TokenHash" character varying(100) NOT NULL,
                "FamilyId" uuid NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "ExpiresAt" timestamp with time zone NOT NULL,
                "RevokedAt" timestamp with time zone,
                "ReplacedById" uuid,
                "TokenVersion" integer NOT NULL,
                "CreatedByIp" character varying(64),
                CONSTRAINT "PK_RefreshTokens" PRIMARY KEY ("Id")
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_RefreshTokens_TokenHash" ON "RefreshTokens" ("TokenHash");
            CREATE INDEX IF NOT EXISTS "IX_RefreshTokens_UserId_FamilyId" ON "RefreshTokens" ("UserId", "FamilyId");

            ALTER TABLE "TruthGroupSettings"
                ADD COLUMN IF NOT EXISTS "LoansEnabled" boolean NOT NULL DEFAULT FALSE,
                ADD COLUMN IF NOT EXISTS "LoanInterestPercent" numeric(9,4),
                ADD COLUMN IF NOT EXISTS "LoanRepaymentMonths" integer,
                ADD COLUMN IF NOT EXISTS "LoanMaxAmount" numeric(18,2),
                ADD COLUMN IF NOT EXISTS "LoanMaxMultipleOfContributions" numeric(9,4),
                ADD COLUMN IF NOT EXISTS "LoanMinMembershipMonths" integer,
                ADD COLUMN IF NOT EXISTS "LoanAllowWithArrears" boolean NOT NULL DEFAULT FALSE,
                ADD COLUMN IF NOT EXISTS "LoanAllowMultipleActive" boolean NOT NULL DEFAULT FALSE,
                ADD COLUMN IF NOT EXISTS "LoanMinCashReserve" numeric(18,2) NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS "LoanRequiredApprovals" integer,
                ADD COLUMN IF NOT EXISTS "LoanApprovalValidDays" integer;

            ALTER TABLE "Loans"
                ADD COLUMN IF NOT EXISTS "LoanApplicationId" uuid,
                ADD COLUMN IF NOT EXISTS "DisbursementReference" character varying(60),
                ADD COLUMN IF NOT EXISTS "DefaultRequestedByUserId" uuid,
                ADD COLUMN IF NOT EXISTS "DefaultRequestedAt" timestamp with time zone,
                ADD COLUMN IF NOT EXISTS "DefaultReason" character varying(500),
                ADD COLUMN IF NOT EXISTS "DefaultDecidedByUserId" uuid,
                ADD COLUMN IF NOT EXISTS "DefaultDecidedAt" timestamp with time zone;
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_Loans_GroupId_DisbursementReference" ON "Loans" ("GroupId", "DisbursementReference") WHERE "DisbursementReference" IS NOT NULL;

            ALTER TABLE "MemberRelations"
                ADD COLUMN IF NOT EXISTS "ApprovedByUserId" uuid,
                ADD COLUMN IF NOT EXISTS "ApprovedAt" timestamp with time zone,
                ADD COLUMN IF NOT EXISTS "DecisionNote" character varying(300);

            ALTER TABLE "Users"
                ADD COLUMN IF NOT EXISTS "FailedLoginCount" integer NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS "LockoutEndUtc" timestamp with time zone,
                ADD COLUMN IF NOT EXISTS "TokenVersion" integer NOT NULL DEFAULT 0;
            """);
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.Sql("""
            ALTER TABLE "Users" DROP COLUMN IF EXISTS "FailedLoginCount", DROP COLUMN IF EXISTS "LockoutEndUtc", DROP COLUMN IF EXISTS "TokenVersion";
            ALTER TABLE "MemberRelations" DROP COLUMN IF EXISTS "ApprovedByUserId", DROP COLUMN IF EXISTS "ApprovedAt", DROP COLUMN IF EXISTS "DecisionNote";
            DROP INDEX IF EXISTS "IX_Loans_GroupId_DisbursementReference";
            ALTER TABLE "Loans" DROP COLUMN IF EXISTS "LoanApplicationId", DROP COLUMN IF EXISTS "DisbursementReference",
                DROP COLUMN IF EXISTS "DefaultRequestedByUserId", DROP COLUMN IF EXISTS "DefaultRequestedAt", DROP COLUMN IF EXISTS "DefaultReason",
                DROP COLUMN IF EXISTS "DefaultDecidedByUserId", DROP COLUMN IF EXISTS "DefaultDecidedAt";
            ALTER TABLE "TruthGroupSettings" DROP COLUMN IF EXISTS "LoansEnabled", DROP COLUMN IF EXISTS "LoanInterestPercent",
                DROP COLUMN IF EXISTS "LoanRepaymentMonths", DROP COLUMN IF EXISTS "LoanMaxAmount", DROP COLUMN IF EXISTS "LoanMaxMultipleOfContributions",
                DROP COLUMN IF EXISTS "LoanMinMembershipMonths", DROP COLUMN IF EXISTS "LoanAllowWithArrears", DROP COLUMN IF EXISTS "LoanAllowMultipleActive",
                DROP COLUMN IF EXISTS "LoanMinCashReserve", DROP COLUMN IF EXISTS "LoanRequiredApprovals", DROP COLUMN IF EXISTS "LoanApprovalValidDays";
            DROP TABLE IF EXISTS "RefreshTokens";
            DROP TABLE IF EXISTS "LoanApplications";
            """);
    }
}
