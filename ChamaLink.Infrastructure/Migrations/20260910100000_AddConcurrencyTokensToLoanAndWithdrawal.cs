using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChamaLink.Infrastructure.Migrations
{
    /// <inheritdoc />
    // INTEGRITY FIX (audit 8.4: "Loan repayment ina concurrency risk" +
    // the equivalent race in WithdrawalService.DecideAsync). Loan and
    // Withdrawal now use PostgreSQL's built-in "xmin" system column as an
    // optimistic concurrency token (see ApplicationDbContext.cs -
    // UseXminAsConcurrencyToken). Every PostgreSQL row already has an
    // xmin column that changes on every UPDATE, so there is no new
    // column to create and nothing to backfill - this migration exists
    // only to record the change in EF's model snapshot/history, not to
    // alter the database schema.
    public partial class AddConcurrencyTokensToLoanAndWithdrawal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty - see class remarks above.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty - see class remarks above.
        }
    }
}
