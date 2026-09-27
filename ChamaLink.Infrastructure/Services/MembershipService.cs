using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Group = ChamaLink.Domain.Entities.Group;
using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Domain.Exceptions;
using ChamaLink.Infrastructure.Services.Truth;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Infrastructure.Services;

/// <summary>
/// Usajili na uanachama (AWAMU I).
///
/// Dhana mbili tofauti:
///   - MWANACHAMA (GroupMember) = rekodi ya FEDHA. Viongozi au import wanaweza kuitengeneza bila mtu kuwa na akaunti.
///   - AKAUNTI (User yenye email + neno la siri) = uwezo wa KUINGIA kwenye mfumo.
/// Mwanachama "hana akaunti" ikiwa User yake haina email wala neno la siri (hivyo ndivyo import ilivyowaingiza).
///
/// Kanuni za usalama:
///   1. Mtu anajiunga kwa kodi ya kikundi → ombi (Pending). Hakuna anayekuwa mwanachama bila idhini ya kiongozi.
///   2. Ombi la Katibu/Mtunza-hazina linaidhinishwa na MWENYEKITI pekee; la mwanachama wa kawaida, na kiongozi yeyote.
///   3. Namba ya simu inayolingana na mwanachama asiye na akaunti ni PENDEKEZO tu. Kiongozi ndiye anayechagua
///      kuunganisha wakati wa kuidhinisha. Hakuna kuunganisha kiotomatiki (kwa jina wala kwa simu).
///   4. Manaibu wanaruhusiwa (Katibu/Mtunza-hazina zaidi ya mmoja). Mwenyekiti ni mmoja tu.
///   5. Namba rasmi ya uanachama ({ShortCode}-{Mwaka}-{Namba}) haibadiliki kamwe baada ya kutolewa.
/// Kila kitendo kinaandika AuditEvent ndani ya SaveChanges ile ile.
/// </summary>
public class MembershipService
{
    private readonly ApplicationDbContext _context;
    private readonly TruthSetupService _setup;

    public MembershipService(ApplicationDbContext context, TruthSetupService setup)
    {
        _context = context;
        _setup = setup;
    }

    public static readonly GroupRole[] LeaderRoles = { GroupRole.Chairperson, GroupRole.Treasurer, GroupRole.Secretary };
    /// <summary>Nafasi ambazo mtu anaweza kuomba kupitia kodi ya kujiunga (Mwenyekiti hachaguliwi hivi).</summary>
    public static readonly GroupRole[] RequestableRoles = { GroupRole.Member, GroupRole.Secretary, GroupRole.Treasurer };

    private static readonly Regex ShortCodePattern = new("^[A-Z][A-Z0-9]{1,4}$", RegexOptions.Compiled);
    // Herufi zisizochanganya macho (hakuna 0/O, 1/I/L) — kodi inasomwa na kuandikwa na binadamu.
    private const string JoinAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int JoinSuffixLength = 5;

    public static bool HasLogin(User? u) => u is not null && !string.IsNullOrEmpty(u.PasswordHash) && !string.IsNullOrEmpty(u.Email);

    // =====================================================================
    // KIKUNDI
    // =====================================================================

    public sealed record GroupCreated(Guid GroupId, string Name, string ShortCode, string JoinCode, Guid ChairMemberId, string ChairMemberNumber);

