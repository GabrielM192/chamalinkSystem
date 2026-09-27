using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChamaLink.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCompositeUniqueIndexToGroupMember : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GroupMembers_GroupId_MemberNumber",
                table: "GroupMembers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_GroupMembers_GroupId_MemberNumber",
                table: "GroupMembers",
                columns: new[] { "GroupId", "MemberNumber" },
                unique: true);
        }
    }
}
