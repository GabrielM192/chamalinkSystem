using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ChamaLink.Infrastructure.Services.Common;

/// <summary>
/// SASISHO15: mipaka ya transaction iliyo wazi + kufuli za kikundi (PostgreSQL advisory locks).
/// Kufuli inazuia maombi mawili ya wakati mmoja (mf. kufungua kesi mbili za tukio lilelile, au namba ya kesi
/// inayofanana) kupita ukaguzi wote kabla yoyote haijahifadhiwa. Kwenye InMemory (tests) inakuwa no-op.
/// </summary>
public static class DbTx
{
    public const int BenefitCaseOpen = 0x3A99;
    public const int LoanApplicationOpen = 0x3A9A;
    public const int LoanDisbursement = 0x3A9B;
    /// <summary>Maamuzi ya maker-checker (idhini/kukataa) ya kikundi — yanapangwa mstari mmoja ili maombi mawili ya wakati mmoja yasiidhinishe mara mbili.</summary>
    public const int Decision = 0x3A9C;
    /// <summary>SASISHO17 (P0-3): KILA ahadi ya pesa (idhini ya kesi, kuanzisha malipo, kukagua upya pesa, idhini ya mkopo,
    /// utoaji wa mkopo) inapita kufuli hii MOJA ya kikundi — mbili haziwezi kuona salio lile lile kwa wakati mmoja.</summary>
    public const int CashCommitment = 0x3A9D;
    /// <summary>SASISHO17: rasimu za PDF ya M-Koba (kupakia/kuidhinisha) zinapangwa mstari.</summary>
    public const int StatementImport = 0x3A9E;
    /// <summary>Mfungaji mmoja wa mnyororo wa ukaguzi kwa kikundi.</summary>
    public const int AuditChainSeal = 0x3A9F;
    /// <summary>SASISHO17: maombi ya vyeo na kura zake.</summary>
    public const int RoleChange = 0x3AA0;

    /// <summary>Anza transaction kama hakuna iliyopo (null = tayari iko ndani ya transaction, au si relational).</summary>
    public static async Task<IDbContextTransaction?> BeginAsync(DbContext ctx, CancellationToken ct = default)
        => ctx.Database.IsRelational() && ctx.Database.CurrentTransaction is null ? await ctx.Database.BeginTransactionAsync(ct) : null;

    /// <summary>
    /// SASISHO17 (P1-10): ufunguo wa bit 64 wa (eneo, kikundi) — SHA-256 ya GUID nzima + eneo. Zamani: bit 32 za kwanza za GUID
    /// (vikundi viwili vingeweza kusubiriana). Thabiti kati ya michakato (si string.GetHashCode).
    /// </summary>
    public static long Key(Guid groupId, int scope)
    {
        Span<byte> buf = stackalloc byte[20];
        groupId.TryWriteBytes(buf);
        BitConverter.TryWriteBytes(buf[16..], scope);
        Span<byte> h = stackalloc byte[32];
        System.Security.Cryptography.SHA256.HashData(buf, h);
        return BitConverter.ToInt64(h[..8]);
    }

    /// <summary>Kufuli ya (kikundi, eneo) inayoachiliwa transaction ikiisha. Lazima iitwe ndani ya transaction.</summary>
    public static async Task LockAsync(DbContext ctx, Guid groupId, int scope, CancellationToken ct = default)
    {
        if (!ctx.Database.IsRelational()) return;
        if (ctx.Database.CurrentTransaction is null)
            throw new InvalidOperationException("DbTx.LockAsync lazima iitwe ndani ya transaction (kufuli ya xact).");
        await ctx.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", new object[] { Key(groupId, scope) }, ct);
    }

    /// <summary>
    /// Endesha <paramref name="body"/> ndani ya transaction yenye kufuli ya (kikundi, eneo). Ombi la pili la wakati mmoja
    /// linasubiri la kwanza likamilike, kisha linasoma hali mpya (mf. "Ombi tayari ni Approved" → 409) badala ya kuidhinisha tena.
    /// SASISHO17: kama tayari kuna transaction (wito wa ndani), kufuli bado inachukuliwa ndani ya transaction hiyo.
    /// </summary>
    public static async Task<T> SerializedAsync<T>(DbContext ctx, Guid groupId, int scope, Func<Task<T>> body, CancellationToken ct = default)
    {
        await using var tx = await BeginAsync(ctx, ct);
        if (ctx.Database.IsRelational() && ctx.Database.CurrentTransaction is not null) await LockAsync(ctx, groupId, scope, ct);
        var result = await body();
        await CommitAsync(tx, ct);
        return result;
    }

