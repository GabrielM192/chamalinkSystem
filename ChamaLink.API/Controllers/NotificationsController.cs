using ChamaLink.API.Extensions;
using ChamaLink.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.API.Controllers;

/// <summary>Arifa za mwanachama aliyeingia (kengele). Mtu anaona arifa ZAKE tu.</summary>
[Authorize]
[ApiController]
[Route("api/notifications")]
public class NotificationsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    public NotificationsController(ApplicationDbContext context) => _context = context;

    private IQueryable<Guid> MyMemberIds(Guid? groupId)
    {
        var uid = User.GetUserId();
        return _context.GroupMembers.Where(m => m.UserId == uid && (groupId == null || m.GroupId == groupId)).Select(m => m.Id);
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? groupId, [FromQuery] bool unreadOnly = false, [FromQuery] int take = 30, CancellationToken ct = default)
    {
        var ids = MyMemberIds(groupId);
        var q = _context.MemberNotifications.AsNoTracking().Where(n => ids.Contains(n.RecipientGroupMemberId));
        var unread = await q.CountAsync(n => n.ReadAt == null, ct);
        if (unreadOnly) q = q.Where(n => n.ReadAt == null);
        var items = await q.OrderByDescending(n => n.CreatedAt).Take(Math.Clamp(take, 1, 100))
            .Select(n => new { n.Id, n.GroupId, n.Kind, n.Title, n.Body, n.LocJson, n.Link, n.CreatedAt, n.ReadAt }).ToListAsync(ct);
        return Ok(new { unread, items });
    }

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> Read(Guid id, CancellationToken ct)
    {
        var ids = MyMemberIds(null);
        var n = await _context.MemberNotifications.FirstOrDefaultAsync(x => x.Id == id && ids.Contains(x.RecipientGroupMemberId), ct);
        if (n is null) return NotFound();
        n.ReadAt ??= DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> ReadAll([FromQuery] Guid? groupId, CancellationToken ct)
    {
        var ids = MyMemberIds(groupId);
        var list = await _context.MemberNotifications.Where(x => ids.Contains(x.RecipientGroupMemberId) && x.ReadAt == null).ToListAsync(ct);
        foreach (var n in list) n.ReadAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        return NoContent();
    }
}
