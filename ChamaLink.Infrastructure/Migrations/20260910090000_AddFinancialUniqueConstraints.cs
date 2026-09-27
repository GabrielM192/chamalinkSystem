using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChamaLink.Infrastructure.Migrations
{
    /// <inheritdoc />
    // SECURITY/INTEGRITY FIX (audit 12.1 / 12.2 / 12.3 + 3.3 / 6.2):
    // Account, Debt and Fine previously only had non-unique indexes on
    // the columns that were supposed to be unique per member, so the
    // database itself did nothing to stop duplicate accounts (two
    // Savings accounts for one member) or duplicate financial records
    // (two Debts/Fines for the same member+period) if the application
    // code's own "check, then create" logic ever lost a race - which it
    // could, since none of those checks ran inside a transaction.
    //
    // IMPORTANT: if duplicate rows already exist in the database from
    // before this fix, this migration will fail to apply (a unique
    // index cannot be created over data that violates it). Run this
    // first, before applying the migration, to find any:
    //
    //   SELECT "GroupMemberId", "Type", COUNT(*)
    //   FROM "Accounts" GROUP BY "GroupMemberId", "Type" HAVING COUNT(*) > 1;
    //
    //   SELECT "GroupMemberId", "Period", COUNT(*)
    //   FROM "Debts" GROUP BY "GroupMemberId", "Period" HAVING COUNT(*) > 1;
    //
    //   SELECT "GroupMemberId", "Period", "ReasonType", COUNT(*)
    //   FROM "Fines" WHERE "Period" IS NOT NULL
    //   GROUP BY "GroupMemberId", "Period", "ReasonType" HAVING COUNT(*) > 1;
    //
    //   SELECT "GroupMemberId", "GroupEventId", "ReasonType", COUNT(*)
    //   FROM "Fines" WHERE "GroupEventId" IS NOT NULL
    //   GROUP BY "GroupMemberId", "GroupEventId", "ReasonType" HAVING COUNT(*) > 1;
    //
    // If any of these return rows, decide which duplicate to keep
    // (usually the earliest one, after moving any AmountPaid/AmountCleared
    // from the duplicate onto the one you keep) and delete the rest
    // before running `dotnet ef database update`.
    public partial class AddFinancialUniqueConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // --- Account(GroupMemberId, Type) ---
            migrationBuilder.DropIndex(
                name: "IX_Accounts_GroupMemberId",
                table: "Accounts");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_GroupMemberId_Type",
                table: "Accounts",
                columns: new[] { "GroupMemberId", "Type" },
                unique: true);

            // --- Debt(GroupMemberId, Period) ---
            migrationBuilder.DropIndex(
                name: "IX_Debts_GroupMemberId_Period",
                table: "Debts");

            migrationBuilder.CreateIndex(
                name: "IX_Debts_GroupMemberId_Period",
                table: "Debts",
                columns: new[] { "GroupMemberId", "Period" },
                unique: true);

            // --- Fine: split into two partial unique indexes, since
            // Period and GroupEventId are each null for the "other"
            // fine family and Postgres never treats NULL = NULL. ---
            migrationBuilder.DropIndex(
                name: "IX_Fines_GroupMemberId_Period",
                table: "Fines");

            migrationBuilder.CreateIndex(
                name: "IX_Fines_GroupMemberId_Period_ReasonType",
                table: "Fines",
                columns: new[] { "GroupMemberId", "Period", "ReasonType" },
                unique: true,
                filter: "\"Period\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Fines_GroupMemberId_GroupEventId_ReasonType",
                table: "Fines",
                columns: new[] { "GroupMemberId", "GroupEventId", "ReasonType" },
                unique: true,
                filter: "\"GroupEventId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // --- Fine ---
            migrationBuilder.DropIndex(
                name: "IX_Fines_GroupMemberId_GroupEventId_ReasonType",
                table: "Fines");

            migrationBuilder.DropIndex(
                name: "IX_Fines_GroupMemberId_Period_ReasonType",
                table: "Fines");

            migrationBuilder.CreateIndex(
                name: "IX_Fines_GroupMemberId_Period",
                table: "Fines",
                columns: new[] { "GroupMemberId", "Period" });

            // --- Debt ---
            migrationBuilder.DropIndex(
                name: "IX_Debts_GroupMemberId_Period",
                table: "Debts");

            migrationBuilder.CreateIndex(
                name: "IX_Debts_GroupMemberId_Period",
                table: "Debts",
                columns: new[] { "GroupMemberId", "Period" });

            // --- Account ---
            migrationBuilder.DropIndex(
                name: "IX_Accounts_GroupMemberId_Type",
                table: "Accounts");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_GroupMemberId",
                table: "Accounts",
                column: "GroupMemberId");
        }
    }
}
