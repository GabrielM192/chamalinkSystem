using ChamaLink.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChamaLink.Infrastructure.Migrations;

/// <summary>
/// SASISHO14 — mapengo 12 ya mafao:
///  - BenefitGovernances: pesa inahakikiwa wakati wa idhini (CashCheckAtApproval), aina za uhusiano za mgongano,
///    nafasi za wapiga kura / Mhakiki wa Ustahiki / Wakaguzi, kizingiti cha malipo makubwa, kuzuia mhusika yule yule
///    kati ya wanachama, kukata deni la mkopo PEKE YAKE, tarehe ya kuanza kwa sera (EffectiveFrom), tarehe ya kuhesabu wanachama.
///    Sera zilizopo: sera ya KWANZA iliyoidhinishwa ya kila kikundi inaanza 2000-01-01 (inatawala matukio ya zamani);
///    zinazofuata zinaanza siku zilipoidhinishwa (saa za EAT). Nafasi za wapiga kura zinatoka AllowMemberApprovers;
///    wahakiki = viongozi; deni la mkopo linakatwa kama sera ilikuwa inakata madeni; hesabu siku ya tukio.
///  - BenefitRates: EffectiveTo (kiwango kinaisha).
///  - BenefitCases: LoanArrearsDeducted (NULL = kesi ya zamani: ArrearsDeducted ilijumuisha mikopo),
///    njia ya malipo (Mkoba/Cash) + taarifa za taslimu ya zamani.
///  - MemberNotifications: LocJson (kiolezo kinachotafsirika).
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260930100000_BenefitsHardening2")]
public partial class BenefitsHardening2 : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.Sql("""
            ALTER TABLE "BenefitGovernances" ADD COLUMN "CashCheckAtApproval" boolean NOT NULL DEFAULT FALSE;
            ALTER TABLE "BenefitGovernances" ADD COLUMN "ConflictRelationTypes" character varying(60) NOT NULL DEFAULT '';
            ALTER TABLE "BenefitGovernances" ADD COLUMN "ApproverRoles" character varying(60) NOT NULL DEFAULT '';
            ALTER TABLE "BenefitGovernances" ADD COLUMN "EvidenceVerifierRoles" character varying(60) NOT NULL DEFAULT '';
            ALTER TABLE "BenefitGovernances" ADD COLUMN "AuditorMemberIds" character varying(2000) NOT NULL DEFAULT '';
            ALTER TABLE "BenefitGovernances" ADD COLUMN "HighValueThreshold" numeric(18,2) NULL;
            ALTER TABLE "BenefitGovernances" ADD COLUMN "BlockSameSubjectAcrossMembers" boolean NOT NULL DEFAULT FALSE;
            ALTER TABLE "BenefitGovernances" ADD COLUMN "DeductLoanArrears" boolean NOT NULL DEFAULT FALSE;
            ALTER TABLE "BenefitGovernances" ADD COLUMN "EffectiveFrom" timestamp with time zone NOT NULL DEFAULT TIMESTAMPTZ '2000-01-01 00:00:00+00';
            ALTER TABLE "BenefitGovernances" ADD COLUMN "HeadcountAsOf" character varying(30) NOT NULL DEFAULT 'EventDate';

            UPDATE "BenefitGovernances" SET
                "ApproverRoles" = CASE WHEN "AllowMemberApprovers" THEN '1,2,3,4' ELSE '1,2,3' END,
                "EvidenceVerifierRoles" = '1,2,3',
                "ConflictRelationTypes" = CASE WHEN "ExcludeRelatedParties" THEN '1,2,3,4,5,6,7' ELSE '' END,
                "DeductLoanArrears" = "DeductArrears";
            -- Tarehe ya kuanza: sera zilizoidhinishwa = siku ya uamuzi (EAT); ya kwanza ya kila kikundi = 2000-01-01;
            -- zinazosubiri/zilizokataliwa = siku ya ombi (ikiidhinishwa baadaye itasogezwa hadi siku hiyo).
            UPDATE "BenefitGovernances" SET "EffectiveFrom" =
                ((COALESCE("DecidedAt", "RequestedAt") AT TIME ZONE 'Africa/Dar_es_Salaam')::date)::timestamp AT TIME ZONE 'UTC';
            UPDATE "BenefitGovernances" g SET "EffectiveFrom" = TIMESTAMPTZ '2000-01-01 00:00:00+00'
              FROM (SELECT DISTINCT ON ("GroupId") "Id" FROM "BenefitGovernances"
                     WHERE "Status" = 'Approved' AND "DecidedAt" IS NOT NULL
                     ORDER BY "GroupId", "DecidedAt", "Id") f
             WHERE g."Id" = f."Id";

            ALTER TABLE "BenefitGovernances" ALTER COLUMN "CashCheckAtApproval" DROP DEFAULT;
            ALTER TABLE "BenefitGovernances" ALTER COLUMN "ConflictRelationTypes" DROP DEFAULT;
            ALTER TABLE "BenefitGovernances" ALTER COLUMN "ApproverRoles" DROP DEFAULT;
            ALTER TABLE "BenefitGovernances" ALTER COLUMN "EvidenceVerifierRoles" DROP DEFAULT;
            ALTER TABLE "BenefitGovernances" ALTER COLUMN "AuditorMemberIds" DROP DEFAULT;
            ALTER TABLE "BenefitGovernances" ALTER COLUMN "BlockSameSubjectAcrossMembers" DROP DEFAULT;
            ALTER TABLE "BenefitGovernances" ALTER COLUMN "DeductLoanArrears" DROP DEFAULT;
            ALTER TABLE "BenefitGovernances" ALTER COLUMN "EffectiveFrom" DROP DEFAULT;
            ALTER TABLE "BenefitGovernances" ALTER COLUMN "HeadcountAsOf" DROP DEFAULT;

            ALTER TABLE "BenefitRates" ADD COLUMN "EffectiveTo" timestamp with time zone NULL;

            ALTER TABLE "BenefitCases" ADD COLUMN "LoanArrearsDeducted" numeric(18,2) NULL;
            ALTER TABLE "BenefitCases" ADD COLUMN "PaymentChannel" character varying(20) NOT NULL DEFAULT 'Mkoba';
            ALTER TABLE "BenefitCases" ADD COLUMN "CashRecipientName" character varying(200) NULL;
            ALTER TABLE "BenefitCases" ADD COLUMN "CashRecipientPhone" character varying(20) NULL;
            ALTER TABLE "BenefitCases" ADD COLUMN "ExceptionReason" character varying(500) NULL;
            ALTER TABLE "BenefitCases" ALTER COLUMN "PaymentChannel" DROP DEFAULT;

            ALTER TABLE "MemberNotifications" ADD COLUMN "LocJson" character varying(4000) NULL;
            """);
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.Sql("""
            ALTER TABLE "MemberNotifications" DROP COLUMN IF EXISTS "LocJson";
            ALTER TABLE "BenefitCases" DROP COLUMN IF EXISTS "LoanArrearsDeducted", DROP COLUMN IF EXISTS "PaymentChannel",
                DROP COLUMN IF EXISTS "CashRecipientName", DROP COLUMN IF EXISTS "CashRecipientPhone", DROP COLUMN IF EXISTS "ExceptionReason";
            ALTER TABLE "BenefitRates" DROP COLUMN IF EXISTS "EffectiveTo";
            ALTER TABLE "BenefitGovernances" DROP COLUMN IF EXISTS "CashCheckAtApproval", DROP COLUMN IF EXISTS "ConflictRelationTypes",
                DROP COLUMN IF EXISTS "ApproverRoles", DROP COLUMN IF EXISTS "EvidenceVerifierRoles", DROP COLUMN IF EXISTS "AuditorMemberIds",
                DROP COLUMN IF EXISTS "HighValueThreshold", DROP COLUMN IF EXISTS "BlockSameSubjectAcrossMembers", DROP COLUMN IF EXISTS "DeductLoanArrears",
                DROP COLUMN IF EXISTS "EffectiveFrom", DROP COLUMN IF EXISTS "HeadcountAsOf";
            """);
    }
}
