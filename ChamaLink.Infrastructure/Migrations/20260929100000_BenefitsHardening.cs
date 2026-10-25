using ChamaLink.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChamaLink.Infrastructure.Migrations;

/// <summary>
/// SASISHO13 — Ulinzi wa Mafao (B1–B10):
///  - BenefitGovernances: toleo (Version), Kianzio kimelipwa, cooldown, kikomo cha kiasi kwa kipindi, pesa ipo?,
///    nafasi za kuanzisha/kuthibitisha malipo, aliyeidhinisha ≠ mweka-malipo, ndugu wasishiriki, vizingiti vya tahadhari.
///    Sera zilizopo zinapewa matoleo 1,2,3... na mipangilio ya ulinzi (Mhazini anaanzisha; Mwenyekiti/Katibu wanathibitisha;
///    pesa lazima iwepo; ndugu hawashiriki) — viongozi wanaweza kupendekeza toleo jipya.
///  - BenefitCases: PolicyVersion + hatua ya kuanzisha malipo (PaymentInitiated*).
///  - MemberRelations (mgongano wa maslahi), BenefitLedgerEntries (Approved = JF + deni + pesa).
///    Kesi zilizokwisha kulipwa zinaandikiwa maandishi yake ya fedha.
///  - AuditEvents: mnyororo wa hash (ChainIndex, PrevHash, Hash) + trigger inayozuia UPDATE/DELETE.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260929100000_BenefitsHardening")]
public partial class BenefitsHardening : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.Sql("""
            ALTER TABLE "BenefitGovernances" ADD COLUMN "Version" integer NOT NULL DEFAULT 0;
            ALTER TABLE "BenefitGovernances" ADD COLUMN "RequireJoinFeePaid" boolean NOT NULL DEFAULT FALSE;
            ALTER TABLE "BenefitGovernances" ADD COLUMN "CooldownDays" integer NULL;
            ALTER TABLE "BenefitGovernances" ADD COLUMN "MaxBenefitAmountPerPeriod" numeric(18,2) NULL;
            ALTER TABLE "BenefitGovernances" ADD COLUMN "MaxBenefitPeriodDays" integer NULL;
            ALTER TABLE "BenefitGovernances" ADD COLUMN "RequireCashAvailability" boolean NOT NULL DEFAULT TRUE;
            ALTER TABLE "BenefitGovernances" ADD COLUMN "PaymentInitiatorRoles" character varying(60) NOT NULL DEFAULT '2';
            ALTER TABLE "BenefitGovernances" ADD COLUMN "PaymentConfirmerRoles" character varying(60) NOT NULL DEFAULT '1,3';
            ALTER TABLE "BenefitGovernances" ADD COLUMN "ApproverCannotInitiatePayment" boolean NOT NULL DEFAULT FALSE;
            ALTER TABLE "BenefitGovernances" ADD COLUMN "ExcludeRelatedParties" boolean NOT NULL DEFAULT TRUE;
            ALTER TABLE "BenefitGovernances" ADD COLUMN "FrequentClaimsFlagCount" integer NULL;
            ALTER TABLE "BenefitGovernances" ADD COLUMN "RecentPolicyChangeFlagDays" integer NULL;
            UPDATE "BenefitGovernances" g SET "Version" = v.rn
              FROM (SELECT "Id", ROW_NUMBER() OVER (PARTITION BY "GroupId" ORDER BY "RequestedAt", "Id") AS rn FROM "BenefitGovernances") v
             WHERE g."Id" = v."Id";
            -- Thamani za sera mpya zinatoka kwa viongozi (hakuna default kwenye model)
            ALTER TABLE "BenefitGovernances" ALTER COLUMN "Version" DROP DEFAULT;
            ALTER TABLE "BenefitGovernances" ALTER COLUMN "RequireJoinFeePaid" DROP DEFAULT;
            ALTER TABLE "BenefitGovernances" ALTER COLUMN "RequireCashAvailability" DROP DEFAULT;
            ALTER TABLE "BenefitGovernances" ALTER COLUMN "PaymentInitiatorRoles" DROP DEFAULT;
            ALTER TABLE "BenefitGovernances" ALTER COLUMN "PaymentConfirmerRoles" DROP DEFAULT;
            ALTER TABLE "BenefitGovernances" ALTER COLUMN "ApproverCannotInitiatePayment" DROP DEFAULT;
            ALTER TABLE "BenefitGovernances" ALTER COLUMN "ExcludeRelatedParties" DROP DEFAULT;

            ALTER TABLE "BenefitCases" ADD COLUMN "PolicyVersion" integer NULL;
            ALTER TABLE "BenefitCases" ADD COLUMN "PaymentInitiatedByUserId" uuid NULL;
            ALTER TABLE "BenefitCases" ADD COLUMN "PaymentInitiatedAt" timestamp with time zone NULL;
            ALTER TABLE "BenefitCases" ADD COLUMN "PaymentInitiatedToPhone" character varying(20) NULL;
            ALTER TABLE "BenefitCases" ADD COLUMN "CashAvailableAtInitiation" numeric(18,2) NULL;
            UPDATE "BenefitCases" c SET "PolicyVersion" = g."Version" FROM "BenefitGovernances" g WHERE c."GovernanceId" = g."Id";

            CREATE TABLE "MemberRelations" (
                "Id" uuid NOT NULL,
                "GroupId" uuid NOT NULL,
                "MemberAId" uuid NOT NULL,
                "MemberBId" uuid NOT NULL,
                "Type" character varying(30) NOT NULL,
                "Note" character varying(300),
                "Status" character varying(20) NOT NULL,
                "CreatedByUserId" uuid NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "EndRequestedByUserId" uuid,
                "EndRequestedAt" timestamp with time zone,
                "EndReason" character varying(300),
                "EndedByUserId" uuid,
                "EndedAt" timestamp with time zone,
                CONSTRAINT "PK_MemberRelations" PRIMARY KEY ("Id")
            );
            CREATE INDEX "IX_MemberRelations_GroupId_Status" ON "MemberRelations" ("GroupId", "Status");
            CREATE INDEX "IX_MemberRelations_MemberAId" ON "MemberRelations" ("MemberAId");
            CREATE INDEX "IX_MemberRelations_MemberBId" ON "MemberRelations" ("MemberBId");

            CREATE TABLE "BenefitLedgerEntries" (
                "Id" uuid NOT NULL,
                "GroupId" uuid NOT NULL,
                "BenefitCaseId" uuid NOT NULL,
                "BeneficiaryGroupMemberId" uuid NOT NULL,
                "Kind" character varying(30) NOT NULL,
                "Amount" numeric(18,2) NOT NULL,
                "Reference" character varying(60),
                "PostedAt" timestamp with time zone NOT NULL,
                "PostedByUserId" uuid NOT NULL,
                CONSTRAINT "PK_BenefitLedgerEntries" PRIMARY KEY ("Id")
            );
            CREATE UNIQUE INDEX "IX_BenefitLedgerEntries_BenefitCaseId_Kind" ON "BenefitLedgerEntries" ("BenefitCaseId", "Kind");
            CREATE INDEX "IX_BenefitLedgerEntries_GroupId_BeneficiaryGroupMemberId" ON "BenefitLedgerEntries" ("GroupId", "BeneficiaryGroupMemberId");

            -- Kesi zilizokwisha kulipwa: maandishi ya fedha (Approved = JF + deni + pesa)
            INSERT INTO "BenefitLedgerEntries" ("Id","GroupId","BenefitCaseId","BeneficiaryGroupMemberId","Kind","Amount","Reference","PostedAt","PostedByUserId")
            SELECT gen_random_uuid(), c."GroupId", c."Id", c."BeneficiaryGroupMemberId", 'Approved', c."GrossAmount", c."CaseNumber",
                   COALESCE(c."PaidConfirmedAt", now()), COALESCE(c."PaidConfirmedByUserId", c."CreatedByUserId")
              FROM "BenefitCases" c WHERE c."Status" = 'Paid';
            INSERT INTO "BenefitLedgerEntries" ("Id","GroupId","BenefitCaseId","BeneficiaryGroupMemberId","Kind","Amount","Reference","PostedAt","PostedByUserId")
            SELECT gen_random_uuid(), c."GroupId", c."Id", c."BeneficiaryGroupMemberId", 'JoinFeeOffset', c."JoinFeeDeducted", 'MAFAO-JF-' || c."CaseNumber",
                   COALESCE(c."PaidConfirmedAt", now()), COALESCE(c."PaidConfirmedByUserId", c."CreatedByUserId")
              FROM "BenefitCases" c WHERE c."Status" = 'Paid' AND c."JoinFeeDeducted" > 0;
            INSERT INTO "BenefitLedgerEntries" ("Id","GroupId","BenefitCaseId","BeneficiaryGroupMemberId","Kind","Amount","Reference","PostedAt","PostedByUserId")
            SELECT gen_random_uuid(), c."GroupId", c."Id", c."BeneficiaryGroupMemberId", 'ArrearsOffset', c."ArrearsDeducted", 'MAFAO-DENI-' || c."CaseNumber",
                   COALESCE(c."PaidConfirmedAt", now()), COALESCE(c."PaidConfirmedByUserId", c."CreatedByUserId")
              FROM "BenefitCases" c WHERE c."Status" = 'Paid' AND c."ArrearsDeducted" > 0;
            INSERT INTO "BenefitLedgerEntries" ("Id","GroupId","BenefitCaseId","BeneficiaryGroupMemberId","Kind","Amount","Reference","PostedAt","PostedByUserId")
            SELECT gen_random_uuid(), c."GroupId", c."Id", c."BeneficiaryGroupMemberId", 'CashPaid', c."NetAmount", c."MkobaReference",
                   COALESCE(c."PaidConfirmedAt", now()), COALESCE(c."PaidConfirmedByUserId", c."CreatedByUserId")
              FROM "BenefitCases" c WHERE c."Status" = 'Paid';

            ALTER TABLE "AuditEvents" ADD COLUMN "ChainIndex" bigint NULL;
            ALTER TABLE "AuditEvents" ADD COLUMN "PrevHash" character varying(64) NULL;
            ALTER TABLE "AuditEvents" ADD COLUMN "Hash" character varying(64) NULL;
            CREATE UNIQUE INDEX "IX_AuditEvents_GroupId_ChainIndex" ON "AuditEvents" ("GroupId", "ChainIndex") WHERE "ChainIndex" IS NOT NULL;
            """);

        // Kumbukumbu za ukaguzi: INSERT tu. UPDATE inaruhusiwa MARA MOJA kujaza mnyororo (ChainIndex/PrevHash/Hash)
        // kwa tukio ambalo bado halijafungwa, bila kubadilisha kitu kingine chochote. DELETE hairuhusiwi.
        mb.Sql("""
            CREATE OR REPLACE FUNCTION chamalink_audit_guard() RETURNS trigger AS $$
            BEGIN
                IF TG_OP = 'DELETE' THEN
                    RAISE EXCEPTION 'AuditEvents ni kumbukumbu za kudumu: haziwezi kufutwa';
                END IF;
                IF OLD."Hash" IS NOT NULL THEN
                    RAISE EXCEPTION 'AuditEvent % imeshafungwa kwenye mnyororo: haiwezi kubadilishwa', OLD."Id";
                END IF;
                IF (to_jsonb(NEW) - 'ChainIndex' - 'PrevHash' - 'Hash') IS DISTINCT FROM (to_jsonb(OLD) - 'ChainIndex' - 'PrevHash' - 'Hash') THEN
                    RAISE EXCEPTION 'AuditEvent %: sehemu pekee zinazoweza kujazwa ni za mnyororo wa hash', OLD."Id";
                END IF;
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;
            DROP TRIGGER IF EXISTS "TR_AuditEvents_AppendOnly" ON "AuditEvents";
            CREATE TRIGGER "TR_AuditEvents_AppendOnly" BEFORE UPDATE OR DELETE ON "AuditEvents"
                FOR EACH ROW EXECUTE FUNCTION chamalink_audit_guard();
            """);
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.Sql("""
            DROP TRIGGER IF EXISTS "TR_AuditEvents_AppendOnly" ON "AuditEvents";
            DROP FUNCTION IF EXISTS chamalink_audit_guard();
            DROP INDEX IF EXISTS "IX_AuditEvents_GroupId_ChainIndex";
            ALTER TABLE "AuditEvents" DROP COLUMN IF EXISTS "ChainIndex", DROP COLUMN IF EXISTS "PrevHash", DROP COLUMN IF EXISTS "Hash";
            DROP TABLE IF EXISTS "BenefitLedgerEntries";
            DROP TABLE IF EXISTS "MemberRelations";
            ALTER TABLE "BenefitCases" DROP COLUMN IF EXISTS "PolicyVersion", DROP COLUMN IF EXISTS "PaymentInitiatedByUserId",
                DROP COLUMN IF EXISTS "PaymentInitiatedAt", DROP COLUMN IF EXISTS "PaymentInitiatedToPhone", DROP COLUMN IF EXISTS "CashAvailableAtInitiation";
            ALTER TABLE "BenefitGovernances" DROP COLUMN IF EXISTS "Version", DROP COLUMN IF EXISTS "RequireJoinFeePaid", DROP COLUMN IF EXISTS "CooldownDays",
                DROP COLUMN IF EXISTS "MaxBenefitAmountPerPeriod", DROP COLUMN IF EXISTS "MaxBenefitPeriodDays", DROP COLUMN IF EXISTS "RequireCashAvailability",
                DROP COLUMN IF EXISTS "PaymentInitiatorRoles", DROP COLUMN IF EXISTS "PaymentConfirmerRoles", DROP COLUMN IF EXISTS "ApproverCannotInitiatePayment",
                DROP COLUMN IF EXISTS "ExcludeRelatedParties", DROP COLUMN IF EXISTS "FrequentClaimsFlagCount", DROP COLUMN IF EXISTS "RecentPolicyChangeFlagDays";
            """);
    }
}
