namespace ChamaLink.Domain.Entities;

// ============================================================================
//  MAFAO (Msiba / Sherehe) + UTAWALA WA WATU WENGI
//
//  Kanuni (Katiba ya VIJANA UMOJA MKOBA WING, lakini kila kitu ni SERA ya kikundi):
//   - Mafao = kiwango kwa KILA mwanachama × idadi ya wanachama wanaohesabiwa
//     (kiambatanisho 2: "MAFAO YATOLEWE INDIVIDUAL KUTOKA KWA WANAKIKUNDI";
//     M-Koba: 390,000 = 15,000×26; 640,000 = 20,000×32; 435,000 = 15,000×29).
//   - Utoaji wa fedha unahitaji ridhaa ya watu wengi (Kanuni 15(2)); idadi inatoka kwenye sera.
//   - Hakuna thamani ya default: sera isipowekwa na KUIDHINISHWA na kiongozi wa pili => 409.
//   - Hakuna kufuta: kesi zinaghairiwa/kukataliwa, rekodi inabaki.
// ============================================================================

/// <summary>
/// SASISHO19: aina za mafao. JobLoss = "kufukuzwa kazi" — ni aina yake (J3 TOGETHER FOREVER
/// inalipa 10,000 kwa mwanachama). Kiasi hicho hakiwekwi kwenye code: kinatoka kwenye
/// BenefitRate ya sera ya kikundi.
/// </summary>
public enum BenefitCategory { Msiba = 1, Sherehe = 2, JobLoss = 3 }

/// <summary>Kanuni 2: msiba = mwanachama mwenyewe, mwenza, mtoto, mzazi; sherehe = ndoa/kujifungua/kuozesha.</summary>
/// <remarks>
/// SASISHO19: InLaw = mkwe/mkoi (familia ya mwenzi) na Guardian = mlezi. J3 inalipa 5,000 kwa
/// kila mmoja; mkwe anaweza kudai MARA MOJA tu (MaxClaimsPerMember kwenye sera).
/// </remarks>
public enum BenefitRelationship { Self = 1, Spouse = 2, Child = 3, Parent = 4, InLaw = 5, Guardian = 6 }

public enum BenefitHeadcountRule
{
    /// <summary>Wanachama wote walio hai kwenye kikundi siku ya tukio.</summary>
    AllActiveMembers = 1,
    /// <summary>Walio hai na wasio na deni la michango/faini siku ya tukio.</summary>
    MembersWithNoArrears = 2,
    /// <summary>Walio hai waliolipa chochote ndani ya siku N kabla ya tukio (N = HeadcountPaidWithinDays).</summary>
    MembersPaidWithinDays = 3
}

public enum BenefitFundingSource
{
    /// <summary>Mafao yanalipwa kutoka mfuko wa kikundi (mgao wa kila mwanachama unaandikwa kwa uwazi).</summary>
    GroupFund = 1,
    /// <summary>Kila mwanachama anachangia upya kwa tukio (harambee) — awamu ijayo.</summary>
    NewCollection = 2
}

/// <summary>SASISHO14: njia ya malipo ya kesi. Mkoba = rejea ya M-Koba (ukweli); Cash = taslimu ya zamani (kabla ya tarehe ya mwisho ya kumbukumbu za mkononi).</summary>
public enum BenefitPaymentChannel { Mkoba = 1, Cash = 2 }

/// <summary>SASISHO14: tarehe ya kuhesabu wanachama (headcount).</summary>
public enum HeadcountDateBasis
{
    /// <summary>Tarehe ya tukio (msiba/sherehe) — chaguo-msingi.</summary>
    EventDate = 1,
    /// <summary>Tarehe dai lilipofunguliwa.</summary>
    ClaimDate = 2,
    /// <summary>Tarehe ya uhakiki (hesabu inapofungwa).</summary>
    VerificationDate = 3
}

public enum ProposalStatus { Pending = 1, Approved = 2, Rejected = 3 }

public enum BenefitCaseStatus
{
    AwaitingEvidence = 1,
    AwaitingVerification = 2,
    Voting = 3,
    Approved = 4,
    Paid = 5,
    Rejected = 6,
    Cancelled = 7,
    /// <summary>Kesi ya zamani (malipo yameshatoka M-Koba) inasubiri kiongozi wa pili kuthibitisha.</summary>
    HistoricalPendingConfirmation = 8,
    /// <summary>Mweka-malipo (mf. Mhazini) ameanzisha malipo baada ya kuhakiki pesa ipo; inasubiri mthibitishaji MWINGINE na rejea ya M-Koba.</summary>
    PaymentInitiated = 9,
    /// <summary>SASISHO14: kura zimeidhinisha lakini pesa (salio la M-Koba) haitoshi — inasubiri pesa kabla ya kuwa Approved.</summary>
    AwaitingFunds = 10
}

