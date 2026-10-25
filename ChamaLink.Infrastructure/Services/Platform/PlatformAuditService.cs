using System.Text.Json;
using ChamaLink.Domain.Entities;
using ChamaLink.Infrastructure.Services.Truth;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Infrastructure.Services.Platform;

/// <summary>
/// SASISHO20 — kumbukumbu za msimamizi wa mfumo.
///
/// Kanuni: HAKUNA UPDATE, HAKUNA DELETE. Jedwali ni append-only (trigger ya DB
/// inakataa UPDATE/DELETE kama ilivyo kwa AuditEvents za kikundi). Kila kitendo
/// cha msimamizi kinachoigusа kikundi kinaandikwa PIA kwenye kumbukumbu ya kikundi
/// (AuditEvents) ili wanachama waone — hakuna "msimamizi asiyeonekana".
/// </summary>
public class PlatformAuditService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // Vikomo vya maandishi (urefu wa safu/simu) — SI kiasi cha fedha.
    private const int DetailJsonMaxChars = 2000;
    private const int MaxAuditRows = 1000;
    private readonly ApplicationDbContext _context;

    public PlatformAuditService(ApplicationDbContext context) => _context = context;

    public async Task WriteAsync(Guid? platformAdminId, Guid? userId, string action, Guid? groupId = null,
        Guid? subjectId = null, string? reason = null, object? detail = null, string? ip = null, CancellationToken ct = default)
    {
        _context.PlatformAuditEvents.Add(new PlatformAuditEvent
        {
            Id = Guid.NewGuid(),
            PlatformAdminId = platformAdminId,
            UserId = userId,
            Action = action,
            GroupId = groupId,
            SubjectId = subjectId,
            Reason = Trim(reason, 500),
            DetailJson = detail is null ? null : Trim(JsonSerializer.Serialize(detail, Json), DetailJsonMaxChars),
            Ip = Trim(ip, 64),
            At = DateTime.UtcNow
        });
        await Task.CompletedTask;
    }

    /// <summary>
    /// Kumbukumbu ya kikundi: msimamizi wa jukwaa amegusa kikundi hiki. Wanachama
    /// wote wanaiona kwenye Ukweli wa Kikundi → Kumbukumbu.
    /// </summary>
    public static async Task WriteGroupVisibleAsync(ApplicationDbContext context, Guid groupId, Guid actorUserId,
        AuditAction action, Guid subjectId, object detail, string reason, CancellationToken ct)
        => await AuditTrail.AddAsync(context, groupId, null, actorUserId, action,
            nameof(PlatformAuditEvent), subjectId, null, detail, "Msimamizi wa mfumo", Trim(reason, 500)!, ct);

    public sealed record AuditRow(Guid Id, string Action, Guid? GroupId, string? GroupName, Guid? SubjectId,
        string? ActorName, Guid? ActorUserId, string? Reason, string? DetailJson, string? Ip, DateTime At);

    public async Task<List<AuditRow>> ListAsync(string? action = null, Guid? groupId = null, Guid? adminId = null,
        DateTime? from = null, int take = 200, CancellationToken ct = default)
    {
        var q = _context.PlatformAuditEvents.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(action)) q = q.Where(x => x.Action == action);
        if (groupId is { } g) q = q.Where(x => x.GroupId == g);
        if (adminId is { } a) q = q.Where(x => x.PlatformAdminId == a);
        if (from is { } f) q = q.Where(x => x.At >= f);

        var rows = await q.OrderByDescending(x => x.At).Take(Math.Clamp(take, 1, MaxAuditRows)).ToListAsync(ct);
        var adminIds = rows.Where(r => r.PlatformAdminId != null).Select(r => r.PlatformAdminId!.Value).Distinct().ToList();
        var names = await _context.PlatformAdmins.AsNoTracking().Include(a => a.User)
            .Where(a => adminIds.Contains(a.Id))
            .Select(a => new { a.Id, Name = a.User != null ? a.User.FullName : null })
            .ToListAsync(ct);
        var groupIds = rows.Where(r => r.GroupId != null).Select(r => r.GroupId!.Value).Distinct().ToList();
        var groups = await _context.Groups.AsNoTracking().Where(g => groupIds.Contains(g.Id))
            .Select(g => new { g.Id, g.Name }).ToListAsync(ct);

        return rows.Select(r => new AuditRow(
            r.Id, r.Action, r.GroupId,
            r.GroupId is { } gid ? groups.FirstOrDefault(x => x.Id == gid)?.Name : null,
            r.SubjectId,
            r.PlatformAdminId is { } pid ? names.FirstOrDefault(x => x.Id == pid)?.Name : null,
            r.UserId, r.Reason, r.DetailJson, r.Ip, r.At)).ToList();
    }

    private static string? Trim(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..(max - 1)] + "…";
}
