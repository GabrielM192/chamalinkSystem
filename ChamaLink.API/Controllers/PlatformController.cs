using ChamaLink.API.Extensions;
using ChamaLink.Domain.Entities;
using ChamaLink.Infrastructure;
using ChamaLink.Domain.Exceptions;
using ChamaLink.Infrastructure.Services.Platform;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.API.Controllers;

/// <summary>
/// SASISHO20 — API ya MSIMAMIZI WA MFUMO (/api/platform).
///
/// Tofauti na API nyingine zote, hii HAIHUSIANI na kikundi kimoja: inaona vikundi vyote
/// vilivyounganishwa. Ndiyo maana ina kinga tatu:
///
///   1. Token ya kikundi HAIWEZI kuitumia (inahitaji madai `pa=1`, yanayotolewa tu baada
///      ya neno la siri + MFA ya TOTP).
///   2. Ngazi: Support (msaada) &lt; Oversight (muhtasari wa fedha) &lt; SuperAdmin (wasimamizi,
///      break-glass). Ngazi ya chini haiwezi kuita endpoint ya juu.
///   3. HATUA KUBWA ZINAHITAJI MSIMAMIZI MWINGINE (4-eyes) — angalia PlatformApprovalService.
///
/// KANUNI: hakuna endpoint hapa inayoidhinisha malipo, kubadilisha kiasi, au kuandika deni.
/// Pesa inabaki kwa viongozi wa kikundi + Injini ya Ukweli. Jaribio la usanifu
/// (TruthArchitectureTests.Every_Write_Endpoint_Is_Truth_Or_Allowlisted_Or_Legacy_Gated)
/// linahakikisha hilo lisivunjwe.
/// </summary>
[ApiController]
[Route("api/platform")]
public class PlatformController : ControllerBase
{
    private readonly PlatformAuthService _auth;
    private readonly PlatformGroupService _groups;
    private readonly PlatformApprovalService _approvals;
    private readonly PlatformSupportService _support;
    private readonly PlatformAuditService _audit;
    private readonly ApplicationDbContext _context;

    public PlatformController(PlatformAuthService auth, PlatformGroupService groups, PlatformApprovalService approvals,
        PlatformSupportService support, PlatformAuditService audit, ApplicationDbContext context)
    {
        _auth = auth; _groups = groups; _approvals = approvals; _support = support; _audit = audit; _context = context;
    }

    private string? Ip => HttpContext.Connection.RemoteIpAddress?.ToString();

