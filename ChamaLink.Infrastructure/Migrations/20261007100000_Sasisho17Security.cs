using ChamaLink.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChamaLink.Infrastructure.Migrations;

/// <summary>
/// SASISHO17 — marekebisho ya ukaguzi wa usalama (pentest ya white-box ya SASISHO16):
///  - RoleChangeRequests: vyeo vya uongozi kwa pendekezo + kura (NEW-2); GroupMembers.LeaderPowersFrom (kipindi cha kusubiri),
///    AccountLinkedAt.
///  - MkobaStatementImports: PDF ya M-Koba inaingia kama rasimu, kiongozi wa pili anaidhinisha (P0-2/P1-7/P1-8);
///    MkobaRawTransactions.ImportId.
///  - GroupResetRequests: kufuta data ya fedha = viongozi wawili (P0-6).
///  - LoginFailures: lockout kwa akaunti+IP, atomiki (P1-4/NEW-5); UX_Users_Email_Lower (P1-2).
///  - MemberStatusChangeRequests.AppliedAt: EffectiveDate halisi.
///  - TruthGroupSettings: RoleChangeApprovals, LeaderCoolingOffDays, CashStatementMaxAgeDays (sera; hakuna hardcode).
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261007100000_Sasisho17Security")]
public partial class Sasisho17Security : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.Sql(""""
-- SASISHO17: usalama wa utawala, uingizaji wa M-Koba kwa maker-checker, auth atomiki
ALTER TABLE "GroupMembers" ADD COLUMN IF NOT EXISTS "LeaderPowersFrom" timestamp with time zone;
ALTER TABLE "GroupMembers" ADD COLUMN IF NOT EXISTS "AccountLinkedAt" timestamp with time zone;
ALTER TABLE "MemberStatusChangeRequests" ADD COLUMN IF NOT EXISTS "AppliedAt" timestamp with time zone;
-- Maombi yaliyokwisha idhinishwa kabla ya SASISHO17 yalitekelezwa siku ya idhini
UPDATE "MemberStatusChangeRequests" SET "AppliedAt" = "DecidedAt" WHERE "Status" = 'Approved' AND "AppliedAt" IS NULL;
ALTER TABLE "TruthGroupSettings" ADD COLUMN IF NOT EXISTS "RoleChangeApprovals" integer;
ALTER TABLE "TruthGroupSettings" ADD COLUMN IF NOT EXISTS "LeaderCoolingOffDays" integer;
ALTER TABLE "TruthGroupSettings" ADD COLUMN IF NOT EXISTS "CashStatementMaxAgeDays" integer;
ALTER TABLE "MkobaRawTransactions" ADD COLUMN IF NOT EXISTS "ImportId" uuid;

CREATE TABLE IF NOT EXISTS "RoleChangeRequests" (
    "Id" uuid NOT NULL,
    "GroupId" uuid NOT NULL,
    "GroupMemberId" uuid NOT NULL,
    "FromRole" character varying(30) NOT NULL,
    "ToRole" character varying(30) NOT NULL,
    "Reason" character varying(500) NOT NULL,
    "RequiredApprovals" integer NOT NULL,
    "RejectionsToReject" integer NOT NULL,
    "LeadersOnly" boolean NOT NULL,
    "Status" character varying(20) NOT NULL,
    "RequestedByUserId" uuid NOT NULL,
    "RequestedAt" timestamp with time zone NOT NULL,
    "DecidedAt" timestamp with time zone,
    "DecisionNote" character varying(500),
    "CoolingOffDaysApplied" integer,
    CONSTRAINT "PK_RoleChangeRequests" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_RoleChangeRequests_Groups_GroupId" FOREIGN KEY ("GroupId") REFERENCES "Groups" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_RoleChangeRequests_GroupMembers_GroupMemberId" FOREIGN KEY ("GroupMemberId") REFERENCES "GroupMembers" ("Id") ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS "IX_RoleChangeRequests_GroupId_Status" ON "RoleChangeRequests" ("GroupId", "Status");
-- Ombi moja tu linalosubiri kwa kila mwanachama (kinga ya ushindani kwenye DB, si code peke yake)
CREATE UNIQUE INDEX IF NOT EXISTS "UX_RoleChangeRequests_Pending" ON "RoleChangeRequests" ("GroupMemberId") WHERE "Status" = 'Pending';

CREATE TABLE IF NOT EXISTS "MkobaStatementImports" (
    "Id" uuid NOT NULL,
    "GroupId" uuid NOT NULL,
    "FileHash" character varying(80) NOT NULL,
    "FileName" character varying(200),
    "FileSize" bigint NOT NULL,
    "StorageKey" character varying(200),
    "Status" character varying(20) NOT NULL,
    "UploadedByUserId" uuid NOT NULL,
    "UploadedAt" timestamp with time zone NOT NULL,
    "DecidedByUserId" uuid,
    "DecidedAt" timestamp with time zone,
    "DecisionNote" character varying(500),
    "RowsInPdf" integer NOT NULL,
    "NewRows" integer NOT NULL,
    "ExistingRows" integer NOT NULL,
    "ConflictingRows" integer NOT NULL,
    "BackdatedRows" integer NOT NULL,
    "PeriodFrom" timestamp with time zone,
    "PeriodTo" timestamp with time zone,
    "LastBalance" numeric(18,2),
    "RowsJson" text NOT NULL,
    "ReportJson" text NOT NULL,
    CONSTRAINT "PK_MkobaStatementImports" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_MkobaStatementImports_Groups_GroupId" FOREIGN KEY ("GroupId") REFERENCES "Groups" ("Id") ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS "IX_MkobaStatementImports_GroupId_Status" ON "MkobaStatementImports" ("GroupId", "Status");
-- Rasimu moja tu inayosubiri kwa kila kikundi
CREATE UNIQUE INDEX IF NOT EXISTS "UX_MkobaStatementImports_Pending" ON "MkobaStatementImports" ("GroupId") WHERE "Status" = 'Pending';

CREATE TABLE IF NOT EXISTS "GroupResetRequests" (
    "Id" uuid NOT NULL,
    "GroupId" uuid NOT NULL,
    "KeepLoans" boolean NOT NULL,
    "Status" character varying(20) NOT NULL,
    "RequestedByUserId" uuid NOT NULL,
    "RequestedAt" timestamp with time zone NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "DecidedByUserId" uuid,
    "DecidedAt" timestamp with time zone,
    CONSTRAINT "PK_GroupResetRequests" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_GroupResetRequests_Groups_GroupId" FOREIGN KEY ("GroupId") REFERENCES "Groups" ("Id") ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS "IX_GroupResetRequests_GroupId_Status" ON "GroupResetRequests" ("GroupId", "Status");

CREATE TABLE IF NOT EXISTS "LoginFailures" (
    "Id" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "IpKey" character varying(80) NOT NULL,
    "Count" integer NOT NULL,
    "WindowStartUtc" timestamp with time zone NOT NULL,
    "LockoutEndUtc" timestamp with time zone,
    CONSTRAINT "PK_LoginFailures" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_LoginFailures_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_LoginFailures_UserId_IpKey" ON "LoginFailures" ("UserId", "IpKey");

-- P1-2: email ya kipekee (bila kujali herufi kubwa/ndogo). Kama kuna nakala, migration inasimama na kuzitaja.
DO $$
DECLARE d text;
BEGIN
  SELECT string_agg(e, ', ') INTO d FROM (SELECT lower("Email") e FROM "Users" WHERE "Email" IS NOT NULL AND "Email" <> '' GROUP BY lower("Email") HAVING count(*) > 1) x;
  IF d IS NOT NULL THEN
    RAISE EXCEPTION 'SASISHO17: email zinazojirudia (rekebisha kwanza, kisha endesha tena): %', d;
  END IF;
END $$;
CREATE UNIQUE INDEX IF NOT EXISTS "UX_Users_Email_Lower" ON "Users" (lower("Email")) WHERE "Email" IS NOT NULL AND "Email" <> '';

"""");
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.Sql(""""
DROP INDEX IF EXISTS "UX_Users_Email_Lower";
DROP TABLE IF EXISTS "LoginFailures";
DROP TABLE IF EXISTS "GroupResetRequests";
DROP TABLE IF EXISTS "MkobaStatementImports";
DROP TABLE IF EXISTS "RoleChangeRequests";
ALTER TABLE "MkobaRawTransactions" DROP COLUMN IF EXISTS "ImportId";
ALTER TABLE "TruthGroupSettings" DROP COLUMN IF EXISTS "CashStatementMaxAgeDays";
ALTER TABLE "TruthGroupSettings" DROP COLUMN IF EXISTS "LeaderCoolingOffDays";
ALTER TABLE "TruthGroupSettings" DROP COLUMN IF EXISTS "RoleChangeApprovals";
ALTER TABLE "MemberStatusChangeRequests" DROP COLUMN IF EXISTS "AppliedAt";
ALTER TABLE "GroupMembers" DROP COLUMN IF EXISTS "AccountLinkedAt";
ALTER TABLE "GroupMembers" DROP COLUMN IF EXISTS "LeaderPowersFrom";
"""");
    }
}
