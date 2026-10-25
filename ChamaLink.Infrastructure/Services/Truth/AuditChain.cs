using ChamaLink.Domain.Localization;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ChamaLink.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ChamaLink.Infrastructure.Services.Truth;

/// <summary>
/// MNYORORO WA HASH wa kumbukumbu za ukaguzi (AuditEvents), kwa kila kikundi.
///
///   Hash(n) = SHA-256( PrevHash | Id | GroupId | MemberId | ActorId | Action | EntityType | EntityId |
///                      Timestamp | BeforeJson | AfterJson | SourceReference | Reason )
///
/// Tukio linaandikwa kwanza (insert-only) na "kufungwa" baadaye kwa mpangilio wa kuingizwa (Timestamp, Id) —
/// kwa hiyo maombi mawili ya wakati mmoja hayagongani. Mtu akibadilisha/kufuta tukio lililofungwa moja kwa moja
/// kwenye database, VerifyAsync inaonyesha mahali mnyororo ulipovunjika. PostgreSQL pia ina trigger inayozuia
/// UPDATE/DELETE ya AuditEvents (isipokuwa kujaza hash mara moja).
/// </summary>
public static class AuditChain
{
    public sealed record VerifyResult(Guid GroupId, bool Ok, int Sealed, int Unsealed, long? BrokenAtIndex, Guid? BrokenEventId, string Message,
        string? Tpl = null, string[]? TArgs = null)
    {
        public VerifyResult(Guid groupId, bool ok, int sealedCount, int unsealed, long? brokenAtIndex, Guid? brokenEventId, Loc message)
            : this(groupId, ok, sealedCount, unsealed, brokenAtIndex, brokenEventId, message.Text, message.Template, message.Args.ToArray()) { }
        /// <summary>Ujumbe kama Loc (kiolezo + hoja) kwa tafsiri upande wa frontend.</summary>
        public Loc ToLoc() => Loc.Of(Tpl ?? Message, TArgs ?? Array.Empty<string>(), Message);
    }

    public static string ComputeHash(AuditEvent e, string? prevHash)
    {
        var sb = new StringBuilder();
        void F(string? v) => sb.Append(v ?? "∅").Append('|');
        F(prevHash);
        F(e.Id.ToString("N"));
        F(e.GroupId.ToString("N"));
        F(e.MemberId?.ToString("N"));
        F(e.ActorId.ToString("N"));
        F(e.Action.ToString());
        F(e.EntityType);
        F(e.EntityId.ToString("N"));
        // Postgres inahifadhi hadi microsecond: tunakata hapo ili hash iwe ile ile kabla na baada ya kusoma DB
        F(new DateTime(e.Timestamp.Ticks - e.Timestamp.Ticks % 10, DateTimeKind.Unspecified).ToString("yyyy-MM-ddTHH:mm:ss.ffffff", CultureInfo.InvariantCulture));
        F(e.BeforeJson);
        F(e.AfterJson);
        F(e.SourceReference);
        F(e.Reason);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }

    /// <summary>Funga matukio yote ambayo bado hayajafungwa ya kikundi (kwa mpangilio wa kuingizwa). Inarudisha idadi.</summary>
    public static async Task<int> SealAsync(ApplicationDbContext context, Guid groupId, CancellationToken ct = default)
    {
        var relational = context.Database.IsRelational();
        await using var tx = relational && context.Database.CurrentTransaction is null ? await context.Database.BeginTransactionAsync(ct) : null;
        if (relational)
        {
            // Mfungaji mmoja tu kwa kikundi kwa wakati mmoja
            var key = ChamaLink.Infrastructure.Services.Common.DbTx.Key(groupId, ChamaLink.Infrastructure.Services.Common.DbTx.AuditChainSeal); // SASISHO17: ufunguo mmoja wa bit 64
            await context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", new object[] { key }, ct);
        }
        var last = await context.AuditEvents.AsNoTracking().Where(a => a.GroupId == groupId && a.ChainIndex != null)
            .OrderByDescending(a => a.ChainIndex).Select(a => new { a.ChainIndex, a.Hash }).FirstOrDefaultAsync(ct);
        var pending = await context.AuditEvents.Where(a => a.GroupId == groupId && a.ChainIndex == null)
            .OrderBy(a => a.Timestamp).ThenBy(a => a.Id).ToListAsync(ct);
        if (pending.Count == 0) { if (tx is not null) await tx.CommitAsync(ct); return 0; }
        var index = last?.ChainIndex ?? 0;
        var prev = last?.Hash;
        foreach (var e in pending)
        {
            index++;
            e.ChainIndex = index;
            e.PrevHash = prev;
            e.Hash = ComputeHash(e, prev);
            prev = e.Hash;
        }
        await context.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return pending.Count;
    }