    /// <summary>Mtu yeyote mwenye akaunti anaanzisha kikundi → anakuwa Mwenyekiti wake (Active) moja kwa moja.</summary>
    public async Task<GroupCreated> CreateGroupAsync(Guid userId, string name, string? description, GroupType type, string shortCode, CancellationToken ct = default)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException("Akaunti haipo.");
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length < 3)
            throw new ValidationException("Jina la kikundi linahitajika (angalau herufi 3).");
        var code = NormalizeShortCode(shortCode);

        var group = new Group
        {
            Id = Guid.NewGuid(), Name = name.Trim(), Description = description?.Trim() ?? string.Empty, Type = type,
            Code = "CHM-" + RandomSuffix(6), ShortCode = code, JoinCode = await NewJoinCodeAsync(code, ct)
        };
        var chair = new GroupMember
        {
            Id = Guid.NewGuid(), GroupId = group.Id, UserId = user.Id, Role = GroupRole.Chairperson,
            Status = MemberStatus.Active, JoinedAt = DateTime.UtcNow
        };
        _context.Groups.Add(group);
        _context.GroupMembers.Add(chair);
        IssueNumber(group, chair, new List<GroupMember>());
        await _context.SaveChangesAsync(ct);

        // Baada ya Mwenyekiti kuhifadhiwa, ili kumbukumbu ionyeshe nafasi na namba yake halisi
        await Audit(group.Id, chair.Id, userId, AuditAction.Created, nameof(Group), group.Id, null,
            new { group.Name, Type = type.ToString(), group.ShortCode, Chair = user.FullName, chair.MemberNumber }, group.JoinCode, "Kikundi kimeanzishwa", ct);
        await _context.SaveChangesAsync(ct);
        return new GroupCreated(group.Id, group.Name, code, group.JoinCode!, chair.Id, chair.MemberNumber);
    }

    public sealed record GroupProfile(Guid GroupId, string Name, string Type, string? ShortCode, bool ShortCodeLocked, string? JoinCode, int Members, int PendingRequests);

    public async Task<GroupProfile> GetProfileAsync(Guid groupId, CancellationToken ct = default)
    {
        var g = await _context.Groups.AsNoTracking().FirstOrDefaultAsync(x => x.Id == groupId, ct)
            ?? throw new NotFoundException("Kikundi hakipo.");
        var members = await _context.GroupMembers.CountAsync(m => m.GroupId == groupId, ct);
        var locked = await _context.GroupMembers.AnyAsync(m => m.GroupId == groupId && m.MemberNumberIssuedAt != null, ct);
        var pending = await _context.MembershipRequests.CountAsync(r => r.GroupId == groupId && r.Status == MembershipRequestStatus.Pending, ct);
        return new GroupProfile(g.Id, g.Name, g.Type.ToString(), g.ShortCode, locked, g.JoinCode, members, pending);
    }

    /// <summary>Mwenyekiti anaweka kifupi cha namba (mf. UMK). Hakibadiliki baada ya namba rasmi ya kwanza kutolewa.</summary>
    public async Task<GroupProfile> SetShortCodeAsync(Guid groupId, string shortCode, Guid actorUserId, CancellationToken ct = default)
    {
        await RequireChairAsync(actorUserId, groupId, ct);
        var g = await _context.Groups.FirstAsync(x => x.Id == groupId, ct);
        var code = NormalizeShortCode(shortCode);
        if (g.ShortCode == code) return await GetProfileAsync(groupId, ct);
        if (await _context.GroupMembers.AnyAsync(m => m.GroupId == groupId && m.MemberNumberIssuedAt != null, ct))
            throw new ConflictException($"Kifupi '{g.ShortCode}' tayari kimetumika kwenye namba rasmi za uanachama — hakiwezi kubadilishwa (namba hazibadiliki).");
        var before = new { g.ShortCode };
        g.ShortCode = code;
        await Audit(groupId, null, actorUserId, AuditAction.Updated, nameof(Group), groupId, before, new { g.ShortCode }, null, "Kifupi cha namba za uanachama", ct);
        await _context.SaveChangesAsync(ct);
        return await GetProfileAsync(groupId, ct);
    }

    /// <summary>Tengeneza kodi mpya ya kujiunga (ya zamani haitumiki tena — kwa mfano ikiwa imevuja).</summary>
    public async Task<string> RegenerateJoinCodeAsync(Guid groupId, Guid actorUserId, CancellationToken ct = default)
    {
        await RequireChairAsync(actorUserId, groupId, ct);
        var g = await _context.Groups.FirstAsync(x => x.Id == groupId, ct);
        var before = new { g.JoinCode };
        g.JoinCode = await NewJoinCodeAsync(g.ShortCode ?? "CHM", ct);
        await Audit(groupId, null, actorUserId, AuditAction.JoinCodeChanged, nameof(Group), groupId, before, new { g.JoinCode }, null,
            before.JoinCode is null ? "Kodi ya kujiunga imetengenezwa" : "Kodi ya kujiunga imebadilishwa (ya zamani haifanyi kazi tena)", ct);
        await _context.SaveChangesAsync(ct);
        return g.JoinCode;
    }

    // =====================================================================
    // KUJIUNGA (mtu mwenye akaunti)
    // =====================================================================

    public sealed record JoinPreview(Guid GroupId, string GroupName, string GroupType, bool AlreadyMember, bool HasPendingRequest);

    public async Task<JoinPreview> PreviewJoinAsync(Guid userId, string joinCode, CancellationToken ct = default)
    {
        var g = await FindByJoinCodeAsync(joinCode, ct);
        var already = await _context.GroupMembers.AnyAsync(m => m.GroupId == g.Id && m.UserId == userId, ct);
        var pending = await _context.MembershipRequests.AnyAsync(r => r.GroupId == g.Id && r.UserId == userId && r.Status == MembershipRequestStatus.Pending, ct);
        return new JoinPreview(g.Id, g.Name, g.Type.ToString(), already, pending);
    }

    public async Task<MembershipRequest> RequestToJoinAsync(Guid userId, string joinCode, GroupRole requestedRole, string? note, CancellationToken ct = default)
    {
        if (!RequestableRoles.Contains(requestedRole))
            throw new ValidationException("Unaweza kuomba nafasi ya Mwanachama, Katibu au Mtunza-hazina tu.");
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw new NotFoundException("Akaunti haipo.");
        if (!HasLogin(user)) throw new ForbiddenException("Akaunti hii haiwezi kuomba kujiunga.");
        var g = await FindByJoinCodeAsync(joinCode, ct);

        if (await _context.GroupMembers.AnyAsync(m => m.GroupId == g.Id && m.UserId == userId, ct))
            throw new ConflictException("Tayari wewe ni mwanachama wa kikundi hiki.");
        if (await _context.MembershipRequests.AnyAsync(r => r.GroupId == g.Id && r.UserId == userId && r.Status == MembershipRequestStatus.Pending, ct))
            throw new ConflictException("Tayari una ombi linalosubiri idhini ya viongozi wa kikundi hiki.");

        var req = new MembershipRequest
        {
            Id = Guid.NewGuid(), GroupId = g.Id, UserId = userId, RequestedRole = requestedRole,
            Note = Trim(note, 500), SuggestedMemberId = await SuggestUnlinkedMemberAsync(g.Id, user.PhoneNumber, ct)
        };
        _context.MembershipRequests.Add(req);
        await Audit(g.Id, null, userId, AuditAction.MembershipRequested, nameof(MembershipRequest), req.Id, null,
            new { user.FullName, user.PhoneNumber, Role = requestedRole.ToString(), req.SuggestedMemberId }, null, req.Note, ct);
        await _context.SaveChangesAsync(ct);
        return req;
    }

    public async Task CancelMyRequestAsync(Guid userId, Guid requestId, CancellationToken ct = default)
    {
        var r = await _context.MembershipRequests.FirstOrDefaultAsync(x => x.Id == requestId && x.UserId == userId, ct)
            ?? throw new NotFoundException("Ombi halipo.");
        if (r.Status != MembershipRequestStatus.Pending) throw new ConflictException("Ombi hili tayari limeshughulikiwa.");
        r.Status = MembershipRequestStatus.Cancelled;
        r.DecidedAt = DateTime.UtcNow;
        r.DecidedByUserId = userId;
        await Audit(r.GroupId, null, userId, AuditAction.Updated, nameof(MembershipRequest), r.Id, new { Status = "Pending" }, new { Status = "Cancelled" }, null, "Ombi limeondolewa na muombaji", ct);
        await _context.SaveChangesAsync(ct);
    }

    public sealed record MyRequest(Guid RequestId, Guid GroupId, string GroupName, string RequestedRole, string Status, DateTime CreatedAt, string? DecisionNote);

    public async Task<List<MyRequest>> MyRequestsAsync(Guid userId, CancellationToken ct = default)
        => await _context.MembershipRequests.AsNoTracking().Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new MyRequest(r.Id, r.GroupId, r.Group!.Name, r.RequestedRole.ToString(), r.Status.ToString(), r.CreatedAt, r.DecisionNote))
            .ToListAsync(ct);

    // =====================================================================
    // VIONGOZI: maombi
    // =====================================================================

    public sealed record SuggestedMember(Guid MemberId, string MemberNumber, string FullName, IReadOnlyList<string> Phones);
    public sealed record PendingRequest(Guid RequestId, Guid UserId, string FullName, string Email, string Phone, string RequestedRole,
        string? Note, DateTime CreatedAt, SuggestedMember? Suggested, bool YouCanDecide);

    public async Task<List<PendingRequest>> PendingRequestsAsync(Guid groupId, Guid actorUserId, CancellationToken ct = default)
    {
        var actor = await RequireLeaderAsync(actorUserId, groupId, ct);
        var reqs = await _context.MembershipRequests.AsNoTracking().Include(r => r.User)
            .Where(r => r.GroupId == groupId && r.Status == MembershipRequestStatus.Pending)
            .OrderBy(r => r.CreatedAt).ToListAsync(ct);
        var suggestedIds = reqs.Where(r => r.SuggestedMemberId != null).Select(r => r.SuggestedMemberId!.Value).ToList();
        var suggested = await _context.GroupMembers.AsNoTracking().Include(m => m.User)
            .Where(m => suggestedIds.Contains(m.Id)).ToListAsync(ct);
        var phones = await PhonesAsync(suggestedIds, ct);

        return reqs.Select(r =>
        {
            var s = suggested.FirstOrDefault(m => m.Id == r.SuggestedMemberId);
            // Pendekezo linaonyeshwa tu kama mwanachama bado hana akaunti (huenda ameshaunganishwa na ombi jingine)
            var sm = s is not null && !HasLogin(s.User)
                ? new SuggestedMember(s.Id, s.MemberNumber, s.User?.FullName ?? "", phones[s.Id].ToList()) : null;
            return new PendingRequest(r.Id, r.UserId, r.User?.FullName ?? "", r.User?.Email ?? "", r.User?.PhoneNumber ?? "",
                r.RequestedRole.ToString(), r.Note, r.CreatedAt, sm, CanDecide(actor.Role, r.RequestedRole));
        }).ToList();
    }

    /// <summary>
    /// Kiongozi anaamua ombi. Akiidhinisha:
    ///   - linkMemberId ikiwa na thamani → akaunti inaunganishwa na mwanachama ALIYEPO asiye na akaunti
    ///     (historia yake yote ya fedha inabaki: rekodi ya fedha ni ile ile, namba ile ile);
    ///   - bila linkMemberId → mwanachama MPYA anatengenezwa.
    /// Pendekezo la simu halitumiki lenyewe — kiongozi lazima alichague waziwazi (linkMemberId).
    /// </summary>
    public async Task<MembershipRequest> DecideRequestAsync(Guid groupId, Guid requestId, bool approve, Guid? linkMemberId, string? note,
        Guid actorUserId, CancellationToken ct = default)
    {
        var actor = await RequireLeaderAsync(actorUserId, groupId, ct);
        var r = await _context.MembershipRequests.Include(x => x.User).FirstOrDefaultAsync(x => x.Id == requestId && x.GroupId == groupId, ct)
            ?? throw new NotFoundException("Ombi halipo.");
        if (r.Status != MembershipRequestStatus.Pending) throw new ConflictException("Ombi hili tayari limeshughulikiwa.");
        if (r.UserId == actorUserId) throw new ForbiddenException("Huwezi kuidhinisha ombi lako mwenyewe.");
        if (!CanDecide(actor.Role, r.RequestedRole))
            throw new ForbiddenException($"Ombi la nafasi ya {RoleSw(r.RequestedRole)} linaidhinishwa na Mwenyekiti pekee.");

        var before = new { Status = r.Status.ToString() };
        r.DecidedAt = DateTime.UtcNow;
        r.DecidedByUserId = actorUserId;
        r.DecisionNote = Trim(note, 500);

        if (!approve)
        {
            if (string.IsNullOrWhiteSpace(note)) throw new ValidationException("Andika sababu ya kukataa ombi (muombaji ataiona).");
            r.Status = MembershipRequestStatus.Rejected;
            await Audit(groupId, null, actorUserId, AuditAction.GovernanceRejected, nameof(MembershipRequest), r.Id, before,
                new { Status = "Rejected", Applicant = r.User?.FullName }, null, r.DecisionNote, ct);
            await _context.SaveChangesAsync(ct);
            return r;
        }

        if (await _context.GroupMembers.AnyAsync(m => m.GroupId == groupId && m.UserId == r.UserId, ct))
            throw new ConflictException("Mtu huyu tayari ni mwanachama wa kikundi hiki.");

        var group = await _context.Groups.FirstAsync(g => g.Id == groupId, ct);
        var all = await _context.GroupMembers.Where(m => m.GroupId == groupId).ToListAsync(ct);
        GroupMember member;
        if (linkMemberId is { } lid)
        {
            member = await _context.GroupMembers.Include(m => m.User).FirstOrDefaultAsync(m => m.Id == lid && m.GroupId == groupId, ct)
                ?? throw new NotFoundException("Mwanachama wa kuunganisha hayupo kwenye kikundi hiki.");
            if (HasLogin(member.User))
                throw new ConflictException($"{member.User!.FullName} tayari ana akaunti yake — hawezi kuunganishwa na akaunti nyingine.");
            var linkBefore = new { OldUserId = member.UserId, RecordName = member.User?.FullName, member.MemberNumber, Role = member.Role.ToString() };
            member.UserId = r.UserId;
            member.Role = r.RequestedRole;
            if (member.Status == MemberStatus.New) member.Status = MemberStatus.Active;
            if (member.MemberNumberIssuedAt is null) IssueNumber(group, member, all);
            await Audit(groupId, member.Id, actorUserId, AuditAction.MemberLinkedToAccount, nameof(GroupMember), member.Id, linkBefore,
                new { NewUserId = r.UserId, Account = r.User?.FullName, member.MemberNumber, Role = member.Role.ToString(), WasSuggestedByPhone = r.SuggestedMemberId == lid },
                null, "Akaunti imeunganishwa na rekodi ya mwanachama aliyepo (historia ya fedha inabaki)", ct);
        }
        else
        {
            member = new GroupMember
            {
                Id = Guid.NewGuid(), GroupId = groupId, UserId = r.UserId, Role = r.RequestedRole,
                Status = MemberStatus.New, JoinedAt = DateTime.UtcNow
            };
            _context.GroupMembers.Add(member);
            IssueNumber(group, member, all);
            await Audit(groupId, member.Id, actorUserId, AuditAction.MemberRecordCreated, nameof(GroupMember), member.Id, null,
                new { Account = r.User?.FullName, member.MemberNumber, Role = member.Role.ToString() }, null, "Mwanachama mpya kutoka ombi la kujiunga", ct);
        }

        r.Status = MembershipRequestStatus.Approved;
        r.LinkedMemberId = member.Id;
        await Audit(groupId, member.Id, actorUserId, AuditAction.GovernanceApproved, nameof(MembershipRequest), r.Id, before,
            new { Status = "Approved", member.MemberNumber, Linked = linkMemberId is not null }, null, r.DecisionNote, ct);
        await SaveUniqueAsync(ct);

        // Mwanachama MPYA: namba aliyojisajili nayo ndiyo ya M-Koba (kiongozi ameiona kwenye ombi kabla ya kuidhinisha).
        // Inapitia njia ile ile ya ukurasa wa Namba (kulinganisha na M-Koba + audit). Haiwekwi kama tayari ni ya mtu
        // mwingine wa kikundi — hapo viongozi wanaamua wenyewe kwenye ukurasa wa Namba.
        // Rekodi ILIYOUNGANISHWA haiguswi: namba zake zilishawekwa na viongozi.
        if (linkMemberId is null && !string.IsNullOrWhiteSpace(r.User?.PhoneNumber))
        {
            string? phone = null;
            try { phone = PhoneNumbers.Normalize(r.User!.PhoneNumber); } catch (ArgumentException) { /* namba isiyosomeka: viongozi wataiweka wenyewe */ }
            var taken = phone is null || await _context.MemberPhones.AnyAsync(p => p.GroupId == groupId && p.Phone == phone, ct);
            if (!taken)
                await _setup.AddPhoneAsync(groupId, member.Id, phone!, actorUserId, "Namba ya usajili — imethibitishwa na kiongozi aliyeidhinisha ombi", ct);
        }
        return r;
    }

    // =====================================================================
    // VIONGOZI: wanachama
    // =====================================================================

    public sealed record MemberRow(Guid MemberId, string MemberNumber, bool NumberIsOfficial, string FullName, string Role, string Status,
        bool HasLogin, string? Email, IReadOnlyList<string> Phones, DateTime JoinedAt);

    public async Task<List<MemberRow>> MembersAsync(Guid groupId, Guid actorUserId, CancellationToken ct = default)
    {
        await RequireLeaderAsync(actorUserId, groupId, ct);
        return await LoadMembersAsync(groupId, ct);
    }

    private async Task<MemberRow> RowAsync(Guid groupId, Guid memberId, CancellationToken ct)
        => (await LoadMembersAsync(groupId, ct)).First(m => m.MemberId == memberId);

    private async Task<List<MemberRow>> LoadMembersAsync(Guid groupId, CancellationToken ct)
    {
        var members = await _context.GroupMembers.AsNoTracking().Include(m => m.User).Where(m => m.GroupId == groupId).ToListAsync(ct);
        var phones = await PhonesAsync(members.Select(m => m.Id).ToList(), ct);
        return members
            .Select(m => new MemberRow(m.Id, m.MemberNumber, m.MemberNumberIssuedAt != null, m.User?.FullName ?? "", m.Role.ToString(), m.Status.ToString(),
                HasLogin(m.User), HasLogin(m.User) ? m.User!.Email : null, phones[m.Id].ToList(), m.JoinedAt))
            .OrderBy(m => NumberSortKey(m.MemberNumber)).ThenBy(m => m.FullName)
            .ToList();
    }

    /// <summary>Kiongozi anaongeza mwanachama (rekodi ya fedha) bila akaunti — mf. mwanachama wa zamani ambaye hana simu janja.</summary>
    public async Task<MemberRow> AddMemberRecordAsync(Guid groupId, string fullName, string? phone, DateTime? joinedAt, Guid actorUserId, CancellationToken ct = default)
    {
        await RequireLeaderAsync(actorUserId, groupId, ct);
        if (string.IsNullOrWhiteSpace(fullName) || fullName.Trim().Length < 2) throw new ValidationException("Jina la mwanachama linahitajika.");
        string? normalized = null;
        if (!string.IsNullOrWhiteSpace(phone))
        {
            normalized = PhoneNumbers.Normalize(phone);
            var owner = await _context.MemberPhones.Where(p => p.GroupId == groupId && p.Phone == normalized)
                .Select(p => p.GroupMemberId).FirstOrDefaultAsync(ct);
            if (owner != Guid.Empty)
                throw new ConflictException($"Namba {normalized} tayari imewekwa kwa mwanachama mwingine wa kikundi hiki.");
        }

        var group = await _context.Groups.FirstAsync(g => g.Id == groupId, ct);
        var all = await _context.GroupMembers.Where(m => m.GroupId == groupId).ToListAsync(ct);
        var profile = new User { Id = Guid.NewGuid(), FullName = fullName.Trim(), PhoneNumber = normalized ?? string.Empty };
        var member = new GroupMember
        {
            Id = Guid.NewGuid(), GroupId = groupId, UserId = profile.Id, Role = GroupRole.Member, Status = MemberStatus.New,
            JoinedAt = joinedAt is { } j ? DateTime.SpecifyKind(j, DateTimeKind.Utc) : DateTime.UtcNow
        };
        _context.Users.Add(profile);
        _context.GroupMembers.Add(member);
        IssueNumber(group, member, all);
        await Audit(groupId, member.Id, actorUserId, AuditAction.MemberRecordCreated, nameof(GroupMember), member.Id, null,
            new { profile.FullName, Phone = normalized, member.MemberNumber, member.JoinedAt }, null, "Kiongozi ameongeza mwanachama (bila akaunti)", ct);
        await SaveUniqueAsync(ct);

        // Namba ya simu inapitia njia ile ile ya ukurasa wa Namba (inalinganisha na M-Koba + audit yake)
        if (normalized is not null) await _setup.AddPhoneAsync(groupId, member.Id, normalized, actorUserId, "Imewekwa wakati wa kuongeza mwanachama", ct);
        return await RowAsync(groupId, member.Id, ct);
    }

    /// <summary>
    /// Mwenyekiti anabadilisha nafasi. Manaibu wanaruhusiwa (Katibu/Mtunza-hazina zaidi ya mmoja).
    /// Nafasi ya uongozi inahitaji mwenye akaunti. Kumpa mtu mwingine Uenyekiti = kukabidhi: Mwenyekiti wa sasa anakuwa Mwanachama.
    /// </summary>
    public async Task<MemberRow> ChangeRoleAsync(Guid groupId, Guid memberId, GroupRole newRole, Guid actorUserId, CancellationToken ct = default)
    {
        var chair = await RequireChairAsync(actorUserId, groupId, ct);
        var target = await _context.GroupMembers.Include(m => m.User).FirstOrDefaultAsync(m => m.Id == memberId && m.GroupId == groupId, ct)
            ?? throw new NotFoundException("Mwanachama hayupo kwenye kikundi hiki.");
        if (target.Role == newRole) return await RowAsync(groupId, memberId, ct);
        if (target.Id == chair.Id)
            throw new ValidationException("Mwenyekiti hawezi kujibadilishia nafasi. Kukabidhi, mpe mwanachama mwingine nafasi ya Mwenyekiti.");
        if (newRole != GroupRole.Member && !HasLogin(target.User))
            throw new ValidationException($"{target.User?.FullName} hana akaunti ya kuingia. Nafasi ya uongozi inahitaji akaunti — kwanza ajisajili na kuunganishwa.");

        var oldRole = target.Role;
        var before = new { Target = target.User?.FullName, Role = oldRole.ToString() };
        target.Role = newRole;
        string reason = $"Nafasi imebadilishwa: {RoleSw(oldRole)} → {RoleSw(newRole)}";
        if (newRole == GroupRole.Chairperson)
        {
            var me = await _context.GroupMembers.FirstAsync(m => m.Id == chair.Id, ct);
            me.Role = GroupRole.Member;
            reason = "Uenyekiti umekabidhiwa (Mwenyekiti aliyetangulia sasa ni Mwanachama)";
        }
        await Audit(groupId, target.Id, actorUserId, AuditAction.MemberRoleChanged, nameof(GroupMember), target.Id, before,
            new { Target = target.User?.FullName, Role = newRole.ToString() }, null, reason, ct);
        await _context.SaveChangesAsync(ct);
        return await RowAsync(groupId, memberId, ct);
    }

    public sealed record IssueResult(int Issued, List<(string FullName, string OldNumber, string NewNumber)> Changes);

    /// <summary>
    /// Toa namba rasmi kwa wanachama ambao bado hawana (mara moja kabla ya kuanza kutumia mfumo).
    /// Namba za zamani za TARAKIMU (mf. "14" kwenye Excel ya viongozi) zinahifadhiwa: 14 → UMK-2026-014.
    /// Namba rasmi zilizokwisha tolewa HAZIGUSWI kamwe.
    /// </summary>
    public async Task<IssueResult> IssueMissingNumbersAsync(Guid groupId, Guid actorUserId, CancellationToken ct = default)
    {
        await RequireChairAsync(actorUserId, groupId, ct);
        var group = await _context.Groups.FirstAsync(g => g.Id == groupId, ct);
        if (string.IsNullOrEmpty(group.ShortCode)) throw new ValidationException("Weka kwanza kifupi cha kikundi (mf. UMK) kabla ya kutoa namba.");
        var all = await _context.GroupMembers.Include(m => m.User).Where(m => m.GroupId == groupId).ToListAsync(ct);
        // Namba za zamani za tarakimu TUPU (za Excel ya viongozi) zinapewa kipaumbele ili zihifadhiwe
        var pending = all.Where(m => m.MemberNumberIssuedAt is null)
            .OrderBy(m => int.TryParse(m.MemberNumber, out var n) && n > 0 ? 0 : 1)
            .ThenBy(m => NumberSortKey(m.MemberNumber)).ThenBy(m => m.JoinedAt).ThenBy(m => m.User?.FullName).ToList();
        var changes = new List<(string, string, string)>();
        foreach (var m in pending)
        {
            var old = m.MemberNumber;
            IssueNumber(group, m, all);
            changes.Add((m.User?.FullName ?? "", old, m.MemberNumber));
            await Audit(groupId, m.Id, actorUserId, AuditAction.MemberNumberIssued, nameof(GroupMember), m.Id,
                new { OldNumber = old }, new { m.MemberNumber }, m.MemberNumber, "Namba rasmi ya uanachama imetolewa (haibadiliki)", ct);
        }
        await SaveUniqueAsync(ct);
        return new IssueResult(changes.Count, changes);
    }

    // =====================================================================
    // Msaada
    // =====================================================================

    private static bool CanDecide(GroupRole actor, GroupRole requested)
        => requested == GroupRole.Member ? LeaderRoles.Contains(actor) : actor == GroupRole.Chairperson;

    public static string RoleSw(GroupRole r) => r switch
    {
        GroupRole.Chairperson => "Mwenyekiti",
        GroupRole.Treasurer => "Mtunza-hazina",
        GroupRole.Secretary => "Katibu",
        GroupRole.MemberRepresentative => "Mwakilishi wa wanachama",
        _ => "Mwanachama"
    };

    private async Task<GroupMember> RequireLeaderAsync(Guid userId, Guid groupId, CancellationToken ct)
    {
        var m = await _context.GroupMembers.AsNoTracking().FirstOrDefaultAsync(x => x.GroupId == groupId && x.UserId == userId, ct)
            ?? throw new ForbiddenException("Wewe si mwanachama wa kikundi hiki.");
        if (!LeaderRoles.Contains(m.Role)) throw new ForbiddenException("Kazi hii ni ya viongozi wa kikundi.");
        return m;
    }

    private async Task<GroupMember> RequireChairAsync(Guid userId, Guid groupId, CancellationToken ct)
    {
        var m = await RequireLeaderAsync(userId, groupId, ct);
        if (m.Role != GroupRole.Chairperson) throw new ForbiddenException("Kazi hii ni ya Mwenyekiti pekee.");
        return m;
    }

    private async Task<Group> FindByJoinCodeAsync(string joinCode, CancellationToken ct)
    {
        var code = (joinCode ?? "").Trim().ToUpperInvariant();
        if (code.Length == 0) throw new ValidationException("Weka kodi ya kujiunga.");
        return await _context.Groups.AsNoTracking().FirstOrDefaultAsync(g => g.JoinCode == code, ct)
            ?? throw new NotFoundException("Kodi hii ya kujiunga haipo. Hakiki na Mwenyekiti wa kikundi.");
    }

    /// <summary>Mwanachama asiye na akaunti ambaye namba yake ya simu (ya M-Koba au ya rekodi) ni sawa na ya muombaji.</summary>
    private async Task<Guid?> SuggestUnlinkedMemberAsync(Guid groupId, string rawPhone, CancellationToken ct)
    {
        string phone;
        try { phone = PhoneNumbers.Normalize(rawPhone); }
        catch (ArgumentException) { return null; }

        var candidates = await _context.MemberPhones.AsNoTracking()
            .Where(p => p.GroupId == groupId && p.Phone == phone).Select(p => p.GroupMemberId).ToListAsync(ct);
        var members = await _context.GroupMembers.AsNoTracking().Include(m => m.User).Where(m => m.GroupId == groupId).ToListAsync(ct);
        candidates.AddRange(members.Where(m => SamePhone(m.User?.PhoneNumber, phone)).Select(m => m.Id));
        var unlinked = candidates.Distinct().Where(id => members.Any(m => m.Id == id && !HasLogin(m.User))).ToList();
        return unlinked.Count == 1 ? unlinked[0] : null; // zaidi ya mmoja = haijulikani → kiongozi achague mwenyewe
    }

    private static bool SamePhone(string? raw, string normalized)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;
        try { return PhoneNumbers.Normalize(raw) == normalized; }
        catch (ArgumentException) { return false; }
    }

    private async Task<ILookup<Guid, string>> PhonesAsync(List<Guid> memberIds, CancellationToken ct)
        => (await _context.MemberPhones.AsNoTracking().Where(p => memberIds.Contains(p.GroupMemberId))
            .Select(p => new { p.GroupMemberId, p.Phone }).ToListAsync(ct)).ToLookup(p => p.GroupMemberId, p => p.Phone);

    /// <summary>
    /// Toa namba rasmi kwa mwanachama mmoja (kama kikundi kina kifupi). Namba ya zamani ya tarakimu inahifadhiwa
    /// kama haijachukuliwa; vinginevyo namba inayofuata baada ya kubwa zaidi iliyopo.
    /// </summary>
    private static void IssueNumber(Group group, GroupMember member, List<GroupMember> all)
    {
        if (member.MemberNumberIssuedAt is not null || string.IsNullOrEmpty(group.ShortCode)) return;
        var year = TruthLedgerService.NowEat().Year;
        var prefix = group.ShortCode + "-";
        var official = new Regex("^" + Regex.Escape(group.ShortCode) + @"-\d{4}-(\d+)$");
        var taken = all.Where(m => m.Id != member.Id && m.MemberNumberIssuedAt != null)
            .Select(m => official.Match(m.MemberNumber)).Where(x => x.Success).Select(x => int.Parse(x.Groups[1].Value)).ToHashSet();
        // Namba za zamani za tarakimu ambazo bado hazijatolewa rasmi zimehifadhiwa kwa wenyewe (14 → UMK-2026-014 baadaye)
        foreach (var o in all.Where(m => m.Id != member.Id && m.MemberNumberIssuedAt == null))
            if (int.TryParse(o.MemberNumber, out var reserved) && reserved > 0) taken.Add(reserved);

        int seq = int.TryParse(member.MemberNumber, out var legacy) && legacy > 0 && !taken.Contains(legacy)
            ? legacy
            : (taken.Count == 0 ? 0 : taken.Max()) + 1;
        // Namba ya zamani inaweza kuwa ndogo kuliko zilizopo; hakikisha haijachukuliwa na wengine wanaopewa sasa
        while (taken.Contains(seq)) seq++;

        member.MemberNumber = $"{prefix}{year}-{seq:000}";
        member.MemberNumberIssuedAt = DateTime.UtcNow;
        if (!all.Contains(member)) all.Add(member);
    }

    private static (int, string) NumberSortKey(string n)
    {
        var digits = Regex.Match(n ?? "", @"(\d+)$");
        return (digits.Success ? int.Parse(digits.Groups[1].Value) : int.MaxValue, n ?? "");
    }

    private static string NormalizeShortCode(string? raw)
    {
        var code = (raw ?? "").Trim().ToUpperInvariant();
        if (!ShortCodePattern.IsMatch(code))
            throw new ValidationException("Kifupi cha kikundi kiwe herufi 2 hadi 5 (herufi ya kwanza ni herufi, mf. UMK).");
        return code;
    }

    private async Task<string> NewJoinCodeAsync(string prefix, CancellationToken ct)
    {
        for (var i = 0; i < 20; i++)
        {
            var code = $"{prefix}-{RandomSuffix(JoinSuffixLength)}";
            if (!await _context.Groups.AnyAsync(g => g.JoinCode == code, ct)) return code;
        }
        throw new ConflictException("Imeshindwa kutengeneza kodi ya kipekee. Jaribu tena.");
    }

    private static string RandomSuffix(int length)
        => string.Concat(Enumerable.Range(0, length).Select(_ => JoinAlphabet[RandomNumberGenerator.GetInt32(JoinAlphabet.Length)]));

    private static string? Trim(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : (s.Trim().Length > max ? s.Trim()[..max] : s.Trim());

    private async Task SaveUniqueAsync(CancellationToken ct)
    {
        try { await _context.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            throw new ConflictException("Kiongozi mwingine amebadilisha wanachama wakati huo huo (namba au akaunti imejirudia). Pakia upya ukurasa na ujaribu tena.");
        }
    }

    private Task Audit(Guid groupId, Guid? memberId, Guid actorUserId, AuditAction action, string entity, Guid entityId,
        object? before, object after, string? reference, string? reason, CancellationToken ct)
        => AuditTrail.AddAsync(_context, groupId, memberId, actorUserId, action, entity, entityId, before, after, reference, reason, ct);
}
