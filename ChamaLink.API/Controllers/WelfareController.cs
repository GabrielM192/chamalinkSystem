using ChamaLink.API.Filters;
using ChamaLink.API.Extensions;
using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Infrastructure;
using ChamaLink.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChamaLink.API.Controllers;

[LegacyEndpoint("Michango ya msiba/sherehe inaingia kupitia M-Koba; makato ya JoinFee: /api/truth/member/{memberId}/welfare-deduction")]
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class WelfareController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly AccountResolverService _accountResolver;
    private readonly GroupAuthorizationService _groupAuth;

    public WelfareController(
        ApplicationDbContext context,
        AccountResolverService accountResolver,
        GroupAuthorizationService groupAuth)
    {
        _context = context;
        _accountResolver = accountResolver;
        _groupAuth = groupAuth;
    }

    [HttpPost("topup")]
    public async Task<IActionResult> TopUpWelfare([FromBody] WelfareTopUpDto dto)
    {
        // BUG FIX: this used to trust an AccountId sent straight from the
        // client, with no check that it exists or belongs to this group or
        // this member. Now we take GroupId + UserId instead, look up the
        // real GroupMember, and get/create their welfare account ourselves.
        //
        // SECURITY FIX (audit Stage 1 #4): dto.UserId used to be trusted
        // straight from the request body as "who is topping up" - any
        // authenticated caller could top up welfare on behalf of (i.e.
        // attributed to) any other member, and there was no check that the
        // caller held any leadership role at all. The actor is now always
        // the authenticated caller (from the JWT), resolved to their own
        // GroupMember row in this group, and must actually be a Treasurer
        // or Chairperson - the same two roles trusted to move money in
        // LoanService/WithdrawalService.

        var actorUserId = User.GetUserId();
        await _groupAuth.RequireRoleAsync(actorUserId, dto.GroupId, GroupRole.Treasurer, GroupRole.Chairperson);

        var groupMember = await _accountResolver.GetGroupMemberAsync(dto.GroupId, dto.TargetUserId);
        var welfareAccount = await _accountResolver.GetOrCreateAccountAsync(
        groupMember.Id, AccountType.SocialFund);

        var ledgerEntry = new LedgerEntry
        {
        Id = Guid.NewGuid(),
        GroupId = dto.GroupId,
        AccountId = welfareAccount.Id,
        UserId = dto.TargetUserId,
        Amount = dto.Amount,
        Type = TransactionType.WelfareTopUp,
        Description = "Kujaza salio la Mfuko wa Ustawi (Welfare)",
        CreatedAt = DateTime.UtcNow
        };

        _context.LedgerEntries.Add(ledgerEntry);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Salio la Welfare limeongezwa kikamilifu.", entryId = ledgerEntry.Id });

    }
}

// AccountId removed from here on purpose - the server works it out itself now,
// it should never be trusted from the client directly. UserId (the actor
// doing the topping-up) is likewise no longer here - it comes from the JWT.
// TargetUserId is kept as an explicit field, since a leader legitimately
// tops up welfare *for* another member, not only for themselves.
public record WelfareTopUpDto(Guid GroupId, Guid TargetUserId, decimal Amount);