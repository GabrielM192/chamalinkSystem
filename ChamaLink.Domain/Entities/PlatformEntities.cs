namespace ChamaLink.Domain.Entities;

// ============================================================================
// SASISHO20 — MSIMAMIZI WA MFUMO (Platform Admin)
//
// Hii ni nafasi ya JUMLA (platform-wide), si nafasi ya kikundi (GroupRole).
// Kanuni tatu zinazoongoza muundo huu:
//
//   1. MSIMAMIZI HAWEZI KUGUSA PESA. Hakuna endpoint ya jukwaa inayoidhinisha
//      malipo, kubadilisha kiasi, au kuandika deni. Pesa inabaki kwa viongozi
//      wa kikundi + Injini ya Ukweli. Jaribio la usanifu (TruthArchitectureTests)
//      linahakikisha jambo hili lisivunjwe baadaye.
//
//   2. KILA KITENDO KINAANDIKWA — na kikundi kikiona. PlatformAuditEvents ni
//      append-only (trigger ya DB inakataa UPDATE/DELETE).
//
//   3. HATARI KUBWA = WATU WAWILI (4-eyes). Kuona taarifa za fedha za mtu
//      mmoja mmoja (break-glass), kuongeza/kuondoa msimamizi — vyote vinahitaji
//      msimamizi MWINGINE kuidhinisha.
// ============================================================================

/// <summary>
/// Ngazi za msimamizi. Kila ngazi inaruhusu ZAIDI ya iliyo chini yake, lakini
/// hakuna ngazi yoyote inayoruhusiwa kugusa fedha.
/// </summary>
public enum PlatformAdminLevel
{
    /// <summary>Msaada: kufungua akaunti iliyofungwa, kutoa code ya kureset, kubadilisha join code, kutuma notisi.</summary>
    Support = 1,
    /// <summary>Uangalizi: Support + muhtasari wa fedha wa kikundi (jumla pekee) + kuidhinisha break-glass.</summary>
    Oversight = 2,
    /// <summary>Msimamizi mkuu: Oversight + kuongeza/kuondoa wasimamizi (kwa idhini ya mwenzake) + kuomba break-glass.</summary>
    SuperAdmin = 3,
}

/// <summary>
/// Aina za maamuzi yanayohitaji msimamizi wa pili. Jedwali MOJA linaendesha
/// michakato yote ya 4-eyes ya jukwaa.
/// </summary>
public enum PlatformApprovalAction
{
    /// <summary>Kuona taarifa kamili za kikundi (wanachama mmoja mmoja) kwa saa 24.</summary>
    BreakGlass = 1,
    /// <summary>Kumpa mtumiaji nafasi ya msimamizi.</summary>
    GrantAdmin = 2,
    /// <summary>Kubadilisha ngazi ya msimamizi.</summary>
    ChangeAdminLevel = 3,
    /// <summary>Kuondoa nafasi ya msimamizi.</summary>
    DeactivateAdmin = 4,
    /// <summary>Kuwasha upya MFA ya msimamizi (simu imepotea) — 4-eyes, msimamizi mwenyewe hawezi.</summary>
    ResetMfa = 5,
}

public enum PlatformApprovalStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    /// <summary>Muda wa kutumia umepita (break-glass: saa 24 baada ya idhini).</summary>
    Expired = 4,
    /// <summary>Aliyeomba ameghairi.</summary>
    Cancelled = 5,
}

/// <summary>
/// Msimamizi wa jukwaa. Akaunti yenyewe ni <see cref="User"/> ya kawaida (email + neno la siri);
/// nafasi hii inaongeza uwezo, MFA ya lazima, na kumbukumbu ya nani alimpa.
/// </summary>
public class PlatformAdmin
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public PlatformAdminLevel Level { get; set; } = PlatformAdminLevel.Support;
    public bool IsActive { get; set; } = true;

    /// <summary>Siri ya TOTP ikiwa imesimbwa (AES-GCM). Haiwachi server kamwe.</summary>
    public string? TotpSecretProtected { get; set; }
    /// <summary>Siri ya TOTP wakati wa kujisajili (bado haijathibitishwa kwa code).</summary>
    public string? PendingTotpSecretProtected { get; set; }
    public DateTime? MfaEnabledAt { get; set; }
    public DateTime? LastMfaVerifiedAt { get; set; }

    public Guid? CreatedByPlatformAdminId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeactivatedAt { get; set; }

    public User? User { get; set; }
}

/// <summary>
/// Kumbukumbu za jukwaa (append-only). Haziwezi kufutwa wala kubadilishwa —
/// trigger ya DB inakataa. Kikundi kinaweza kuona msimamizi aliyekigusua.
/// </summary>
public class PlatformAuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? PlatformAdminId { get; set; }
    public Guid? UserId { get; set; }
    /// <summary>Kitendo: login, mfa_failed, unlock, reset_code, rotate_join_code, notice, break_glass_requested, ...</summary>
    public string Action { get; set; } = string.Empty;
    /// <summary>Kikundi kinachohusika (kama kipo) — ili kikundi kiweze kuona kumbukumbu zake.</summary>
    public Guid? GroupId { get; set; }
    /// <summary>Kitu kilichoguswa (mtumiaji, mwanachama, ombi la break-glass…).</summary>
    public Guid? SubjectId { get; set; }
    public string? Reason { get; set; }
    /// <summary>Maelezo ya ziada (JSON) — HAYAANDIKI kiasi cha fedha cha mtu mmoja.</summary>
    public string? DetailJson { get; set; }
    public string? Ip { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Ombi la hatua kubwa linalohitaji idhini ya msimamizi MWINGINE. Mmoja hawezi kuidhinisha ombi lake.
/// </summary>
public class PlatformApproval
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public PlatformApprovalAction Action { get; set; }
    public PlatformApprovalStatus Status { get; set; } = PlatformApprovalStatus.Pending;

    public Guid RequestedByPlatformAdminId { get; set; }
    public Guid? DecidedByPlatformAdminId { get; set; }

    /// <summary>Kikundi lengwa (break-glass).</summary>
    public Guid? GroupId { get; set; }
    /// <summary>Msimamizi lengwa (GrantAdmin / ChangeAdminLevel / DeactivateAdmin).</summary>
    public Guid? TargetPlatformAdminId { get; set; }
    /// <summary>Mtumiaji lengwa wakati wa kumpa nafasi (kabla ya PlatformAdmin kuundwa).</summary>
    public Guid? TargetUserId { get; set; }
    /// <summary>Data ya ombi (mf. {"level":"Oversight"}). HAIWEZI kuwa na kiasi cha fedha.</summary>
    public string? PayloadJson { get; set; }

    public string Reason { get; set; } = string.Empty;
    public string? DecisionNote { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DecidedAt { get; set; }
    /// <summary>Break-glass: mwisho wa ufikiaji baada ya idhini (saa 24).</summary>
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>
/// Code ya mara moja ya kureset neno la siri. Msimamizi HAONI neno la siri jipya —
/// anatoa code, kiongozi anajiwekea neno lake. Code huhifadhiwa kwa HASH pekee.
/// </summary>
public class PlatformPasswordResetCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    /// <summary>Kikundi kilichotumika kuthibitisha utambulisho (kumbukumbu + kikundi kiona).</summary>
    public Guid? GroupId { get; set; }
    /// <summary>SHA-256(code + salt). Code yenyewe haihifadhiwi.</summary>
    public string CodeHash { get; set; } = string.Empty;
    public string Salt { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public Guid IssuedByPlatformAdminId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
