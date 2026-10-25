using ChamaLink.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChamaLink.Infrastructure.Migrations;

/// <summary>
/// SASISHO20 — MSIMAMIZI WA MFUMO (platform admin).
///
/// Jedwali nne MPYA pekee — hakuna jedwali la fedha linaloguswa, hakuna nguzo ya fedha
/// inayoongezwa. Msimamizi anaona muhtasari; taarifa za mwanachama mmoja mmoja
/// zinafunguliwa tu kwa break-glass iliyoidhinishwa na msimamizi wa pili (saa 24).
///
///   1. PlatformAdmins            — nafasi ya jukwaa (Support / Oversight / SuperAdmin) + siri ya TOTP (imesimbwa)
///   2. PlatformAuditEvents       — kumbukumbu za jukwaa; APPEND-ONLY (trigger inakataa UPDATE/DELETE)
///   3. PlatformApprovals         — maamuzi ya watu wawili (break-glass, kuongeza/kuondoa msimamizi)
///   4. PlatformPasswordResetCodes— code ya mara moja (HASH pekee; msimamizi haoni neno la siri)
///
/// Hakuna msimamizi wa kawaida aliyeundwa hapa: wa kwanza anaundwa kwa console
/// (`--seed-platform-admin`) — hakuna neno la siri lililoandikwa kwenye code.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261022140000_Sasisho20PlatformAdmin")]
public partial class Sasisho20PlatformAdmin : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ---------------------------------------------------------------- 1. Wasimamizi
        migrationBuilder.CreateTable(
            name: "PlatformAdmins",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                Level = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                TotpSecretProtected = table.Column<string>(type: "varchar(400)", maxLength: 400, nullable: true),
                PendingTotpSecretProtected = table.Column<string>(type: "varchar(400)", maxLength: 400, nullable: true),
                MfaEnabledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                LastMfaVerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedByPlatformAdminId = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                DeactivatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PlatformAdmins", x => x.Id);
                table.ForeignKey("FK_PlatformAdmins_Users_UserId", x => x.UserId, principalTable: "Users",
                    principalColumn: "Id", onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex(name: "IX_PlatformAdmins_UserId", table: "PlatformAdmins", column: "UserId", unique: true);

        // ---------------------------------------------------------------- 2. Kumbukumbu (append-only)
        migrationBuilder.CreateTable(
            name: "PlatformAuditEvents",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                PlatformAdminId = table.Column<Guid>(type: "uuid", nullable: true),
                UserId = table.Column<Guid>(type: "uuid", nullable: true),
                Action = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false),
                GroupId = table.Column<Guid>(type: "uuid", nullable: true),
                SubjectId = table.Column<Guid>(type: "uuid", nullable: true),
                Reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                DetailJson = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                Ip = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_PlatformAuditEvents", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_PlatformAuditEvents_At", table: "PlatformAuditEvents", column: "At");
        migrationBuilder.CreateIndex(name: "IX_PlatformAuditEvents_GroupId_At", table: "PlatformAuditEvents", columns: new[] { "GroupId", "At" });
        migrationBuilder.CreateIndex(name: "IX_PlatformAuditEvents_PlatformAdminId_At", table: "PlatformAuditEvents", columns: new[] { "PlatformAdminId", "At" });

        // ---------------------------------------------------------------- 3. Maamuzi ya watu wawili
        migrationBuilder.CreateTable(
            name: "PlatformApprovals",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Action = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                RequestedByPlatformAdminId = table.Column<Guid>(type: "uuid", nullable: false),
                DecidedByPlatformAdminId = table.Column<Guid>(type: "uuid", nullable: true),
                GroupId = table.Column<Guid>(type: "uuid", nullable: true),
                TargetPlatformAdminId = table.Column<Guid>(type: "uuid", nullable: true),
                TargetUserId = table.Column<Guid>(type: "uuid", nullable: true),
                PayloadJson = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                Reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                DecisionNote = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_PlatformApprovals", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_PlatformApprovals_GroupId_Status", table: "PlatformApprovals", columns: new[] { "GroupId", "Status" });
        migrationBuilder.CreateIndex(name: "IX_PlatformApprovals_Status_Action", table: "PlatformApprovals", columns: new[] { "Status", "Action" });

        // ---------------------------------------------------------------- 4. Code za kureset
        migrationBuilder.CreateTable(
            name: "PlatformPasswordResetCodes",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                GroupId = table.Column<Guid>(type: "uuid", nullable: true),
                CodeHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                Salt = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                Attempts = table.Column<int>(type: "integer", nullable: false),
                ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                IssuedByPlatformAdminId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_PlatformPasswordResetCodes", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_PlatformPasswordResetCodes_ExpiresAt", table: "PlatformPasswordResetCodes", column: "ExpiresAt");
        migrationBuilder.CreateIndex(name: "IX_PlatformPasswordResetCodes_UserId_UsedAt", table: "PlatformPasswordResetCodes", columns: new[] { "UserId", "UsedAt" });

        // ---------------------------------------------------------------- 5. Append-only
        // Kumbukumbu za msimamizi HAZIWEZI kufutwa wala kubadilishwa — hata na msimamizi mwenyewe
        // kupitia SQL ya moja kwa moja. Hii ndiyo dhamana ya "kikundi kinaona kila kitu".
        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION chamalink_platform_audit_guard() RETURNS trigger AS $$
            BEGIN
                IF TG_OP = 'DELETE' THEN
                    RAISE EXCEPTION 'PlatformAuditEvents ni kumbukumbu za kudumu: haziwezi kufutwa';
                END IF;
                RAISE EXCEPTION 'PlatformAuditEvents ni kumbukumbu za kudumu: haziwezi kubadilishwa';
            END;
            $$ LANGUAGE plpgsql;
            DROP TRIGGER IF EXISTS "TR_PlatformAuditEvents_AppendOnly" ON "PlatformAuditEvents";
            CREATE TRIGGER "TR_PlatformAuditEvents_AppendOnly" BEFORE UPDATE OR DELETE ON "PlatformAuditEvents"
                FOR EACH ROW EXECUTE FUNCTION chamalink_platform_audit_guard();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS "TR_PlatformAuditEvents_AppendOnly" ON "PlatformAuditEvents";
            DROP FUNCTION IF EXISTS chamalink_platform_audit_guard();
            """);
        migrationBuilder.DropTable(name: "PlatformPasswordResetCodes");
        migrationBuilder.DropTable(name: "PlatformApprovals");
        migrationBuilder.DropTable(name: "PlatformAuditEvents");
        migrationBuilder.DropTable(name: "PlatformAdmins");
    }
}