    /// <summary>Kama SerializedAsync lakini kwa kufuli kadhaa (zinachukuliwa kwa mpangilio uleule kila mara — hakuna deadlock).</summary>
    public static async Task<T> SerializedAsync<T>(DbContext ctx, Guid groupId, int[] scopes, Func<Task<T>> body, CancellationToken ct = default)
    {
        await using var tx = await BeginAsync(ctx, ct);
        if (ctx.Database.IsRelational() && ctx.Database.CurrentTransaction is not null)
            foreach (var sc in scopes.Distinct().OrderBy(x => x)) await LockAsync(ctx, groupId, sc, ct);
        var result = await body();
        await CommitAsync(tx, ct);
        return result;
    }

    public static async Task CommitAsync(IDbContextTransaction? tx, CancellationToken ct = default)
    {
        if (tx is not null) await tx.CommitAsync(ct);
    }
}

/// <summary>
/// SASISHO15: rejea moja ya utoaji wa M-Koba inaeleza jambo MOJA tu — kesi ya mafao, utoaji wa mkopo,
/// au maelezo ya matumizi mengine. Chanzo kimoja cha ukweli kwa kila utoaji.
/// </summary>
public static class WithdrawalRegistry
{
    public sealed record Use(string Reference, string Kind, string Label, bool Final, Guid Id);

    public static async Task<List<Use>> UsesAsync(ApplicationDbContext ctx, Guid groupId, CancellationToken ct = default)
    {
        var list = new List<Use>();
        list.AddRange((await ctx.BenefitCases.AsNoTracking().Where(c => c.GroupId == groupId && c.MkobaReference != null)
                .Select(c => new { c.MkobaReference, c.CaseNumber, c.Status, c.Id }).ToListAsync(ct))
            .Select(c => new Use(c.MkobaReference!, "Benefit", c.CaseNumber, c.Status == Domain.Entities.BenefitCaseStatus.Paid, c.Id)));
        list.AddRange((await ctx.LoanApplications.AsNoTracking().Where(a => a.GroupId == groupId && a.MkobaReference != null)
                .Select(a => new { a.MkobaReference, a.ApplicationNumber, a.Status, a.Id }).ToListAsync(ct))
            .Select(a => new Use(a.MkobaReference!, "Loan", a.ApplicationNumber, a.Status == Domain.Entities.LoanApplicationStatus.Disbursed, a.Id)));
        var appRefs = list.Where(u => u.Kind == "Loan").Select(u => u.Reference).ToHashSet();
        list.AddRange((await ctx.Loans.AsNoTracking().Where(l => l.GroupId == groupId && l.DisbursementReference != null)
                .Select(l => new { l.DisbursementReference, l.Id }).ToListAsync(ct))
            .Where(l => !appRefs.Contains(l.DisbursementReference!))
            .Select(l => new Use(l.DisbursementReference!, "Loan", "Mkopo", true, l.Id)));
        list.AddRange((await ctx.MkobaWithdrawalExplanations.AsNoTracking().Where(e => e.GroupId == groupId && e.Status != Domain.Entities.ProposalStatus.Rejected)
                .Select(e => new { e.MkobaReference, e.Status, e.Id }).ToListAsync(ct))
            .Select(e => new Use(e.MkobaReference, "Explanation", "Maelezo ya matumizi", e.Status == Domain.Entities.ProposalStatus.Approved, e.Id)));
        return list;
    }

    /// <summary>Rejea imeshatumika na kitu kingine? (exceptId = kitu kinachothibitishwa sasa)</summary>
    public static async Task<Use?> UsedByOtherAsync(ApplicationDbContext ctx, Guid groupId, string reference, Guid? exceptId, CancellationToken ct = default)
        => (await UsesAsync(ctx, groupId, ct)).FirstOrDefault(u => u.Reference == reference && u.Id != exceptId);
}