    public static async Task<VerifyResult> VerifyAsync(ApplicationDbContext context, Guid groupId, CancellationToken ct = default)
    {
        var rows = await context.AuditEvents.AsNoTracking().Where(a => a.GroupId == groupId && a.ChainIndex != null)
            .OrderBy(a => a.ChainIndex).ToListAsync(ct);
        var unsealed = await context.AuditEvents.CountAsync(a => a.GroupId == groupId && a.ChainIndex == null, ct);
        string? prev = null;
        long expected = 0;
        foreach (var e in rows)
        {
            expected++;
            if (e.ChainIndex != expected)
                return new VerifyResult(groupId, false, rows.Count, unsealed, expected, e.Id, (Loc)$"Tukio namba {expected} kwenye mnyororo halipo (limefutwa?). Lililopo ni {e.ChainIndex}.");
            if (e.PrevHash != prev)
                return new VerifyResult(groupId, false, rows.Count, unsealed, e.ChainIndex, e.Id, (Loc)$"Tukio {e.ChainIndex}: kiungo cha tukio lililotangulia hakilingani (mpangilio umeharibiwa).");
            if (ComputeHash(e, prev) != e.Hash)
                return new VerifyResult(groupId, false, rows.Count, unsealed, e.ChainIndex, e.Id,
                    (Loc)$"Tukio {e.ChainIndex} ({e.Action}, {e.Timestamp:dd/MM/yyyy HH:mm}) limebadilishwa baada ya kuandikwa (hash hailingani).");
            prev = e.Hash;
        }
        return new VerifyResult(groupId, true, rows.Count, unsealed, null, null,
            rows.Count == 0 ? (Loc)"Hakuna kumbukumbu zilizofungwa bado." : (Loc)$"Mnyororo uko salama: kumbukumbu {rows.Count} zimehakikiwa.");
    }
}

/// <summary>Inafunga kumbukumbu mpya za ukaguzi mara kwa mara (Audit:SealIntervalSeconds; si sheria ya fedha).</summary>
public sealed class AuditSealingBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<AuditSealingBackgroundService> _log;
    private readonly TimeSpan _interval;
    private const int DefaultIntervalSeconds = 60;

    public AuditSealingBackgroundService(IServiceScopeFactory scopes, ILogger<AuditSealingBackgroundService> log, Microsoft.Extensions.Configuration.IConfiguration config)
    {
        _scopes = scopes;
        _log = log;
        var s = int.TryParse(config["Audit:SealIntervalSeconds"], out var v) && v > 0 ? v : DefaultIntervalSeconds;
        _interval = TimeSpan.FromSeconds(s);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var groups = await db.AuditEvents.AsNoTracking().Where(a => a.ChainIndex == null).Select(a => a.GroupId).Distinct().ToListAsync(stoppingToken);
                foreach (var g in groups)
                {
                    db.ChangeTracker.Clear();
                    await AuditChain.SealAsync(db, g, stoppingToken);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _log.LogWarning(ex, "Kufunga kumbukumbu za ukaguzi kumeshindikana; itajaribu tena."); }
            try { await Task.Delay(_interval, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }
}
