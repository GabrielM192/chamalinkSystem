using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Localization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Infrastructure.Services.Benefits;

/// <summary>
/// Arifa ndani ya mfumo. Zinaongezwa kwenye DbContext ILE ILE ya kitendo (zinahifadhiwa pamoja nacho).
/// Kila mwanachama akipata arifa ya kila msiba/sherehe/utoaji, wanachama wote wanakuwa walinzi.
/// (SMS/WhatsApp zinaweza kuongezwa baadaye juu ya jedwali hili.)
/// </summary>
public class NotificationService
{
    private readonly ApplicationDbContext _context;
    public NotificationService(ApplicationDbContext context) => _context = context;

    // Urefu wa safu za database (lazima ulingane na ApplicationDbContext) — si sheria ya fedha
    public const int TitleStringLength = 200, BodyStringLength = 1000;

    // SASISHO17: chanzo kimoja — Common.MemberAccess
    public static readonly GroupRole[] LeaderRoles = ChamaLink.Infrastructure.Services.Common.MemberAccess.LeaderRoles;

    /// <summary>Hali ambazo mwanachama bado yuko kikundini (anapata arifa, anahesabiwa, anaweza kupiga kura).</summary>
    public static readonly MemberStatus[] InGroupStatuses = ChamaLink.Infrastructure.Services.Common.MemberAccess.ActiveStatuses;

    public async Task NotifyAsync(Guid groupId, IEnumerable<Guid> recipientMemberIds, string kind, Loc title, Loc body, string? link, CancellationToken ct)
    {
        foreach (var id in recipientMemberIds.Distinct())
            _context.MemberNotifications.Add(new MemberNotification
            {
                Id = Guid.NewGuid(), GroupId = groupId, RecipientGroupMemberId = id, Kind = kind,
                Title = Trim(title.Text, TitleStringLength), Body = Trim(body.Text, BodyStringLength), Link = link,
                LocJson = LocJsonOf(title, body)
            });
        await Task.CompletedTask;
    }

    public async Task NotifyAllMembersAsync(Guid groupId, string kind, Loc title, Loc body, string? link, CancellationToken ct, params Guid[] except)
    {
        var ids = await _context.GroupMembers.Where(m => m.GroupId == groupId && InGroupStatuses.Contains(m.Status) && !except.Contains(m.Id))
            .Select(m => m.Id).ToListAsync(ct);
        await NotifyAsync(groupId, ids, kind, title, body, link, ct);
    }

    public async Task NotifyLeadersAsync(Guid groupId, string kind, Loc title, Loc body, string? link, CancellationToken ct, params GroupRole[] onlyRoles)
    {
        var roles = onlyRoles.Length > 0 ? onlyRoles : LeaderRoles;
        var ids = await _context.GroupMembers.Where(m => m.GroupId == groupId && roles.Contains(m.Role) && InGroupStatuses.Contains(m.Status))
            .Select(m => m.Id).ToListAsync(ct);
        await NotifyAsync(groupId, ids, kind, title, body, link, ct);
    }

    // Urefu wa juu wa LocJson (lazima ulingane na ApplicationDbContext) — si sheria ya fedha
    public const int LocJsonStringLength = 4000;
    private static readonly JsonSerializerOptions LocJsonOptions = new(JsonSerializerDefaults.Web);
    /// <summary>SASISHO14: kiolezo + vigeu vya kichwa na maelezo -> frontend inatafsiri (EN).</summary>
    public static string? LocJsonOf(Loc title, Loc body)
    {
        var json = JsonSerializer.Serialize(new { title = title.ToMessage(), body = body.ToMessage() }, LocJsonOptions);
        return json.Length <= LocJsonStringLength ? json : null;
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
