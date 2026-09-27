using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using ChamaLink.Domain.Exceptions;

namespace ChamaLink.Infrastructure.Services;

// IMPORTANT: This is the ONLY place in the whole app that should decide
// what a LedgerEntry.AccountId is.
//
// Before this fix, different parts of the code used three different
// (wrong) values as "AccountId":
//   1. GroupMember.Id was used directly (EventService, background job).
//   2. "The first Account row in the whole database" was used
//      (MkobaImportController, ReportsController) - this mixed up
//      money between different members and different groups.
//   3. A raw AccountId sent by the client, with no checks (WelfareController).
//
// Because of that, entries written by different modules could not be
// found by GetMemberStatementAsync, which only looks at REAL Account IDs
// that belong to that specific member. So imported transactions, welfare
// deductions and fines were invisible on a member's statement.
//
// From now on, every service must call GetOrCreateAccountAsync() below to
// get a real, member-specific Account before writing a LedgerEntry.
public class AccountResolverService
{
    private readonly ApplicationDbContext _context;

    public AccountResolverService(ApplicationDbContext context)
    {
        _context = context;
    }

    // Finds the Account for this exact member + account type (Savings,
    // Loan, Fine, SocialFund). If it does not exist yet, creates it.
    public async Task<Account> GetOrCreateAccountAsync(Guid groupMemberId, AccountType type)
    {
        var account = await _context.Accounts
            .FirstOrDefaultAsync(a => a.GroupMemberId == groupMemberId && a.Type == type);

        if (account != null)
            return account;

        account = new Account
        {
            Id = Guid.NewGuid(),
            GroupMemberId = groupMemberId,
            Type = type
        };
        _context.Accounts.Add(account);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // RACE FIX (audit 3.3, paired with the new unique index on
            // Account(GroupMemberId, Type)): two concurrent callers can
            // both fail to find an existing account and both reach this
            // point. Only one insert can win now that the database
            // enforces uniqueness - the loser used to silently create a
            // second, orphaned account; now it fails at SaveChangesAsync
            // instead. Rather than let that surface as a raw 500 to
            // whichever request happened to lose the race, drop our own
            // failed insert and fetch the row the winner actually created.
            _context.Entry(account).State = EntityState.Detached;

            account = await _context.Accounts
                .FirstOrDefaultAsync(a => a.GroupMemberId == groupMemberId && a.Type == type);

            // Extremely unlikely (e.g. the row was deleted again between
            // our failed insert and this re-fetch) - if we still can't
            // find it, the original DbUpdateException is more useful to
            // the caller than a confusing NullReferenceException.
            if (account == null)
                throw;
        }

        return account;
    }

    // Finds the GroupMember row for a given group + user.
    // Throws a friendly error if the person is not actually a member of
    // that group (this also stops data from one group leaking into another).
    public async Task<GroupMember> GetGroupMemberAsync(Guid groupId, Guid userId)
    {
        return await _context.GroupMembers
            .FirstOrDefaultAsync(gm => gm.GroupId == groupId && gm.UserId == userId)
            ?? throw new NotFoundException("Mwanachama hajapatikana kwenye kikundi hiki.");
    }
}
