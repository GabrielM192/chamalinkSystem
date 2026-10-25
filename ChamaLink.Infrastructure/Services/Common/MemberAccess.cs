using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Infrastructure.Services.Common;

/// <summary>
/// SASISHO17 (P0-1, NEW-4): CHANZO KIMOJA cha "nani anaruhusiwa" kwa kila huduma.
///  • Uanachama HAI tu (New/Active/Warning/EligibleForExpulsion) unaruhusu kitendo chochote kwenye kikundi.
///    Aliyesimamishwa/aliyetoka/aliyefariki/aliyehifadhiwa hana ruhusa yoyote (isipokuwa kusoma taarifa YAKE — njia maalum).
///  • Kiongozi = nafasi ya uongozi + uanachama hai + kipindi cha kusubiri kimekwisha (LeaderPowersFrom).
///    Kiongozi aliye kwenye kipindi cha kusubiri anachukuliwa kama Mwanachama kwa ruhusa zote (EffectiveRole).
/// Kila huduma (Truth, Benefits, Membership, Loans, MemberStatus, ripoti) inaita helper hizi — hakuna huduma
/// inayoandika ukaguzi wake wa uanachama. Majaribio: MemberAccessTests + phase9 (kila endpoint × kila hali isiyo hai = 403).
/// </summary>
public static class MemberAccess
{
    public static readonly GroupRole[] LeaderRoles = { GroupRole.Chairperson, GroupRole.Treasurer, GroupRole.Secretary };

    /// <summary>Hali ambazo mwanachama bado yuko kikundini (ana ruhusa, anapata arifa, anahesabiwa, anapiga kura).</summary>
    public static readonly MemberStatus[] ActiveStatuses =
        { MemberStatus.New, MemberStatus.Active, MemberStatus.Warning, MemberStatus.EligibleForExpulsion };

    public static bool IsActive(GroupMember m) => ActiveStatuses.Contains(m.Status);
    public static bool IsActive(MemberStatus s) => ActiveStatuses.Contains(s);

    public static bool InCoolingOff(GroupMember m, DateTime utcNow) =>
        LeaderRoles.Contains(m.Role) && m.LeaderPowersFrom is { } from && from > utcNow;

    /// <summary>Nafasi inayotumika kwa RUHUSA: kiongozi aliye kwenye kipindi cha kusubiri = Mwanachama; asiye hai = Mwanachama.</summary>
    public static GroupRole EffectiveRole(GroupMember m, DateTime? utcNow = null)
    {
        if (!IsActive(m)) return GroupRole.Member;
        return InCoolingOff(m, utcNow ?? DateTime.UtcNow) ? GroupRole.Member : m.Role;
    }

    public static bool IsEffectiveLeader(GroupMember m, DateTime? utcNow = null) => LeaderRoles.Contains(EffectiveRole(m, utcNow));

    public const string NotMemberMessage = "Wewe si mwanachama wa kikundi hiki.";
    public const string InactiveMessage = "Uanachama wako kwenye kikundi hiki si hai (umesimamishwa, umetoka au umehifadhiwa) — huna ruhusa ya kitendo hiki.";

    public static ForbiddenException CoolingOff(GroupMember m) =>
        new($"Nguvu zako za uongozi zinaanza {m.LeaderPowersFrom:dd/MM/yyyy HH:mm} (UTC) — kipindi cha kusubiri cha sera ya kikundi. Kwa sasa una ruhusa za mwanachama tu.");

    /// <summary>Rekodi ya uanachama (hali yoyote) — kwa njia za kusoma taarifa YAKO mwenyewe tu.</summary>
    public static Task<GroupMember?> FindAsync(DbContext ctx, Guid groupId, Guid userId, CancellationToken ct = default, bool tracking = true)
    {
        var q = ctx.Set<GroupMember>().Where(x => x.GroupId == groupId && x.UserId == userId);
        return (tracking ? q : q.AsNoTracking()).FirstOrDefaultAsync(ct);
    }

    /// <summary>Uanachama HAI au 403.</summary>
    public static async Task<GroupMember> RequireActiveAsync(DbContext ctx, Guid groupId, Guid userId, CancellationToken ct = default, bool tracking = true)
    {
        var m = await FindAsync(ctx, groupId, userId, ct, tracking) ?? throw new ForbiddenException(NotMemberMessage);
        if (!IsActive(m)) throw new ForbiddenException(InactiveMessage);
        return m;
    }

    /// <summary>Kiongozi mwenye nguvu kamili (hai + nje ya kipindi cha kusubiri) au 403.</summary>
    public static async Task<GroupMember> RequireEffectiveLeaderAsync(DbContext ctx, Guid groupId, Guid userId, CancellationToken ct = default,
        string? message = null, bool tracking = true)
    {
        var m = await RequireActiveAsync(ctx, groupId, userId, ct, tracking);
        if (!LeaderRoles.Contains(m.Role)) throw new ForbiddenException(message ?? "Kitendo hiki ni cha viongozi (Mwenyekiti, Mtunza-hazina, Katibu).");
        if (InCoolingOff(m, DateTime.UtcNow)) throw CoolingOff(m);
        return m;
    }

    /// <summary>Nafasi mojawapo kati ya <paramref name="roles"/> kwa EffectiveRole.</summary>
    public static GroupMember EnsureRole(GroupMember m, IReadOnlyCollection<GroupRole> roles, string message)
    {
        if (roles.Contains(EffectiveRole(m))) return m;
        if (roles.Contains(m.Role) && InCoolingOff(m, DateTime.UtcNow)) throw CoolingOff(m);
        throw new ForbiddenException(message);
    }
}

/// <summary>
/// SASISHO17 (P0-5): namba za mwanachama ZINAZOTUMIKA siku fulani (MemberPhone.Covers). Kabla ya hapa namba iliyofungwa
/// (mf. ya Fauzia baada ya 30/06) bado ilikubaliwa kama "namba ya mnufaika" wakati wa kuthibitisha utoaji. Chanzo kimoja
/// kwa mafao, mikopo na maonyesho.
/// </summary>
public static class MemberPhonesAt
{
    public static async Task<List<string>> ListAsync(ApplicationDbContext ctx, Guid groupId, Guid memberId, DateTime atEat, CancellationToken ct)
        => (await ctx.MemberPhones.AsNoTracking()
                .Where(p => p.GroupId == groupId && p.GroupMemberId == memberId && p.Status == ChamaLink.Domain.Entities.TruthApprovalStatus.Approved)
                .OrderByDescending(p => p.ValidFrom).ToListAsync(ct))
            .Where(p => p.Covers(atEat)).Select(p => p.Phone).ToList();
}
