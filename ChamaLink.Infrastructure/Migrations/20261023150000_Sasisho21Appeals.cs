using System;
using ChamaLink.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChamaLink.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261023150000_Sasisho21Appeals")]
    /// <summary>
    /// SASISHO21 (Sehemu ya 3) — RUfAA.
    ///
    /// Mwanachama anapinga uamuzi unaomgusa (kubadilishwa hali/kufukuzwa,
    /// kukataliwa dai la malipo, faini). Viongozi WAWILI wanahitajika kuamua;
    /// mmoja hawezi, na aliyeomba hapigani kura yake.
    ///
    /// Rufaa haizidishi wala haipunguzi pesa: inabadilisha UAMUZI tu.
    ///
    /// Sera mpya (TruthGroupSettings):
    ///   AppealsEnabled          (default true)
    ///   AppealWindowDays        (siku za kuwasilisha rufaa; 0 = hakuna kikomo)
    ///   AppealApprovalsRequired (viongozi wanaohitajika; kamwe chini ya 2)
    /// </summary>
    public partial class Sasisho21Appeals : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MemberAppeals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubjectSummary = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1500)", maxLength: 1500, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RequiredApprovals = table.Column<int>(type: "integer", nullable: false),
                    DecisionWindowDays = table.Column<int>(type: "integer", nullable: false),
                    AppellantUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FiledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DecidedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DecisionNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    EffectSummary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemberAppeals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MemberAppeals_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MemberAppeals_GroupMembers_GroupMemberId",
                        column: x => x.GroupMemberId,
                        principalTable: "GroupMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MemberAppealVotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppealId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Approve = table.Column<bool>(type: "boolean", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemberAppealVotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MemberAppealVotes_MemberAppeals_AppealId",
                        column: x => x.AppealId,
                        principalTable: "MemberAppeals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MemberAppeals_GroupId_Status",
                table: "MemberAppeals",
                columns: new[] { "GroupId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MemberAppeals_GroupId_GroupMemberId_Status",
                table: "MemberAppeals",
                columns: new[] { "GroupId", "GroupMemberId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MemberAppeals_GroupMemberId",
                table: "MemberAppeals",
                column: "GroupMemberId");

            // Kiongozi mmoja = kura MOJA kwa rufaa.
            migrationBuilder.CreateIndex(
                name: "IX_MemberAppealVotes_AppealId_UserId",
                table: "MemberAppealVotes",
                columns: new[] { "AppealId", "UserId" },
                unique: true);

            // ---- Sera za rufaa ----
            migrationBuilder.AddColumn<bool>(
                name: "AppealsEnabled",
                table: "TruthGroupSettings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "AppealWindowDays",
                table: "TruthGroupSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AppealApprovalsRequired",
                table: "TruthGroupSettings",
                type: "integer",
                nullable: false,
                defaultValue: 2);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AppealApprovalsRequired", table: "TruthGroupSettings");
            migrationBuilder.DropColumn(name: "AppealWindowDays", table: "TruthGroupSettings");
            migrationBuilder.DropColumn(name: "AppealsEnabled", table: "TruthGroupSettings");

            migrationBuilder.DropTable(name: "MemberAppealVotes");
            migrationBuilder.DropTable(name: "MemberAppeals");
        }
    }
}