public enum EvidenceDocumentType
{
    DeathCertificate = 1,
    BurialPermit = 2,
    LocalGovernmentLetter = 3,
    MarriageCertificate = 4,
    BirthCertificate = 5,
    HospitalLetter = 6,
    Other = 7,
    /// <summary>SASISHO14: risiti/uthibitisho wa malipo ya taslimu (kesi za zamani nje ya M-Koba).</summary>
    PaymentReceipt = 8
}

public enum EvidenceStatus { Uploaded = 1, Verified = 2, Rejected = 3 }

/// <summary>
/// Sera ya utawala wa mafao na hali za wanachama. Inaandikwa kama TOLEO jipya kila mara
/// (Pending) na inaanza kutumika tu baada ya kiongozi WA PILI kuidhinisha. Matoleo ya zamani yanabaki.
/// </summary>
public class BenefitGovernance
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public ProposalStatus Status { get; set; } = ProposalStatus.Pending;

    /// <summary>Idadi ya kura za NDIYO zinazohitajika kuidhinisha malipo (Kanuni 15(2): 5).</summary>
    public int RequiredApprovals { get; set; }
    /// <summary>Kati ya kura hizo, ngapi lazima ziwe za viongozi (0 = hakuna sharti).</summary>
    public int MinLeaderApprovals { get; set; }
    /// <summary>Wanachama wa kawaida wanaruhusiwa kupiga kura ya kuidhinisha.</summary>
    public bool AllowMemberApprovers { get; set; }
    /// <summary>Kura ngapi za HAPANA (zenye sababu) zinakataa kesi.</summary>
    public int RejectionsToReject { get; set; }
    /// <summary>Siku za kupiga kura tangu ushahidi uthibitishwe.</summary>
    public int VotingDays { get; set; }

    public BenefitHeadcountRule HeadcountRule { get; set; }
    public int? HeadcountPaidWithinDays { get; set; }
    public bool ExcludeBeneficiaryFromHeadcount { get; set; }
    public BenefitFundingSource FundingSource { get; set; }

    /// <summary>Kata Kianzio kilichobaki cha mnufaika kabla ya kumlipa.</summary>
    public bool DeductJoinFee { get; set; }
    /// <summary>Kata pia deni la michango/faini/mkopo uliochelewa.</summary>
    public bool DeductArrears { get; set; }
    /// <summary>Kanuni 5(d): mnufaika asiwe na deni la michango siku ya tukio.</summary>
    public bool RequireNoArrearsForEligibility { get; set; }
    /// <summary>Siku za chini za uanachama kabla ya kustahili mafao (0 = hakuna sharti).</summary>
    public int MinMembershipDays { get; set; }
    /// <summary>Ombi liwasilishwe ndani ya siku hizi baada ya tukio.</summary>
    public int ClaimWindowDays { get; set; }
    /// <summary>Malipo yaliyoidhinishwa yaonekane M-Koba ndani ya siku hizi, vinginevyo tahadhari.</summary>
    public int PaymentConfirmationDays { get; set; }

    /// <summary>Idhini za viongozi (zaidi ya aliyeomba) kubadilisha hali ya mwanachama (kujitoa, kifo, kusimamishwa).</summary>
    public int StatusChangeApprovals { get; set; }
    /// <summary>Kanuni 12(3): asilimia ya wanachama WOTE wanaohitajika kumfukuza mwanachama (mf. 75).</summary>
    public int ExpulsionVotePercent { get; set; }

    // ---- SASISHO13: toleo, ulinzi wa ziada, mgawanyo wa majukumu (SoD) ----
    /// <summary>Namba ya toleo la sera ndani ya kikundi (1, 2, 3...). Toleo la zamani halibadiliki kamwe.</summary>
    public int Version { get; set; }
    /// <summary>Mnufaika lazima awe amemaliza Kianzio siku ya tukio (haiwezi kwenda pamoja na DeductJoinFee).</summary>
    public bool RequireJoinFeePaid { get; set; }
    /// <summary>Siku za chini kati ya madai mawili ya aina moja kwa mnufaika mmoja (null = hakuna sharti).</summary>
    public int? CooldownDays { get; set; }
    /// <summary>Kiasi cha juu (jumla ya mafao ghafi) ambacho mwanachama mmoja anaweza kupata ndani ya MaxBenefitPeriodDays (null = bila kikomo).</summary>
    public decimal? MaxBenefitAmountPerPeriod { get; set; }
    public int? MaxBenefitPeriodDays { get; set; }
    /// <summary>Malipo hayaanzishwi kama salio la M-Koba (ukweli) ukiondoa kesi zilizoidhinishwa hazijalipwa halitoshi.</summary>
    public bool RequireCashAvailability { get; set; }
    /// <summary>CSV ya GroupRole wanaoruhusiwa KUANZISHA malipo (mf. "2" = Mhazini).</summary>
    public string PaymentInitiatorRoles { get; set; } = string.Empty;
    /// <summary>CSV ya GroupRole wanaoruhusiwa KUTHIBITISHA malipo kwa rejea ya M-Koba (mtu tofauti na aliyeanzisha).</summary>
    public string PaymentConfirmerRoles { get; set; } = string.Empty;
    /// <summary>Aliyepiga kura ya NDIYO hawezi kuanzisha malipo ya kesi hiyo.</summary>
    public bool ApproverCannotInitiatePayment { get; set; }
    /// <summary>Ndugu waliosajiliwa (MemberRelation) wa mnufaika hawapigi kura, hawahakiki ushahidi, hawaanzishi wala kuthibitisha malipo.</summary>
    public bool ExcludeRelatedParties { get; set; }
    /// <summary>Tahadhari ikiwa mwanachama ana madai ≥ N ndani ya siku 365 (null = hakuna).</summary>
    public int? FrequentClaimsFlagCount { get; set; }
    /// <summary>Tahadhari ikiwa kesi imefunguliwa ndani ya siku N baada ya sera/kiwango kubadilishwa (null = hakuna).</summary>
    public int? RecentPolicyChangeFlagDays { get; set; }

    // ---- SASISHO14 ----
    /// <summary>Kura ya mwisho ikiidhinisha na pesa haitoshi -> AwaitingFunds (si Approved).</summary>
    public bool CashCheckAtApproval { get; set; }
    /// <summary>Aina za uhusiano (MemberRelationType, csv) zinazoleta mgongano wa maslahi. ExcludeRelatedParties ndiyo swichi kuu.</summary>
    public string ConflictRelationTypes { get; set; } = string.Empty;
    /// <summary>Nafasi (GroupRole, csv) zinazoruhusiwa kupiga kura ya kuidhinisha. Member=4 = wanachama wa kawaida.</summary>
    public string ApproverRoles { get; set; } = string.Empty;
    /// <summary>Nafasi (GroupRole, csv) za Mhakiki wa Ustahiki (anahakiki ushahidi).</summary>
    public string EvidenceVerifierRoles { get; set; } = string.Empty;
    /// <summary>Wakaguzi (GroupMember Id, csv): wanaona daftari/ulinganisho/hatari bila kuandika.</summary>
    public string AuditorMemberIds { get; set; } = string.Empty;
    /// <summary>Kiasi (halisi) kinachohesabiwa kuwa kikubwa: aliyeidhinisha hawezi kuanzisha malipo. null = imezimwa.</summary>
    public decimal? HighValueThreshold { get; set; }
    /// <summary>true = mhusika yule yule akidaiwa na mwanachama mwingine -> zuia (false = tahadhari tu).</summary>
    public bool BlockSameSubjectAcrossMembers { get; set; }
    /// <summary>Kata madeni ya MKOPO yaliyopitiliza kutoka kwenye mafao (tofauti na madeni ya michango).</summary>
    public bool DeductLoanArrears { get; set; }
    /// <summary>Sera inaanza kutumika tarehe hii (tukio la tarehe X linatumia sera iliyokuwa hai tarehe X).</summary>
    public DateTime EffectiveFrom { get; set; }
    /// <summary>Tarehe ya kuhesabu wanachama.</summary>
    public HeadcountDateBasis HeadcountAsOf { get; set; } = HeadcountDateBasis.EventDate;

    public Guid RequestedByUserId { get; set; }
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public Guid? DecidedByUserId { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// Kiwango cha mafao kwa kila mwanachama kwa (aina, uhusiano), kuanzia tarehe fulani. Toleo jipya = mstari mpya
/// (Pending -> kiongozi wa pili anaidhinisha). Kinachotumika siku D = cha mwisho kilichoidhinishwa chenye EffectiveFrom ≤ D.
/// </summary>
public class BenefitRate
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public BenefitCategory Category { get; set; }
    public BenefitRelationship Relationship { get; set; }
    /// <summary>Kiasi ambacho KILA mwanachama anachangia kwa tukio hili (mf. 20,000).</summary>
    public decimal PerMemberAmount { get; set; }
    /// <summary>Aina za ushahidi zinazokubalika (angalau moja iliyothibitishwa), CSV ya EvidenceDocumentType.</summary>
    public string AcceptedEvidence { get; set; } = string.Empty;
    /// <summary>Idadi ya juu ya madai kwa mwanachama kwa (aina, uhusiano). Null = bila kikomo.</summary>
    public int? MaxClaimsPerMember { get; set; }
    /// <summary>true = uhusiano huu hauna mafao tena kuanzia EffectiveFrom.</summary>
    public bool IsDisabled { get; set; }
    public DateTime EffectiveFrom { get; set; }
    /// <summary>SASISHO14: kiwango kinaisha tarehe hii (pamoja nayo). null = hakina mwisho.</summary>
    public DateTime? EffectiveTo { get; set; }
    public ProposalStatus Status { get; set; } = ProposalStatus.Pending;
    public Guid RequestedByUserId { get; set; }
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public Guid? DecidedByUserId { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? Note { get; set; }
}

public class BenefitCase
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    /// <summary>Namba ya kesi ya kipekee ndani ya kikundi, mf. UMK-MF-2026-003.</summary>
    public string CaseNumber { get; set; } = string.Empty;
    public BenefitCategory Category { get; set; }
    public BenefitRelationship Relationship { get; set; }
    public Guid BeneficiaryGroupMemberId { get; set; }
    /// <summary>Jina la mhusika wa tukio (mf. marehemu, mtoto aliyezaliwa, mwenza).</summary>
    public string SubjectName { get; set; } = string.Empty;
    public DateTime EventDate { get; set; }
    public string? Description { get; set; }
    public BenefitCaseStatus Status { get; set; }
    public bool IsHistorical { get; set; }

    // ---- Snapshot ya hesabu (inafungwa kura zinapoanza; haibadiliki baadaye) ----
    public Guid? BenefitRateId { get; set; }
    public Guid? GovernanceId { get; set; }
    public decimal PerMemberAmount { get; set; }
    public int Headcount { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal JoinFeeDeducted { get; set; }
    public decimal ArrearsDeducted { get; set; }
    /// <summary>SASISHO14: deni la MKOPO lililokatwa. null = kesi ya zamani (ArrearsDeducted ilijumuisha mikopo).</summary>
    public decimal? LoanArrearsDeducted { get; set; }
    public decimal NetAmount { get; set; }
    public string? HeadcountRuleUsed { get; set; }
    /// <summary>JSON: orodha ya GroupMember.Id waliohesabiwa (null kwa kesi za zamani ambazo orodha haijulikani).</summary>
    public string? CountedMemberIdsJson { get; set; }
    /// <summary>JSON: matokeo ya ukaguzi wa ustahiki (kila hundi + sababu).</summary>
    public string? EligibilityJson { get; set; }
    public DateTime? SnapshotAt { get; set; }

    public DateTime? VotingDeadline { get; set; }
    public DateTime? ApprovedAt { get; set; }
    /// <summary>Toleo la sera lililotumika (snapshot).</summary>
    public int? PolicyVersion { get; set; }

    // ---- Kuanzisha malipo (hatua ya 1 ya malipo) ----
    public Guid? PaymentInitiatedByUserId { get; set; }
    public DateTime? PaymentInitiatedAt { get; set; }
    /// <summary>Namba iliyoidhinishwa ya mnufaika ambayo malipo yanaelekezwa.</summary>
    public string? PaymentInitiatedToPhone { get; set; }
    /// <summary>Pesa iliyopatikana (salio la M-Koba − akiba za kesi nyingine) wakati wa kuanzisha.</summary>
    public decimal? CashAvailableAtInitiation { get; set; }

    // ---- Malipo (M-Koba ndiyo ushahidi) ----
    public string? MkobaReference { get; set; }
    /// <summary>SASISHO14: njia ya malipo (Mkoba/Cash). Cash inaruhusiwa kwa kesi za zamani tu, kabla ya tarehe ya mwisho.</summary>
    public BenefitPaymentChannel PaymentChannel { get; set; } = BenefitPaymentChannel.Mkoba;
    public string? CashRecipientName { get; set; }
    public string? CashRecipientPhone { get; set; }
    /// <summary>Sababu ya malipo ya kipekee (taslimu).</summary>
    public string? ExceptionReason { get; set; }
    /// <summary>Saa ya muamala wa M-Koba (EAT, kama PDF).</summary>
    public DateTime? PaidAt { get; set; }
    public Guid? PaidConfirmedByUserId { get; set; }
    public DateTime? PaidConfirmedAt { get; set; }

    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? ClosedReason { get; set; }
    public Guid? ClosedByUserId { get; set; }
    public DateTime? ClosedAt { get; set; }

    public List<BenefitEvidence> Evidence { get; set; } = new();
}

public class BenefitEvidence
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid BenefitCaseId { get; set; }
    public EvidenceDocumentType DocumentType { get; set; }
    /// <summary>Namba ya cheti/kibali (ya kipekee kwa aina ndani ya kikundi).</summary>
    public string DocumentNumber { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    /// <summary>SHA-256 ya faili: faili lile lile haliwezi kutumika mara mbili, na mabadiliko yanaonekana.</summary>
    public string Sha256 { get; set; } = string.Empty;
    [System.Text.Json.Serialization.JsonIgnore] // SASISHO16: njia ya faili ya ndani — kamwe isiende kwa mteja
    public string StorageKey { get; set; } = string.Empty;
    public Guid UploadedByUserId { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public EvidenceStatus Status { get; set; } = EvidenceStatus.Uploaded;
    public Guid? VerifiedByUserId { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public string? ReviewNote { get; set; }
}

/// <summary>Kura moja ya mtu mmoja kwa jambo moja (kesi ya mafao, au mabadiliko ya hali ya mwanachama).</summary>
public class GovernanceVote
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    /// <summary>"BenefitCase" au "MemberStatusChange".</summary>
    public string SubjectType { get; set; } = string.Empty;
    public Guid SubjectId { get; set; }
    public Guid GroupMemberId { get; set; }
    public Guid UserId { get; set; }
    public bool Approve { get; set; }
    public string? Reason { get; set; }
    public GroupRole RoleAtVote { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Ombi la kubadilisha hali ya mwanachama (kujitoa, kifo, kusimamishwa, kurejeshwa, kufukuzwa).
/// Mwanachama HAFUTWI kamwe — historia yake yote ya fedha inabaki.
/// </summary>
public class MemberStatusChangeRequest
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid GroupMemberId { get; set; }
    public MemberStatus FromStatus { get; set; }
    public MemberStatus ToStatus { get; set; }
    /// <summary>Kufukuzwa (Kanuni 12(3)) — kunahitaji kura za asilimia ya wanachama wote.</summary>
    public bool IsExpulsion { get; set; }
    public DateTime EffectiveDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? EvidenceNote { get; set; }
    /// <summary>Kura za NDIYO zinazohitajika (snapshot ya sera wakati wa ombi).</summary>
    public int RequiredApprovals { get; set; }
    public bool LeadersOnly { get; set; }
    public int RejectionsToReject { get; set; }
    public ProposalStatus Status { get; set; } = ProposalStatus.Pending;
    public Guid? GovernanceId { get; set; }
    public Guid RequestedByUserId { get; set; }
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DecidedAt { get; set; }
    /// <summary>SASISHO17: hali imetekelezwa lini (EffectiveDate ikifika). Null + Approved = inasubiri tarehe yake.</summary>
    public DateTime? AppliedAt { get; set; }
}

public enum WithdrawalExplanationKind { LoanDisbursement = 1, GroupExpense = 2, BankTransfer = 3, Refund = 4, Other = 5 }

/// <summary>
/// Maelezo ya pesa iliyotoka M-Koba ambayo SI mafao (mf. mkopo, gharama ya kikundi). Kiongozi wa pili anathibitisha.
/// Kila utoaji wa M-Koba lazima uelezwe na kesi ya mafao au maelezo haya — vinginevyo ni tahadhari nyekundu.
/// </summary>
public class MkobaWithdrawalExplanation
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public string MkobaReference { get; set; } = string.Empty;
    public WithdrawalExplanationKind Kind { get; set; }
    public string Description { get; set; } = string.Empty;
    public ProposalStatus Status { get; set; } = ProposalStatus.Pending;
    public Guid RequestedByUserId { get; set; }
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public Guid? DecidedByUserId { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
}

/// <summary>Arifa ndani ya mfumo kwa mwanachama mmoja.</summary>
public class MemberNotification
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid RecipientGroupMemberId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? Link { get; set; }
    /// <summary>SASISHO14: {"title":{tpl,args},"body":{tpl,args}} — frontend inatafsiri.</summary>
    public string? LocJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }
}

/// <summary>
/// Pendekezo la kubadilisha kanuni za fedha za Injini ya Ukweli (mchango, faini, Kianzio, tarehe...).
/// Mabadiliko haya yanaathiri deni la KILA mwanachama, kwa hiyo yanahitaji kiongozi wa pili (maker-checker).
/// (Mipangilio ya KWANZA kabisa ya kikundi inaruhusiwa moja kwa moja ili kikundi kiweze kuanza.)
/// </summary>
public class TruthSettingsProposal
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    /// <summary>JSON ya SettingsDto iliyopendekezwa (sehemu zilizojazwa tu ndizo zinabadilika).</summary>
    public string PayloadJson { get; set; } = "{}";
    public ProposalStatus Status { get; set; } = ProposalStatus.Pending;
    public Guid RequestedByUserId { get; set; }
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public Guid? DecidedByUserId { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? Note { get; set; }
    public string? DecisionNote { get; set; }
}

public enum MemberRelationType { Spouse = 1, Parent = 2, Child = 3, Sibling = 4, Household = 5, BusinessPartner = 6, Other = 7 }

/// <summary>Pending = imeombwa na kiongozi mmoja, inasubiri kiongozi wa pili (SASISHO15: kuzuia kiongozi mmoja kuwafungia wenzake kura).</summary>
public enum MemberRelationStatus { Active = 1, EndPending = 2, Ended = 3, Pending = 4, Rejected = 5 }

/// <summary>
/// Uhusiano kati ya wanachama wawili (mgongano wa maslahi). SASISHO15: KUONGEZA na KUONDOA vyote vinahitaji kiongozi
/// wa pili (maker-checker). Uhusiano unaosubiri (Pending) hauzuii kura — vinginevyo kiongozi mmoja angeweza kuwasajili
/// wenzake wote kama "ndugu" na kukwamisha kila kesi (denial-of-governance).
/// </summary>
public class MemberRelation
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid MemberAId { get; set; }
    public Guid MemberBId { get; set; }
    public MemberRelationType Type { get; set; }
    public string? Note { get; set; }
    public MemberRelationStatus Status { get; set; } = MemberRelationStatus.Pending;
    public Guid CreatedByUserId { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? DecisionNote { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid? EndRequestedByUserId { get; set; }
    public DateTime? EndRequestedAt { get; set; }
    public string? EndReason { get; set; }
    public Guid? EndedByUserId { get; set; }
    public DateTime? EndedAt { get; set; }
}

public enum BenefitLedgerKind { Approved = 1, JoinFeeOffset = 2, ArrearsOffset = 3, CashPaid = 4, LoanArrearsOffset = 5 }

/// <summary>
/// Maandishi ya kifedha ya kesi ya mafao (yanaandikwa mara moja kesi inapolipwa; hayabadiliki).
/// Kanuni isiyovunjika: Approved = JoinFeeOffset + ArrearsOffset + CashPaid.
/// JoinFeeOffset/ArrearsOffset zinaonekana pia kwenye Injini ya Ukweli kama MAFAO-JF-/MAFAO-DENI-{kesi}.
/// </summary>
public class BenefitLedgerEntry
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid BenefitCaseId { get; set; }
    public Guid BeneficiaryGroupMemberId { get; set; }
    public BenefitLedgerKind Kind { get; set; }
    public decimal Amount { get; set; }
    /// <summary>Rejea: M-Koba (CashPaid) au rejea ya Injini ya Ukweli (MAFAO-JF-... / MAFAO-DENI-...).</summary>
    public string? Reference { get; set; }
    public DateTime PostedAt { get; set; } = DateTime.UtcNow;
    public Guid PostedByUserId { get; set; }
}