    // =================================================================
    // 1. Kuingia (hakuna [Authorize] — token ndiyo inayotafutwa)
    // =================================================================
    public sealed record LoginBody(string Email, string Password);
    public sealed record CodeBody(string Code);

    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("auth/login")]
    public async Task<IActionResult> Login([FromBody] LoginBody body, CancellationToken ct)
        => Ok(await _auth.LoginAsync(body.Email ?? string.Empty, body.Password ?? string.Empty, Ip, ct));

    /// <summary>Hatua ya kujisajili MFA — token ya hatua ya kwanza pekee inakubaliwa.</summary>
    [Authorize]
    [HttpPost("auth/mfa/enroll")]
    public async Task<IActionResult> Enroll(CancellationToken ct)
    {
        RequireStage(PlatformAuthService.EnrollStageClaim);
        var e = await _auth.EnrollAsync(User.GetUserId(), ct);
        return Ok(new
        {
            secret = e.Secret,
            uri = e.Uri,
            manualHint = e.ManualHint,
            ujumbe = "Ongeza akaunti hii kwenye Google Authenticator / Authy, kisha andika code ya tarakimu 6 kuthibitisha."
        });
    }

    [Authorize]
    [HttpPost("auth/mfa/confirm")]
    public async Task<IActionResult> ConfirmEnrollment([FromBody] CodeBody body, CancellationToken ct)
    {
        RequireStage(PlatformAuthService.EnrollStageClaim);
        return Ok(await _auth.ConfirmEnrollmentAsync(User.GetUserId(), body.Code ?? string.Empty, Ip, ct));
    }

    [Authorize]
    [EnableRateLimiting("auth")]
    [HttpPost("auth/mfa/verify")]
    public async Task<IActionResult> VerifyMfa([FromBody] CodeBody body, CancellationToken ct)
    {
        RequireStage(PlatformAuthService.MfaStageClaim);
        return Ok(await _auth.VerifyMfaAsync(User.GetUserId(), body.Code ?? string.Empty, Ip, ct));
    }

    private void RequireStage(string claim)
    {
        if (!User.HasClaim(claim, "1"))
            throw new ForbiddenException("Hatua ya kuingia si sahihi. Anza tena kwa neno la siri.");
        if (User.HasClaim(PlatformAuthService.PlatformClaim, "1"))
            throw new ForbiddenException("Tayari umeingia kikamilifu."); // token kamili haitumiki tena kwenye hatua za MFA
    }

    // =================================================================
    // 2. Mimi ni nani
    // =================================================================
    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var admin = await RequirePlatform(PlatformAdminLevel.Support, ct);
        return Ok(new
        {
            userId = admin.UserId,
            fullName = admin.User?.FullName,
            email = admin.User?.Email,
            level = admin.Level.ToString(),
            mfaEnabled = admin.MfaEnabledAt is not null,
            lastMfaVerifiedAt = admin.LastMfaVerifiedAt
        });
    }

    // =================================================================
    // 3. Vikundi vyote
    // =================================================================
    /// <summary>Orodha ya vikundi vyote + afya. Ngazi ya Support haioni kiasi cha fedha.</summary>
    [Authorize]
    [HttpGet("groups")]
    public async Task<IActionResult> Groups(CancellationToken ct)
    {
        var admin = await RequirePlatform(PlatformAdminLevel.Support, ct);
        var rows = await _groups.ListAsync(ct);
        if (admin.Level == PlatformAdminLevel.Support)
            rows = rows.Select(r => r with { EventDebt = 0m }).ToList();
        return Ok(new { rows, kikundi = rows.Count, hatari = rows.Count(r => r.Health == "Critical"), tahadhari = rows.Count(r => r.Health == "Warning") });
    }

    /// <summary>Muhtasari wa kikundi kimoja (ngazi: Oversight na juu).</summary>
    [Authorize]
    [HttpGet("groups/{groupId:guid}")]
    public async Task<IActionResult> Group(Guid groupId, CancellationToken ct)
    {
        await RequirePlatform(PlatformAdminLevel.Oversight, ct);
        return Ok(await _groups.DetailAsync(groupId, User.GetUserId(), ct));
    }

    /// <summary>Wanachama mmoja mmoja — break-glass iliyoidhinishwa pekee.</summary>
    [Authorize]
    [HttpGet("groups/{groupId:guid}/members")]
    public async Task<IActionResult> GroupMembers(Guid groupId, CancellationToken ct)
    {
        await RequirePlatform(PlatformAdminLevel.Oversight, ct);
        var rows = await _groups.MembersAsync(groupId, User.GetUserId(), ct);
        return Ok(new { rows, members = rows.Count });
    }

    public sealed record NoticeBody(string Title, string Body);

    [Authorize]
    [HttpPost("groups/{groupId:guid}/notice")]
    public async Task<IActionResult> SendNotice(Guid groupId, [FromBody] NoticeBody body, CancellationToken ct)
    {
        await RequirePlatform(PlatformAdminLevel.Support, ct);
        await _support.NotifyLeadersAsync(User.GetUserId(), groupId, body.Title ?? string.Empty, body.Body ?? string.Empty, ct);
        return Ok(new { ujumbe = "Notisi imetumwa kwa viongozi wote wa kikundi." });
    }

    public sealed record ReasonBody(string Reason);

    [Authorize]
    [HttpPost("groups/{groupId:guid}/rotate-join-code")]
    public async Task<IActionResult> RotateJoinCode(Guid groupId, [FromBody] ReasonBody body, CancellationToken ct)
    {
        await RequirePlatform(PlatformAdminLevel.Support, ct);
        var code = await _support.RotateJoinCodeAsync(User.GetUserId(), groupId, body.Reason ?? string.Empty, ct);
        return Ok(new { joinCode = code });
    }

    // =================================================================
    // 4. Msaada kwa watu (kufungua akaunti, code ya kureset)
    // =================================================================
    [Authorize]
    [HttpGet("people")]
    public async Task<IActionResult> People([FromQuery] string q, CancellationToken ct)
    {
        await RequirePlatform(PlatformAdminLevel.Support, ct);
        return Ok(new { rows = await _support.FindPeopleAsync(q, ct) });
    }

    [Authorize]
    [HttpPost("people/{userId:guid}/unlock")]
    public async Task<IActionResult> Unlock(Guid userId, [FromBody] ReasonBody body, CancellationToken ct)
    {
        await RequirePlatform(PlatformAdminLevel.Support, ct);
        var cleared = await _support.UnlockAsync(User.GetUserId(), userId, body.Reason ?? string.Empty, ct);
        return Ok(new { ujumbe = "Akaunti imefunguliwa. Mtu anaweza kuingia tena kwa neno lake la siri.", attemptsCleared = cleared });
    }

    public sealed record ResetCodeBody(Guid? GroupId, string Reason);

    [Authorize]
    [HttpPost("people/{userId:guid}/reset-code")]
    public async Task<IActionResult> IssueResetCode(Guid userId, [FromBody] ResetCodeBody body, CancellationToken ct)
    {
        await RequirePlatform(PlatformAdminLevel.Support, ct);
        var c = await _support.IssueResetCodeAsync(User.GetUserId(), userId, body.GroupId, body.Reason ?? string.Empty, ct);
        return Ok(new
        {
            code = c.Code,
            expiresAt = c.ExpiresAt,
            person = c.PersonName,
            email = c.Email,
            ujumbe = "Mpe mtu code hii kwa simu. Neno la siri jipya ANALIWEKA mwenyewe — wewe hulioni."
        });
    }

    // =================================================================
    // 5. Wasimamizi + 4-eyes
    // =================================================================
    [Authorize]
    [HttpGet("admins")]
    public async Task<IActionResult> Admins(CancellationToken ct)
    {
        await RequirePlatform(PlatformAdminLevel.SuperAdmin, ct);
        // Kupanga KABLA ya makadirio: PostgreSQL haiwezi kutafsiri `.ToString()` ya enum ndani
        // ya hoja ya OrderBy (ilijaribiwa — 500 halisi). Ngahe inapangwa kwa thamani, jina baadaye.
        var rows = await _context.PlatformAdmins.AsNoTracking()
            .OrderBy(a => a.Level)
            .ThenBy(a => a.User == null ? "" : a.User.FullName)
            .Select(a => new
            {
                id = a.Id, userId = a.UserId,
                name = a.User != null ? a.User.FullName : null,
                email = a.User != null ? a.User.Email : null,
                level = a.Level.ToString(), isActive = a.IsActive,
                mfaEnabled = a.MfaEnabledAt != null,
                createdAt = a.CreatedAt, deactivatedAt = a.DeactivatedAt
            })
            .ToListAsync(ct);
        return Ok(new { rows });
    }

    public sealed record BreakGlassBody(Guid GroupId, string Reason);

    [Authorize]
    [HttpPost("approvals/break-glass")]
    public async Task<IActionResult> RequestBreakGlass([FromBody] BreakGlassBody body, CancellationToken ct)
    {
        await RequirePlatform(PlatformAdminLevel.SuperAdmin, ct);
        var a = await _approvals.RequestBreakGlassAsync(User.GetUserId(), body.GroupId, body.Reason ?? string.Empty, ct);
        return Ok(new { approvalId = a.Id, status = a.Status.ToString(), ujumbe = "Ombi limeundwa. Msimamizi mwingine (Oversight au Super Admin) anahitaji kuidhinisha." });
    }

    public sealed record AdminRequestBody(string Action, Guid? TargetUserId, Guid? TargetPlatformAdminId, string? Level, string Reason);

    [Authorize]
    [HttpPost("approvals/admins")]
    public async Task<IActionResult> RequestAdminChange([FromBody] AdminRequestBody body, CancellationToken ct)
    {
        await RequirePlatform(PlatformAdminLevel.SuperAdmin, ct);
        if (!Enum.TryParse<PlatformApprovalAction>(body.Action, true, out var action) || !PlatformApprovalService.AdminActions.Contains(action))
            throw new ValidationException("Aina ya ombi si sahihi.");
        PlatformAdminLevel? level = null;
        if (!string.IsNullOrWhiteSpace(body.Level))
        {
            if (!Enum.TryParse<PlatformAdminLevel>(body.Level, true, out var l)) throw new ValidationException("Ngazi si sahihi.");
            level = l;
        }
        var a = await _approvals.RequestAdminAsync(User.GetUserId(), action, body.TargetUserId, body.TargetPlatformAdminId, level, body.Reason ?? string.Empty, ct);
        return Ok(new { approvalId = a.Id, status = a.Status.ToString() });
    }

    [Authorize]
    [HttpGet("approvals")]
    public async Task<IActionResult> Approvals([FromQuery] bool pendingOnly, CancellationToken ct)
    {
        await RequirePlatform(PlatformAdminLevel.Support, ct);
        return Ok(new { rows = await _approvals.ListAsync(User.GetUserId(), pendingOnly, ct) });
    }

    public sealed record DecideBody(bool Approve, string? Note);

    [Authorize]
    [HttpPost("approvals/{id:guid}/decide")]
    public async Task<IActionResult> Decide(Guid id, [FromBody] DecideBody body, CancellationToken ct)
    {
        await RequirePlatform(PlatformAdminLevel.Oversight, ct);
        var a = await _approvals.DecideAsync(User.GetUserId(), id, body.Approve, body.Note, ct);
        return Ok(new { status = a.Status.ToString(), expiresAt = a.ExpiresAt });
    }

    [Authorize]
    [HttpPost("approvals/{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        await RequirePlatform(PlatformAdminLevel.Support, ct);
        var a = await _approvals.CancelAsync(User.GetUserId(), id, ct);
        return Ok(new { status = a.Status.ToString() });
    }

    // =================================================================
    // 5b. Kikao cha msimamizi + MFA mpya (4-eyes)
    // =================================================================

    /// <summary>Kufunga kikao cha msimamizi kwenye kifaa hiki (kumbukumbu inaandikwa).</summary>
    [Authorize]
    [HttpPost("auth/logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var me = await _auth.RequireAdminAsync(User.GetUserId(), ct);
        await _audit.WriteAsync(me.Id, me.UserId, "logout", ip: Ip, ct: ct);
        await _context.SaveChangesAsync(ct);
        return Ok(new { ujumbe = "Umetoka. Funga kikao kwenye kifaa hiki." });
    }

    /// <summary>
    /// Break-glass inayotumika SASA kwa msimamizi huyu kwenye kikundi hiki (ikipatikana).
    /// UI inaonyesha muda uliobaki badala ya kufungua ombi jipya kila mara.
    /// </summary>
    [Authorize]
    [HttpGet("break-glass")]
    public async Task<IActionResult> CurrentBreakGlass([FromQuery] Guid groupId, CancellationToken ct)
    {
        var me = await RequirePlatform(PlatformAdminLevel.Oversight, ct);
        return Ok(new { active = await _approvals.HasBreakGlassAsync(me.UserId, groupId, ct) });
    }

    /// <summary>
    /// MFA mpya kwa msimamizi mwingine (simu imepotea). Msimamizi mmoja HAWEZI kufanya
    /// hivi kwa akaunti yake — 4-eyes inahitajika. Siri ya zamani inafutwa kabisa.
    /// </summary>
    [Authorize]
    [HttpPost("admins/{id:guid}/reset-mfa")]
    public async Task<IActionResult> RequestMfaReset(Guid id, [FromBody] ReasonBody body, CancellationToken ct)
    {
        var me = await RequirePlatform(PlatformAdminLevel.SuperAdmin, ct);
        if (body?.Reason is null || body.Reason.Trim().Length < 10)
            return BadRequest(new { message = "Eleza sababu (angalau herufi 10)." });
        var a = await _approvals.RequestAdminAsync(me.UserId, PlatformApprovalAction.ResetMfa, null, id, null,
            body.Reason.Trim(), ct);
        return Ok(new { approvalId = a.Id, ujumbe = "Ombi limetumwa kwa msimamizi mwingine; MFA itawashwa upya baada ya kuidhinishwa." });
    }

    // =================================================================
    // 6. Kumbukumbu
    // =================================================================
    [Authorize]
    [HttpGet("audit")]
    public async Task<IActionResult> AuditLog([FromQuery] string? action, [FromQuery] Guid? groupId,
        [FromQuery] DateTime? from, [FromQuery] int take, CancellationToken ct)
    {
        await RequirePlatform(PlatformAdminLevel.Oversight, ct);
        return Ok(new { rows = await _audit.ListAsync(action, groupId, null, from, take == 0 ? 200 : take, ct) });
    }

    // =================================================================
    private async Task<PlatformAdmin> RequirePlatform(PlatformAdminLevel min, CancellationToken ct)
    {
        if (!User.HasClaim(PlatformAuthService.PlatformClaim, "1"))
            throw new ForbiddenException("Hii ni API ya msimamizi wa mfumo. Ingia kupitia /admin.");
        var admin = await _auth.RequireLevelAsync(User.GetUserId(), min, ct);
        if (admin.MfaEnabledAt is null) throw new ForbiddenException("MFA haijawashwa kwenye akaunti yako ya usimamizi.");
        return admin;
    }
}
