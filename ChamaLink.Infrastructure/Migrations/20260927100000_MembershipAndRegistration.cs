using System;
using ChamaLink.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChamaLink.Infrastructure.Migrations;

/// <summary>
/// Usajili na uanachama:
///  - Groups.ShortCode (kifupi cha namba za uanachama) na Groups.JoinCode (kodi ya kujiunga, ya kipekee).
///  - GroupMembers.MemberNumberIssuedAt (namba rasmi haibadiliki) + namba ya kipekee ndani ya kikundi.
///  - Jedwali MembershipRequests (maombi ya kujiunga; kiongozi anaidhinisha).
/// Imeandikwa kwa mkono kama migrations nyingine za Injini ya Ukweli.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260927100000_MembershipAndRegistration")]
public partial class MembershipAndRegistration : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.AddColumn<string>(name: "ShortCode", table: "Groups", type: "character varying(6)", maxLength: 6, nullable: true);
        mb.AddColumn<string>(name: "JoinCode", table: "Groups", type: "character varying(20)", maxLength: 20, nullable: true);
        mb.CreateIndex(name: "IX_Groups_JoinCode", table: "Groups", column: "JoinCode", unique: true, filter: "\"JoinCode\" IS NOT NULL");

        mb.AddColumn<DateTime>(name: "MemberNumberIssuedAt", table: "GroupMembers", type: "timestamp with time zone", nullable: true);

        // Namba za majaribio za zamani (mf. "M001") zinaweza kujirudia ndani ya kikundi kimoja. Kabla ya kuweka
        // unique index, namba zinazojirudia zinaachwa tupu isipokuwa ya kwanza (kwa JoinedAt). Hazipotei kwa
        // maana ya fedha: rekodi ya fedha ni GroupMember.Id. Kiongozi atatoa namba rasmi (UMK-2026-001) baadaye.
        mb.Sql("""
            UPDATE "GroupMembers" gm SET "MemberNumber" = ''
            FROM (
                SELECT "Id", ROW_NUMBER() OVER (PARTITION BY "GroupId", "MemberNumber" ORDER BY "JoinedAt", "Id") AS rn
                FROM "GroupMembers" WHERE "MemberNumber" <> ''
            ) d
            WHERE gm."Id" = d."Id" AND d.rn > 1;
            """);
        mb.CreateIndex(name: "IX_GroupMembers_GroupId_MemberNumber", table: "GroupMembers",
            columns: new[] { "GroupId", "MemberNumber" }, unique: true, filter: "\"MemberNumber\" <> ''");

        mb.CreateTable(
            name: "MembershipRequests",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                RequestedRole = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                SuggestedMemberId = table.Column<Guid>(type: "uuid", nullable: true),
                LinkedMemberId = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                DecidedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                DecisionNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MembershipRequests", x => x.Id);
                table.ForeignKey("FK_MembershipRequests_Groups_GroupId", x => x.GroupId, "Groups", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_MembershipRequests_Users_UserId", x => x.UserId, "Users", "Id", onDelete: ReferentialAction.Cascade);
            });
        mb.CreateIndex(name: "IX_MembershipRequests_GroupId_Status", table: "MembershipRequests", columns: new[] { "GroupId", "Status" });
        mb.CreateIndex(name: "IX_MembershipRequests_UserId", table: "MembershipRequests", column: "UserId");
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropTable(name: "MembershipRequests");
        mb.DropIndex(name: "IX_GroupMembers_GroupId_MemberNumber", table: "GroupMembers");
        mb.DropColumn(name: "MemberNumberIssuedAt", table: "GroupMembers");
        mb.DropIndex(name: "IX_Groups_JoinCode", table: "Groups");
        mb.DropColumn(name: "JoinCode", table: "Groups");
        mb.DropColumn(name: "ShortCode", table: "Groups");
    }
}
