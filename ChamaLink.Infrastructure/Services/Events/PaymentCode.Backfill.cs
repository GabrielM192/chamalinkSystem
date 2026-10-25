using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Infrastructure.Services.Events;

/// <summary>
/// SASISHO21 — kujaza kodi za malipo kwa wajibu wa zamani (kabla ya toleo hili)
/// na kuzitolea mwanachama kodi yake.
/// </summary>
public static class PaymentCodeBackfill
{
    /// <summary>
    /// Hujaza PaymentCode kwa wajibu wote wa kikundi ambao hauna kodi bado.
    /// Inarudisha idadi iliyojazwa. Ni idempotent — kuitisha tena hairudishi kitu.
    /// </summary>
    public static async Task<int> RunAsync(ApplicationDbContext context, Guid groupId, CancellationToken ct = default)
    {
        var shortCode = await context.Groups.AsNoTracking()
            .Where(g => g.Id == groupId).Select(g => g.ShortCode).FirstOrDefaultAsync(ct);

        var missing = await context.EventObligations
            .Where(o => o.GroupId == groupId && o.PaymentCode == null)
            .ToListAsync(ct);
        if (missing.Count == 0) return 0;

        var caseNumbers = await context.BenefitCases.AsNoTracking()
            .Where(c => c.GroupId == groupId)
            .Select(c => new { c.Id, c.CaseNumber })
            .ToDictionaryAsync(x => x.Id, x => x.CaseNumber, ct);

        var memberNumbers = await context.GroupMembers.AsNoTracking()
            .Where(m => m.GroupId == groupId)
            .Select(m => new { m.Id, m.MemberNumber })
            .ToDictionaryAsync(x => x.Id, x => x.MemberNumber, ct);

        // Kodi zinazotumika tayari (kuepuka mgongano kwenye index ya kipekee).
        var taken = (await context.EventObligations.AsNoTracking()
                .Where(o => o.GroupId == groupId && o.PaymentCode != null)
                .Select(o => o.PaymentCode!)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var filled = 0;
        foreach (var o in missing)
        {
            var code = PaymentCode.Build(shortCode,
                caseNumbers.TryGetValue(o.BenefitCaseId, out var cn) ? cn : null,
                memberNumbers.TryGetValue(o.GroupMemberId, out var mn) ? mn : null);

            // Mgongano (mf. MemberNumber inaisha na tarakimu zile zile) → ongeza herufi ya mwisho.
            if (taken.Contains(code))
            {
                var i = 2;
                while (taken.Contains($"{code[..Math.Min(code.Length, PaymentCode.MaxLength - 1)]}{i}") && i < 99) i++;
                code = $"{code[..Math.Min(code.Length, PaymentCode.MaxLength - 1)]}{i}";
            }

            o.PaymentCode = code;
            taken.Add(code);
            filled++;
        }

        if (filled > 0) await context.SaveChangesAsync(ct);
        return filled;
    }
}
