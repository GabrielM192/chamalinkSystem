using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Infrastructure;

public class ApplicationDbContext : DbContext
{
    /// <summary>SASISHO21 — urefu wa kodi ya malipo (mf. "J3-E07-0147").</summary>
    private const int PaymentCodeMaxLength = 20;

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<GroupSettings> GroupSettings => Set<GroupSettings>(); // Fixed entity class name
    public DbSet<GroupMember> GroupMembers => Set<GroupMember>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<GroupEvent> GroupEvents => Set<GroupEvent>();

    // NEW: stores "Withdraw / Transfer fund" rows found in M-Koba
    // statements, so they are kept for the treasurer to review instead of
    // only showing up once in an import response and then being lost.
    public DbSet<WalletWithdrawal> WalletWithdrawals => Set<WalletWithdrawal>();

    // NEW (ChamaLink v1 scope): structured, audited withdrawal/expenditure
    // records - see Withdrawal.cs for how this differs from WalletWithdrawal.
    public DbSet<Withdrawal> Withdrawals => Set<Withdrawal>();

    // NEW (Withdrawal Governance gap): individual approve/reject votes
    // toward a Withdrawal's decision - see WithdrawalApproval.cs.
    public DbSet<WithdrawalApproval> WithdrawalApprovals => Set<WithdrawalApproval>();

    // NEW (Reports Engine gap: Loan Portfolio Report / Member Financial
    // Profile). Individual loan records - see Loan.cs for how this
    // differs from the raw LoanDisbursement/LoanRepayment ledger rows.
    public DbSet<Loan> Loans => Set<Loan>();

    // NEW: Sprint 1 gaps - see Fine.cs, Debt.cs for how each differs from
    // the raw LedgerEntry rows that already existed.
    public DbSet<Fine> Fines => Set<Fine>();
    public DbSet<Debt> Debts => Set<Debt>();

    // NEW: Sprint 2 gap - see EventContribution.cs.
    public DbSet<EventContribution> EventContributions => Set<EventContribution>();

    // SASISHO18: wajibu wa kila mwanachama kwa kila tukio (vikundi vya "kila tukio").
    public DbSet<EventObligation> EventObligations => Set<EventObligation>();

    // NEW: Sprint 1 gap #18 - whole-statement duplicate import protection.
    public DbSet<ImportedStatement> ImportedStatements => Set<ImportedStatement>();

    // NEW: Ukonga Rules Specification v1.2, sehemu 6 (Phase 4). Reporting
    // snapshot only - see ComplianceSnapshot.cs.
    public DbSet<ComplianceSnapshot> ComplianceSnapshots => Set<ComplianceSnapshot>();

    // V2 — Financial OS foundation
    public DbSet<GroupPolicy> GroupPolicies => Set<GroupPolicy>();
    public DbSet<FinancialEvent> FinancialEvents => Set<FinancialEvent>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<WelfareEvent> WelfareEvents => Set<WelfareEvent>();
    public DbSet<WelfareObligation> WelfareObligations => Set<WelfareObligation>();
    public DbSet<WelfareContribution> WelfareContributions => Set<WelfareContribution>();
    public DbSet<WelfareDisbursement> WelfareDisbursements => Set<WelfareDisbursement>();
    public DbSet<MemberStatusHistory> MemberStatusHistories => Set<MemberStatusHistory>();

    // Injini ya Ukweli (data ghafi tu)
    public DbSet<TruthGroupSettings> TruthGroupSettings => Set<TruthGroupSettings>();
    public DbSet<MkobaRawTransaction> MkobaRawTransactions => Set<MkobaRawTransaction>();
    public DbSet<PaymentReceipt> PaymentReceipts => Set<PaymentReceipt>();
    public DbSet<PaymentClaim> PaymentClaims => Set<PaymentClaim>();
    public DbSet<SmsMessage> SmsMessages => Set<SmsMessage>();

    // SASISHO21 — rufaa: mwanachama anapinga uamuzi; viongozi WAWILI wanaamua.
    public DbSet<MemberAppeal> MemberAppeals => Set<MemberAppeal>();
    public DbSet<MemberAppealVote> MemberAppealVotes => Set<MemberAppealVote>();
    public DbSet<SmsAccount> SmsAccounts => Set<SmsAccount>();
    public DbSet<MemberPhone> MemberPhones => Set<MemberPhone>();
    public DbSet<MkobaStatementGap> MkobaStatementGaps => Set<MkobaStatementGap>();
    public DbSet<TruthAdjustmentRecord> TruthAdjustments => Set<TruthAdjustmentRecord>();
    public DbSet<MembershipRequest> MembershipRequests => Set<MembershipRequest>();
    // Mafao + utawala wa watu wengi
    public DbSet<BenefitGovernance> BenefitGovernances => Set<BenefitGovernance>();
    public DbSet<BenefitRate> BenefitRates => Set<BenefitRate>();
    public DbSet<BenefitCase> BenefitCases => Set<BenefitCase>();
    public DbSet<BenefitEvidence> BenefitEvidence => Set<BenefitEvidence>();
    public DbSet<GovernanceVote> GovernanceVotes => Set<GovernanceVote>();
    public DbSet<MemberStatusChangeRequest> MemberStatusChangeRequests => Set<MemberStatusChangeRequest>();
    // SASISHO20 — msimamizi wa mfumo (platform-wide, si nafasi ya kikundi)
    public DbSet<PlatformAdmin> PlatformAdmins => Set<PlatformAdmin>();
    public DbSet<PlatformAuditEvent> PlatformAuditEvents => Set<PlatformAuditEvent>();
    public DbSet<PlatformApproval> PlatformApprovals => Set<PlatformApproval>();
    public DbSet<PlatformPasswordResetCode> PlatformPasswordResetCodes => Set<PlatformPasswordResetCode>();
    public DbSet<MkobaWithdrawalExplanation> MkobaWithdrawalExplanations => Set<MkobaWithdrawalExplanation>();
    public DbSet<MemberNotification> MemberNotifications => Set<MemberNotification>();
    public DbSet<MemberRelation> MemberRelations => Set<MemberRelation>();
    public DbSet<BenefitLedgerEntry> BenefitLedgerEntries => Set<BenefitLedgerEntry>();
    public DbSet<TruthSettingsProposal> TruthSettingsProposals => Set<TruthSettingsProposal>();
    public DbSet<LoanApplication> LoanApplications => Set<LoanApplication>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    // SASISHO17
    public DbSet<RoleChangeRequest> RoleChangeRequests => Set<RoleChangeRequest>();
    public DbSet<MkobaStatementImport> MkobaStatementImports => Set<MkobaStatementImport>();
    public DbSet<GroupResetRequest> GroupResetRequests => Set<GroupResetRequest>();
    public DbSet<LoginFailure> LoginFailures => Set<LoginFailure>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ---- Injini ya Ukweli ----
        modelBuilder.Entity<TruthGroupSettings>(e =>
        {
            e.HasKey(x => x.GroupId);
            e.Property(x => x.MonthlyContribution).HasPrecision(18, 2);
            e.Property(x => x.LateFine).HasPrecision(18, 2);
            e.Property(x => x.LateFinePercent).HasPrecision(5, 2);
            e.Property(x => x.JoiningFee).HasPrecision(18, 2);
            e.Property(x => x.LoanLatePenaltyAmount).HasPrecision(18, 2);
            e.Property(x => x.LoanInterestPercent).HasPrecision(9, 4);
            e.Property(x => x.LoanMaxAmount).HasPrecision(18, 2);
            e.Property(x => x.LoanMaxMultipleOfContributions).HasPrecision(9, 4);
            e.Property(x => x.LoanMinCashReserve).HasPrecision(18, 2);
            // SASISHO18 — sera ya vikundi vya "kila tukio"
            e.Property(x => x.MemberMinimumBalance).HasPrecision(18, 2);
            e.Property(x => x.OverdueEventFineAmount).HasPrecision(18, 2);
            e.Property(x => x.MaxEventDeductionPerMemberPerMonth).HasPrecision(18, 2);
            // SASISHO19 — kanuni za J3: faini -> neema -> hatari ya kuondolewa -> kurudi
            e.Property(x => x.ReinstatementFineMultiplier).HasPrecision(9, 2);
        });
        modelBuilder.Entity<MkobaRawTransaction>(e =>
        {
            e.HasIndex(x => new { x.GroupId, x.Reference }).IsUnique();   // muamala mmoja = mara moja tu
            e.HasIndex(x => new { x.GroupId, x.Phone });
            e.HasIndex(x => new { x.GroupId, x.Timestamp });              // ukuaji: cashbook/ripoti kwa tarehe
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Balance).HasPrecision(18, 2);
            e.Property(x => x.Reference).HasMaxLength(40);
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.AttributionReason).HasMaxLength(300);
            // SASISHO21: sanduku la malipo hupata amana zisizotambulika kwa kikundi.
            e.HasIndex(x => new { x.GroupId, x.AttributedGroupMemberId });
        });

        // ---- SASISHO21: SANDUKU LA MALIPO (risiti + madai) ----
        modelBuilder.Entity<PaymentReceipt>(e =>
        {
            e.HasIndex(x => new { x.GroupId, x.Status });
            // Mstari mmoja wa statement = risiti moja tu (hakuna kuingiza mara mbili).
            e.HasIndex(x => new { x.GroupId, x.MkobaTransactionId }).IsUnique()
                .HasFilter("\"MkobaTransactionId\" IS NOT NULL");
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.MatchRule).HasMaxLength(20);
            e.Property(x => x.Reference).HasMaxLength(80);
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.PaymentCodeSeen).HasMaxLength(20);
            e.Property(x => x.PayerName).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(400);
            e.Property(x => x.Note).HasMaxLength(500);
            // SASISHO21 (Sehemu ya 2): malipo ya kujiunga
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.GroupId, x.MembershipRequestId });
        });

        // ---- SASISHO21 (Sehemu ya 2): SMS ----
        modelBuilder.Entity<SmsMessage>(e =>
        {
            e.HasIndex(x => new { x.GroupId, x.Status, x.SendAfter });
        // ---------------------------------------------------------------
        // SASISHO21 — RUFA
        // ---------------------------------------------------------------
        modelBuilder.Entity<MemberAppeal>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.GroupId, x.Status });
            e.HasIndex(x => new { x.GroupId, x.GroupMemberId, x.Status });
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasOne<Group>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<GroupMember>().WithMany().HasForeignKey(x => x.GroupMemberId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MemberAppealVote>(e =>
        {
            e.HasKey(x => x.Id);
            // Kiongozi mmoja = kura MOJA kwa rufaa (haiwezi kupiga mara mbili)
            e.HasIndex(x => new { x.AppealId, x.UserId }).IsUnique();
            e.HasOne<MemberAppeal>().WithMany().HasForeignKey(x => x.AppealId).OnDelete(DeleteBehavior.Cascade);
        });
   // foleni ya kutuma
            e.HasIndex(x => new { x.GroupId, x.CreatedAt });
            e.HasIndex(x => x.ProviderReference);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.TemplateKey).HasMaxLength(40);
            e.Property(x => x.Body).HasMaxLength(360);
            e.Property(x => x.ProviderReference).HasMaxLength(80);
            e.Property(x => x.ProviderStatus).HasMaxLength(40);
            e.Property(x => x.FailureReason).HasMaxLength(300);
            e.Property(x => x.SuppressedReason).HasMaxLength(200);
            e.Property(x => x.RelatedEntityType).HasMaxLength(40);
        });

        modelBuilder.Entity<SmsAccount>(e =>
        {
            e.HasKey(x => x.GroupId);
        });

        modelBuilder.Entity<PaymentClaim>(e =>
        {
            e.HasIndex(x => new { x.GroupId, x.Status });
            e.HasIndex(x => new { x.GroupId, x.GroupMemberId });
            // Namba ya muamala haiwezi kuombwa mara mbili kwenye kikundi kimoja.
            e.HasIndex(x => new { x.GroupId, x.Reference }).IsUnique();
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Reference).HasMaxLength(80);
            e.Property(x => x.PaymentCode).HasMaxLength(20);
            e.Property(x => x.Note).HasMaxLength(500);
            e.Property(x => x.DecisionNote).HasMaxLength(500);
        });
        modelBuilder.Entity<MemberPhone>(e =>
        {
            // Namba moja inaweza kuwa ya watu wawili kwa NYAKATI TOFAUTI (mf. ya Fauzia -> Abdulla hadi Juni).
            // Kutopishana kwa vipindi kunakaguliwa na TruthSetupService.
            e.HasIndex(x => new { x.GroupId, x.Phone });
            e.HasIndex(x => x.GroupMemberId);
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.PendingEndReason).HasMaxLength(500);
        });

        // ---- Mafao (msiba/sherehe) + utawala wa watu wengi ----
        modelBuilder.Entity<BenefitGovernance>(e =>
        {
            e.HasIndex(x => new { x.GroupId, x.Status });
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.HeadcountRule).HasConversion<string>().HasMaxLength(40);
            e.Property(x => x.FundingSource).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Note).HasMaxLength(500);
            e.Property(x => x.PaymentInitiatorRoles).HasMaxLength(60);
            e.Property(x => x.PaymentConfirmerRoles).HasMaxLength(60);
            e.Property(x => x.MaxBenefitAmountPerPeriod).HasPrecision(18, 2);
            e.Property(x => x.HighValueThreshold).HasPrecision(18, 2);
            e.Property(x => x.ConflictRelationTypes).HasMaxLength(60);
            e.Property(x => x.ApproverRoles).HasMaxLength(60);
            e.Property(x => x.EvidenceVerifierRoles).HasMaxLength(60);
            e.Property(x => x.AuditorMemberIds).HasMaxLength(2000);
            e.Property(x => x.HeadcountAsOf).HasConversion<string>().HasMaxLength(30);
        });
        modelBuilder.Entity<BenefitRate>(e =>
        {
            e.HasIndex(x => new { x.GroupId, x.Category, x.Relationship, x.EffectiveFrom });
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Category).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Relationship).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.PerMemberAmount).HasPrecision(18, 2);
            e.Property(x => x.AcceptedEvidence).HasMaxLength(300);
            e.Property(x => x.Note).HasMaxLength(500);
        });
        modelBuilder.Entity<BenefitCase>(e =>
        {
            e.HasIndex(x => new { x.GroupId, x.CaseNumber }).IsUnique();
            e.HasIndex(x => new { x.GroupId, x.Status });
            e.HasIndex(x => x.BeneficiaryGroupMemberId);
            // Utoaji mmoja wa M-Koba unaeleza kesi MOJA tu
            e.HasIndex(x => new { x.GroupId, x.MkobaReference }).IsUnique().HasFilter("\"MkobaReference\" IS NOT NULL");
            e.Property(x => x.CaseNumber).HasMaxLength(40);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(40);
            e.Property(x => x.Category).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Relationship).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.SubjectName).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.HeadcountRuleUsed).HasMaxLength(200);
            // SASISHO15: kinga ya ushindani — maombi mawili yakibadilisha kesi moja kwa wakati mmoja, la pili linapata 409
            e.Property<uint>("xmin").HasColumnType("xid").IsRowVersion();
            e.Property(x => x.MkobaReference).HasMaxLength(40);
            e.Property(x => x.ClosedReason).HasMaxLength(500);
            e.Property(x => x.PaymentInitiatedToPhone).HasMaxLength(20);
            e.Property(x => x.CashAvailableAtInitiation).HasPrecision(18, 2);
            e.Property(x => x.LoanArrearsDeducted).HasPrecision(18, 2);
            e.Property(x => x.PaymentChannel).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.CashRecipientName).HasMaxLength(200);
            e.Property(x => x.CashRecipientPhone).HasMaxLength(20);
            e.Property(x => x.ExceptionReason).HasMaxLength(500);
            foreach (var n in new[] { nameof(BenefitCase.PerMemberAmount), nameof(BenefitCase.GrossAmount), nameof(BenefitCase.JoinFeeDeducted),
                                       nameof(BenefitCase.ArrearsDeducted), nameof(BenefitCase.NetAmount) })
                e.Property<decimal>(n).HasPrecision(18, 2);
            e.HasMany(x => x.Evidence).WithOne().HasForeignKey(x => x.BenefitCaseId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<BenefitEvidence>(e =>
        {
            e.HasIndex(x => new { x.GroupId, x.Sha256 });
            e.HasIndex(x => new { x.GroupId, x.DocumentType, x.DocumentNumber });
            e.Property(x => x.DocumentType).HasConversion<string>().HasMaxLength(40);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.DocumentNumber).HasMaxLength(100);
            e.Property(x => x.FileName).HasMaxLength(255);
            e.Property(x => x.ContentType).HasMaxLength(100);
            e.Property(x => x.Sha256).HasMaxLength(64);
            e.Property(x => x.StorageKey).HasMaxLength(300);
            e.Property(x => x.ReviewNote).HasMaxLength(500);
        });
        modelBuilder.Entity<GovernanceVote>(e =>
        {
            e.HasIndex(x => new { x.SubjectType, x.SubjectId, x.GroupMemberId }).IsUnique(); // kura moja kwa mtu
            e.HasIndex(x => x.GroupId);
            e.Property(x => x.SubjectType).HasMaxLength(40);
            e.Property(x => x.RoleAtVote).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Reason).HasMaxLength(500);
        });
        modelBuilder.Entity<MemberStatusChangeRequest>(e =>
        {
            e.HasIndex(x => new { x.GroupId, x.Status });
            e.HasIndex(x => x.GroupMemberId);
            e.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.EvidenceNote).HasMaxLength(500);
        });
        modelBuilder.Entity<MkobaWithdrawalExplanation>(e =>
        {
            e.HasIndex(x => new { x.GroupId, x.MkobaReference });
            e.Property(x => x.MkobaReference).HasMaxLength(40);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.DecisionNote).HasMaxLength(500);
        });
        modelBuilder.Entity<MemberNotification>(e =>
        {
            e.HasIndex(x => new { x.RecipientGroupMemberId, x.ReadAt });
            e.Property(x => x.Kind).HasMaxLength(40);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Body).HasMaxLength(1000);
            e.Property(x => x.Link).HasMaxLength(200);
            e.Property(x => x.LocJson).HasMaxLength(4000);
        });
        modelBuilder.Entity<MemberRelation>(e =>
        {
            e.HasIndex(x => new { x.GroupId, x.Status });
            e.HasIndex(x => x.MemberAId);
            e.HasIndex(x => x.MemberBId);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Note).HasMaxLength(300);
            e.Property(x => x.EndReason).HasMaxLength(300);
            e.Property(x => x.DecisionNote).HasMaxLength(300);
        });
        // ---- SASISHO15: maombi ya mikopo + refresh tokens ----
        modelBuilder.Entity<LoanApplication>(e =>
        {
            e.HasIndex(x => new { x.GroupId, x.ApplicationNumber }).IsUnique();
            e.HasIndex(x => new { x.GroupId, x.Status });
            e.HasIndex(x => x.BorrowerGroupMemberId);
            // Utoaji mmoja wa M-Koba unaeleza ombi MOJA tu
            e.HasIndex(x => new { x.GroupId, x.MkobaReference }).IsUnique().HasFilter("\"MkobaReference\" IS NOT NULL");
            e.Property(x => x.ApplicationNumber).HasMaxLength(40);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Purpose).HasMaxLength(500);
            e.Property(x => x.MkobaReference).HasMaxLength(60);
            e.Property(x => x.RecipientNote).HasMaxLength(300);
            e.Property(x => x.CloseReason).HasMaxLength(500);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.InterestPercent).HasPrecision(9, 4);
            e.Property(x => x.InterestAmount).HasPrecision(18, 2);
            e.Ignore(x => x.TotalRepayable);
            e.Property<uint>("xmin").HasColumnType("xid").IsRowVersion();
        });
        // ---- SASISHO17 ----
        modelBuilder.Entity<RoleChangeRequest>(e =>
        {
            e.HasIndex(x => new { x.GroupId, x.Status });
            e.Property(x => x.FromRole).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.ToRole).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.DecisionNote).HasMaxLength(500);
        });
        modelBuilder.Entity<MkobaStatementImport>(e =>
        {
            e.HasIndex(x => new { x.GroupId, x.Status });
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.FileHash).HasMaxLength(80);
            e.Property(x => x.FileName).HasMaxLength(200);
            e.Property(x => x.StorageKey).HasMaxLength(200);
            e.Property(x => x.DecisionNote).HasMaxLength(500);
            e.Property(x => x.LastBalance).HasPrecision(18, 2);
            e.Property<uint>("xmin").HasColumnType("xid").IsRowVersion();
        });
        modelBuilder.Entity<GroupResetRequest>(e =>
        {
            e.HasIndex(x => new { x.GroupId, x.Status });
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        });
        modelBuilder.Entity<LoginFailure>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.IpKey }).IsUnique();
            e.Property(x => x.IpKey).HasMaxLength(80);
        });
        modelBuilder.Entity<RefreshToken>(e =>
        {
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => new { x.UserId, x.FamilyId });
            e.Property(x => x.TokenHash).HasMaxLength(100);
            e.Property(x => x.CreatedByIp).HasMaxLength(64);
        });
        modelBuilder.Entity<BenefitLedgerEntry>(e =>
        {
            // Kila aina ya andiko mara MOJA kwa kesi (haliandikwi mara mbili)
            e.HasIndex(x => new { x.BenefitCaseId, x.Kind }).IsUnique();
            e.HasIndex(x => new { x.GroupId, x.BeneficiaryGroupMemberId });
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Reference).HasMaxLength(60);
        });
        modelBuilder.Entity<TruthSettingsProposal>(e =>
        {
            e.HasIndex(x => new { x.GroupId, x.Status });
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.PayloadJson).HasMaxLength(4000);
            e.Property(x => x.Note).HasMaxLength(500);
            e.Property(x => x.DecisionNote).HasMaxLength(500);
        });
        modelBuilder.Entity<MkobaStatementGap>(e =>
        {
            e.HasIndex(x => new { x.GroupId, x.BeforeReference }).IsUnique();
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.ResolutionStatus).HasConversion<string>();
        });
        modelBuilder.Entity<TruthAdjustmentRecord>(e =>
        {
            e.HasIndex(x => new { x.GroupId, x.Year, x.Month });
            e.Property(x => x.Kind).HasConversion<string>();
            e.Property(x => x.Status).HasConversion<string>();
        });
        modelBuilder.Entity<MembershipRequest>(e =>
        {
            e.HasIndex(x => new { x.GroupId, x.Status });
            e.HasIndex(x => x.UserId);
            e.Property(x => x.RequestedRole).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Note).HasMaxLength(500);
            e.Property(x => x.DecisionNote).HasMaxLength(500);
            // SASISHO21 (Sehemu ya 2): lango la kujiunga
            e.Property(x => x.PaymentCode).HasMaxLength(20);
            e.Property(x => x.PaymentAmount).HasPrecision(18, 2);
            e.Property(x => x.PaymentReference).HasMaxLength(80);
            e.HasIndex(x => new { x.GroupId, x.PaymentCode }).IsUnique()
                .HasFilter("\"PaymentCode\" IS NOT NULL");
        });
        modelBuilder.Entity<Group>(e =>
        {
            e.Property(x => x.ShortCode).HasMaxLength(6);
            e.Property(x => x.JoinCode).HasMaxLength(20);
            e.HasIndex(x => x.JoinCode).IsUnique().HasFilter("\"JoinCode\" IS NOT NULL");
        });
        // Namba ya uanachama ni ya kipekee ndani ya kikundi (tupu = bado haijatolewa)
        modelBuilder.Entity<GroupMember>()
            .HasIndex(m => new { m.GroupId, m.MemberNumber })
            .IsUnique()
            .HasFilter("\"MemberNumber\" <> ''");

        // Convert Enums to strings for PostgreSQL readability
        modelBuilder.Entity<Group>()
            .Property(g => g.OrganizationType)
            .HasConversion<string>();

        modelBuilder.Entity<GroupMember>()
            .Property(m => m.Role)
            .HasConversion<string>();

        modelBuilder.Entity<Account>()
            .Property(a => a.Type)
            .HasConversion<string>();

        modelBuilder.Entity<LedgerEntry>()
            .Property(l => l.Type)
            .HasConversion<string>();

        // ====================================================================
        // PHASE A (GroupSettings Owned Types Refactor): each module below is
        // an EF Core owned type mapped onto the SAME "GroupSettings" table
        // (table splitting, the default for OwnsOne with no ToTable call).
        // Every HasColumnName below that matches a name used in the old flat
        // mapping is intentional - it keeps the actual database column the
        // same one the old flat property used to own directly, so this is a
        // schema-compatible reshuffle, not a data migration, for every field
        // that already existed. Only the columns explicitly marked NEW below
        // require an actual AddColumn in the next migration.
        modelBuilder.Entity<GroupSettings>(entity =>
        {
            entity.OwnsOne(g => g.Financial, fs =>
            {
                fs.Property(f => f.JoiningFee)
                    .HasColumnName("JoiningFee").HasPrecision(18, 2);
                fs.Property(f => f.MinimumReserveBalance)
                    .HasColumnName("MinimumReserveBalance").HasPrecision(18, 2);
                // JoinFee config
                fs.OwnsOne(f => f.JoinFeeConfig, jfc =>
                {
                    jfc.Property(j => j.RequiredAmount).HasColumnName("JoinFeeRequiredAmount").HasPrecision(18, 2);
                    jfc.Property(j => j.Mode).HasColumnName("JoinFeeMode").HasConversion<string>();
                    jfc.Property(j => j.CaptureMode).HasColumnName("JoinFeeCaptureMode").HasConversion<string>();
                    jfc.Property(j => j.ApprovalThreshold).HasColumnName("JoinFeeApprovalThreshold").HasPrecision(18, 2);
                });
            });

            entity.OwnsOne(g => g.Contribution, cs =>
            {
                cs.Property(c => c.MonthlyContribution)
                    .HasColumnName("MonthlyContribution").HasPrecision(18, 2);
                cs.Property(c => c.DueDateDay)
                    .HasColumnName("DueDateDay");
                cs.Property(c => c.GracePeriodDays)
                    .HasColumnName("GracePeriodDays");
                cs.Property(c => c.LateFine)
                    .HasColumnName("LateFine").HasPrecision(18, 2);
                cs.Property(c => c.MinimumShortfallForFine)
                    .HasColumnName("MinimumShortfallForFine").HasPrecision(18, 2);
                // NEW columns (Compliance Engine, Ukonga Rules Specification
                // v1.2 Phase 1): was hardcoded "3" inside
                // ContributionComplianceBackgroundService, now per-group config.
                cs.Property(c => c.MaxConsecutiveMissedMonths)
                    .HasColumnName("MaxConsecutiveMissedMonths");
                // NEW column (ARCH-001, sehemu 4b): resolves which debt a new
                // payment closes - see DebtAllocationStrategy in Enums.cs.
                cs.Property(c => c.DebtAllocationStrategy)
                    .HasColumnName("DebtAllocationStrategy").HasConversion<string>();
                // NEW (Product Config Layer): how shortfall is handled
                cs.Property(c => c.ShortfallStrategy)
                    .HasColumnName("ShortfallStrategy").HasConversion<string>();
                cs.Property(c => c.ComplianceStrategy)
                    .HasColumnName("ComplianceStrategy").HasConversion<string>();
            });

            entity.OwnsOne(g => g.Event, es =>
            {
                es.Property(e => e.WelfareMode)
                    .HasColumnName("WelfareMode").HasConversion<string>();
                // NEW column: replaces the `fineAmount = 5000m` constant
                // that used to be hardcoded in WelfarePenaltyBackgroundService.
                es.Property(e => e.WelfareFineAmount)
                    .HasColumnName("WelfareFineAmount").HasPrecision(18, 2);
                // NEW column.
                es.Property(e => e.EventsEnabled)
                    .HasColumnName("EventsEnabled");
            });

            entity.OwnsOne(g => g.Loan, ls =>
            {
                ls.Property(l => l.InterestRate)
                    .HasColumnName("LoanInterestRate").HasPrecision(5, 2);
                // NEW columns.
                ls.Property(l => l.Enabled)
                    .HasColumnName("LoanEnabled");
                ls.Property(l => l.InterestType)
                    .HasColumnName("LoanInterestType").HasConversion<string>();
                ls.Property(l => l.RepaymentDays)
                    .HasColumnName("LoanRepaymentDays");
                ls.Property(l => l.LatePenaltyAmount)
                    .HasColumnName("LoanLatePenaltyAmount").HasPrecision(18, 2);
                ls.Property(l => l.MaxLoanMultiplier)
                    .HasColumnName("LoanMaxMultiplier").HasPrecision(5, 2);
                // NEW (Product Config Layer)
                ls.Property(l => l.LoanStrategy)
                    .HasColumnName("LoanStrategy").HasConversion<string>();
                ls.Property(l => l.GuarantorRequired)
                    .HasColumnName("GuarantorRequired");
                ls.Property(l => l.MinGuarantors)
                    .HasColumnName("MinGuarantors");
            });

            entity.OwnsOne(g => g.Governance, gs =>
            {
                // Existing columns - unchanged names, matches old behaviour.
                gs.OwnsOne(x => x.WithdrawalApproval, ap =>
                {
                    ap.Property(a => a.Mode)
                        .HasColumnName("WithdrawalApprovalMode").HasConversion<string>();
                    ap.Property(a => a.CustomRoles)
                        .HasColumnName("CustomApprovalRoles");
                    ap.Property(a => a.CustomRequiredApprovals)
                        .HasColumnName("CustomRequiredApprovals");
                });

                // NEW: replaces the hardcoded "Treasurer or Chairperson"
                // role checks in LoanService.IssueLoanAsync/MarkDefaultedAsync.
                // Not yet read by LoanService - wiring it up is Phase B/C,
                // once that service is deliberately touched.
                gs.OwnsOne(x => x.LoanApproval, ap =>
                {
                    ap.Property(a => a.Mode)
                        .HasColumnName("LoanApprovalMode").HasConversion<string>();
                    ap.Property(a => a.CustomRoles)
                        .HasColumnName("LoanApprovalCustomRoles");
                    ap.Property(a => a.CustomRequiredApprovals)
                        .HasColumnName("LoanApprovalCustomRequiredApprovals");
                });

                // NEW: replaces the hardcoded role check in EventController.
                gs.OwnsOne(x => x.EventApproval, ap =>
                {
                    ap.Property(a => a.Mode)
                        .HasColumnName("EventApprovalMode").HasConversion<string>();
                    ap.Property(a => a.CustomRoles)
                        .HasColumnName("EventApprovalCustomRoles");
                    ap.Property(a => a.CustomRequiredApprovals)
                        .HasColumnName("EventApprovalCustomRequiredApprovals");
                });

                // NEW: replaces the hardcoded role checks in MkobaImportController.
                gs.OwnsOne(x => x.ImportApproval, ap =>
                {
                    ap.Property(a => a.Mode)
                        .HasColumnName("ImportApprovalMode").HasConversion<string>();
                    ap.Property(a => a.CustomRoles)
                        .HasColumnName("ImportApprovalCustomRoles");
                    ap.Property(a => a.CustomRequiredApprovals)
                        .HasColumnName("ImportApprovalCustomRequiredApprovals");
                });
            });
        });

        modelBuilder.Entity<LedgerEntry>()
            .Property(l => l.Amount).HasPrecision(18, 2);

        // NEW: precision + duplicate-prevention for withdrawal records.
        modelBuilder.Entity<WalletWithdrawal>()
            .Property(w => w.Amount).HasPrecision(18, 2);

        // Prevent importing the exact same withdrawal row twice.
        modelBuilder.Entity<WalletWithdrawal>()
            .HasIndex(w => new { w.GroupId, w.ReferenceNo })
            .IsUnique();

        // SECURITY/INTEGRITY FIX (audit 12.1 / 3.3: "Account unique
        // constraint haipo" - AccountResolverService's "find or create"
        // logic had a race window where two concurrent requests could
        // both fail to find an existing account and both create one,
        // leaving a member with two Savings accounts (or two Loan
        // accounts, etc.) with balances split between them. This is now
        // enforced at the database level, not just in application code -
        // a second concurrent insert will fail with a DB constraint
        // violation instead of silently succeeding.
        modelBuilder.Entity<Account>()
            .HasIndex(a => new { a.GroupMemberId, a.Type })
            .IsUnique();

        // NEW (ChamaLink v1 scope): structured withdrawal/expenditure records.
        modelBuilder.Entity<Withdrawal>()
            .Property(w => w.Amount).HasPrecision(18, 2);

        // Who approved / who recorded are both GroupMembers, but a
        // GroupMember can appear as either (or both) on many withdrawals -
        // restrict cascade delete so removing a member doesn't wipe the
        // group's expenditure history.
        modelBuilder.Entity<Withdrawal>()
            .HasOne(w => w.ApprovedByGroupMember)
            .WithMany()
            .HasForeignKey(w => w.ApprovedByGroupMemberId)
            // ApprovedByGroupMemberId is nullable now (Sprint 2: a
            // withdrawal starts life Pending, with no approver yet), so
            // SetNull rather than Restrict.
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Withdrawal>()
            .HasOne(w => w.RecordedByGroupMember)
            .WithMany()
            .HasForeignKey(w => w.RecordedByGroupMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Withdrawal>()
            .HasOne(w => w.WalletWithdrawal)
            .WithMany()
            .HasForeignKey(w => w.WalletWithdrawalId)
            .OnDelete(DeleteBehavior.SetNull);

        // NEW (Reports Engine gap: Benefits Received linkage)
        modelBuilder.Entity<Withdrawal>()
            .HasOne(w => w.BeneficiaryGroupMember)
            .WithMany()
            .HasForeignKey(w => w.BeneficiaryGroupMemberId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Withdrawal>()
            .HasOne(w => w.GroupEvent)
            .WithMany()
            .HasForeignKey(w => w.GroupEventId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Withdrawal>()
            .Property(w => w.Status).HasConversion<string>();

        // NOTE (Phase A): WithdrawalApprovalMode / WelfareMode string
        // conversions used to be configured here directly. They are now
        // [NotMapped] pass-through properties - the real, persisted
        // conversions live on Governance.WithdrawalApproval.Mode and
        // Event.WelfareMode in the GroupSettings owned-type setup above.

        // NEW (Withdrawal Governance gap): one approval/rejection vote
        // per member per withdrawal - a leader cannot vote twice.
        modelBuilder.Entity<WithdrawalApproval>()
            .Property(a => a.RoleAtDecision).HasConversion<string>();
        modelBuilder.Entity<WithdrawalApproval>()
            .HasIndex(a => new { a.WithdrawalId, a.GroupMemberId })
            .IsUnique();
        modelBuilder.Entity<WithdrawalApproval>()
            .HasOne(a => a.Withdrawal)
            .WithMany(w => w.Approvals)
            .HasForeignKey(a => a.WithdrawalId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<WithdrawalApproval>()
            .HasOne(a => a.GroupMember)
            .WithMany()
            .HasForeignKey(a => a.GroupMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        // NEW (Reports Engine gap: Loan Portfolio / Member Financial Profile)
        modelBuilder.Entity<Loan>().Property(l => l.PrincipalAmount).HasPrecision(18, 2);
        modelBuilder.Entity<Loan>().Property(l => l.InterestRate).HasPrecision(5, 2);
        modelBuilder.Entity<Loan>().Property(l => l.InterestAmount).HasPrecision(18, 2);
        modelBuilder.Entity<Loan>().Property(l => l.AmountRepaid).HasPrecision(18, 2);
        modelBuilder.Entity<Loan>().Property(l => l.Status).HasConversion<string>();
        modelBuilder.Entity<Loan>().HasIndex(l => new { l.GroupId, l.Status });
        modelBuilder.Entity<Loan>().Property(l => l.DisbursementReference).HasMaxLength(60);
        modelBuilder.Entity<Loan>().Property(l => l.DefaultReason).HasMaxLength(500);
        modelBuilder.Entity<Loan>().HasIndex(l => new { l.GroupId, l.DisbursementReference }).IsUnique().HasFilter("\"DisbursementReference\" IS NOT NULL");
        modelBuilder.Entity<Loan>()
            .HasOne(l => l.GroupMember)
            .WithMany()
            .HasForeignKey(l => l.GroupMemberId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Loan>()
            .HasOne(l => l.IssuedByGroupMember)
            .WithMany()
            .HasForeignKey(l => l.IssuedByGroupMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        // NEW (Sprint 2 gap: Event Beneficiary Tracking)
        modelBuilder.Entity<GroupEvent>()
            .Property(e => e.TargetAmountPerMember).HasPrecision(18, 2);
        modelBuilder.Entity<GroupEvent>()
            .HasOne(e => e.BeneficiaryGroupMember)
            .WithMany()
            .HasForeignKey(e => e.BeneficiaryGroupMemberId)
            .OnDelete(DeleteBehavior.SetNull);

        // NEW (Sprint 1 gap: Fine Entity)
        modelBuilder.Entity<Fine>().Property(f => f.Amount).HasPrecision(18, 2);
        modelBuilder.Entity<Fine>().Property(f => f.AmountPaid).HasPrecision(18, 2);
        modelBuilder.Entity<Fine>().Property(f => f.Status).HasConversion<string>();
        modelBuilder.Entity<Fine>().Property(f => f.ReasonType).HasConversion<string>();

        // SECURITY/INTEGRITY FIX (audit 12.3 / 6.2: "Fine uniqueness
        // haipo" - the old index below was not unique, so it only helped
        // query performance; ContributionComplianceBackgroundService's
        // "check AnyAsync, then create" pattern still had a race window
        // where two overlapping runs (or two app instances) could both
        // pass the check and both issue a Fine for the same member/period.
        // Period is only ever set for monthly-late fines and GroupEventId
        // is only ever set for welfare/event fines (see FineService), so
        // one plain composite unique index would not work: Postgres
        // treats NULLs as distinct, meaning a NULL Period would never
        // collide with another NULL Period, and a NULL GroupEventId would
        // never collide with another NULL GroupEventId. So this is split
        // into two partial unique indexes, one per fine "family", each
        // only active on the rows where its key column is actually set.
        modelBuilder.Entity<Fine>()
            .HasIndex(f => new { f.GroupMemberId, f.Period, f.ReasonType })
            .IsUnique()
            .HasFilter("\"Period\" IS NOT NULL");
        modelBuilder.Entity<Fine>()
            .HasIndex(f => new { f.GroupMemberId, f.GroupEventId, f.ReasonType })
            .IsUnique()
            .HasFilter("\"GroupEventId\" IS NOT NULL");

        modelBuilder.Entity<Fine>()
            .HasOne(f => f.GroupEvent)
            .WithMany()
            .HasForeignKey(f => f.GroupEventId)
            .OnDelete(DeleteBehavior.SetNull);

        // NEW (Sprint 1 gap: Debt Table)
        modelBuilder.Entity<Debt>().Property(d => d.Amount).HasPrecision(18, 2);
        modelBuilder.Entity<Debt>().Property(d => d.AmountCleared).HasPrecision(18, 2);
        modelBuilder.Entity<Debt>().Property(d => d.Status).HasConversion<string>();

        // SECURITY/INTEGRITY FIX (audit 12.2 / 6.2: "Debt uniqueness
        // haipo" - same race window as Fine above: this index used to be
        // non-unique, so it could not stop two overlapping background-job
        // runs from both recording a shortfall Debt for the same member
        // for the same month. Debt.Period is never null (every Debt is a
        // specific month's missed contribution), so a single unique
        // composite index is enough here - no partial-index split needed.
        modelBuilder.Entity<Debt>()
            .HasIndex(d => new { d.GroupMemberId, d.Period })
            .IsUnique();

        // NEW (Sprint 2 gap: Event Contribution Tracking)
        modelBuilder.Entity<EventContribution>().Property(e => e.ExpectedAmount).HasPrecision(18, 2);
        modelBuilder.Entity<EventContribution>().Property(e => e.PaidAmount).HasPrecision(18, 2);
        modelBuilder.Entity<EventContribution>().Property(e => e.Status).HasConversion<string>();
        modelBuilder.Entity<EventContribution>()
            .HasIndex(e => new { e.GroupEventId, e.GroupMemberId }).IsUnique();

        // SASISHO18 — wajibu wa kila mwanachama kwa kila tukio.
        // UNIQUE(BenefitCaseId, GroupMemberId) ndiyo kinga kuu: tukio moja
        // haliwezi kumkata mwanachama mara mbili, hata kama idhini itarudiwa.
        modelBuilder.Entity<EventObligation>(o =>
        {
            o.Property(x => x.RequiredAmount).HasPrecision(18, 2);
            o.Property(x => x.DeductedAmount).HasPrecision(18, 2);
            o.Property(x => x.Status).HasConversion<string>();
            o.Ignore(x => x.OutstandingAmount);
            o.HasIndex(x => new { x.BenefitCaseId, x.GroupMemberId }).IsUnique();
            o.HasIndex(x => new { x.GroupId, x.GroupMemberId, x.Status });
            // SASISHO21 — kodi ya malipo: ya kipekee ndani ya kikundi (NULL nyingi huruhusiwa).
            o.Property(x => x.PaymentCode).HasMaxLength(PaymentCodeMaxLength);
            o.HasIndex(x => new { x.GroupId, x.PaymentCode }).IsUnique();
            o.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
            o.HasOne(x => x.GroupMember).WithMany().HasForeignKey(x => x.GroupMemberId).OnDelete(DeleteBehavior.Cascade);
        });

        // ================= SASISHO20 — msimamizi wa mfumo =================
        modelBuilder.Entity<PlatformAdmin>(e =>
        {
            e.HasIndex(x => x.UserId).IsUnique();
            e.Property(x => x.Level).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.TotpSecretProtected).HasMaxLength(400);
            e.Property(x => x.PendingTotpSecretProtected).HasMaxLength(400);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PlatformAuditEvent>(e =>
        {
            e.Property(x => x.Action).HasMaxLength(60);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.DetailJson).HasMaxLength(2000);
            e.Property(x => x.Ip).HasMaxLength(64);
            e.HasIndex(x => new { x.GroupId, x.At });
            e.HasIndex(x => x.At);
            e.HasIndex(x => new { x.PlatformAdminId, x.At });
        });

        modelBuilder.Entity<PlatformApproval>(e =>
        {
            e.Property(x => x.Action).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.DecisionNote).HasMaxLength(500);
            e.Property(x => x.PayloadJson).HasMaxLength(1000);
            e.HasIndex(x => new { x.Status, x.Action });
            e.HasIndex(x => new { x.GroupId, x.Status });
        });

        modelBuilder.Entity<PlatformPasswordResetCode>(e =>
        {
            e.Property(x => x.CodeHash).HasMaxLength(64);
            e.Property(x => x.Salt).HasMaxLength(32);
            e.HasIndex(x => new { x.UserId, x.UsedAt });
            e.HasIndex(x => x.ExpiresAt);
        });

        // NEW (Sprint 1 gap #18: whole-statement duplicate import protection)
        modelBuilder.Entity<ImportedStatement>()
            .HasIndex(s => new { s.GroupId, s.FileHash }).IsUnique();

        // NEW (Ukonga Rules Specification v1.2, sehemu 6 / Phase 4)
        modelBuilder.Entity<ComplianceSnapshot>().Property(c => c.ExpectedContribution).HasPrecision(18, 2);
        modelBuilder.Entity<ComplianceSnapshot>().Property(c => c.PaidContribution).HasPrecision(18, 2);
        modelBuilder.Entity<ComplianceSnapshot>().Property(c => c.FineIssuedAmount).HasPrecision(18, 2);
        modelBuilder.Entity<ComplianceSnapshot>().Property(c => c.FinePaidAmount).HasPrecision(18, 2);
        modelBuilder.Entity<ComplianceSnapshot>().Property(c => c.OutstandingFineAmount).HasPrecision(18, 2);
        modelBuilder.Entity<ComplianceSnapshot>().Property(c => c.OutstandingContributionDebt).HasPrecision(18, 2);
        modelBuilder.Entity<ComplianceSnapshot>().Property(c => c.Status).HasConversion<string>();

        // One snapshot per member per month (sehemu 6: "engine inaandika
        // snapshot MOJA kwa kila mwanachama, kila mwezi") - the service
        // upserts against this, it never appends duplicates for the same
        // (member, month).
        modelBuilder.Entity<ComplianceSnapshot>()
            .HasIndex(c => new { c.GroupMemberId, c.Month })
            .IsUnique();

        modelBuilder.Entity<ComplianceSnapshot>()
            .HasOne(c => c.GroupMember)
            .WithMany()
            .HasForeignKey(c => c.GroupMemberId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ComplianceSnapshot>()
            .HasOne(c => c.Group)
            .WithMany()
            .HasForeignKey(c => c.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        // Primary Key for GroupSettings (1-to-1 relationship with Group)
        modelBuilder.Entity<GroupSettings>()
            .HasKey(g => g.GroupId);

        // Prevent duplicate User registration in the same Group
        modelBuilder.Entity<GroupMember>()
            .HasIndex(m => new { m.GroupId, m.UserId })
            .IsUnique();

        // SECURITY/INTEGRITY FIX (audit 8.4: "Loan repayment ina
        // concurrency risk" + implicit race in WithdrawalService.DecideAsync
        // where two leaders approving/rejecting at the same instant both
        // read the same Approvals snapshot): both Loan and Withdrawal now
        // carry an optimistic concurrency token. On PostgreSQL the
        // idiomatic token is the built-in "xmin" system column - every
        // row already has one and it changes on every UPDATE, so no new
        // column/migration DDL is needed, EF just needs to know to check
        // it. If two requests load the same Loan/Withdrawal and both try
        // to save, the second SaveChangesAsync now throws
        // DbUpdateConcurrencyException instead of silently overwriting
        // the first request's change (e.g. two simultaneous repayments
        // both approving because both read the same stale
        // OutstandingBalance) - see LoanController/WithdrawalController
        // for how that exception is surfaced to the caller.
        //
        // UPDATED: Npgsql marked the old UseXminAsConcurrencyToken()
        // helper obsolete (CS0618) in favour of configuring it through
        // EF Core's own standard IsRowVersion() API. This is a like-for-
        // like replacement, not a schema change - IsRowVersion() just
        // expands to ValueGeneratedOnAddOrUpdate() + IsConcurrencyToken(),
        // which is exactly what the old helper produced under the hood,
        // still mapped onto the same "xmin" system column. No migration
        // is needed: the resulting model is identical to before.
        modelBuilder.Entity<Loan>()
            .Property<uint>("xmin")
            .HasColumnType("xid")
            .IsRowVersion();

        modelBuilder.Entity<Withdrawal>()
            .Property<uint>("xmin")
            .HasColumnType("xid")
            .IsRowVersion();

        // Optimize query performance for Ledger entries
        modelBuilder.Entity<LedgerEntry>()
            .HasIndex(l => new { l.GroupId, l.AccountId });

        // V2 — Financial OS foundation
        modelBuilder.Entity<GroupPolicy>().Property(p => p.MonthlyContribution).HasPrecision(18, 2);
        modelBuilder.Entity<GroupPolicy>().Property(p => p.JoiningFee).HasPrecision(18, 2);
        modelBuilder.Entity<GroupPolicy>().Property(p => p.LateFine).HasPrecision(18, 2);
        modelBuilder.Entity<GroupPolicy>().Property(p => p.LoanInterestRate).HasPrecision(5, 2);
        modelBuilder.Entity<GroupPolicy>().Property(p => p.LoanLatePenaltyAmount).HasPrecision(18, 2);
        modelBuilder.Entity<GroupPolicy>().Property(p => p.LoanMaxMultiplier).HasPrecision(5, 2);
        modelBuilder.Entity<GroupPolicy>().Property(p => p.WelfareFineAmount).HasPrecision(18, 2);
        modelBuilder.Entity<GroupPolicy>().Property(p => p.AllocationStrategy).HasConversion<string>();
        modelBuilder.Entity<GroupPolicy>().Property(p => p.ShortfallStrategy).HasConversion<string>();
        modelBuilder.Entity<GroupPolicy>().Property(p => p.ComplianceStrategy).HasConversion<string>();
        modelBuilder.Entity<GroupPolicy>().Property(p => p.DebtAllocationStrategy).HasConversion<string>();
        modelBuilder.Entity<GroupPolicy>().Property(p => p.LoanInterestType).HasConversion<string>();
        modelBuilder.Entity<GroupPolicy>().Property(p => p.LoanStrategy).HasConversion<string>();
        modelBuilder.Entity<GroupPolicy>().Property(p => p.WelfareMode).HasConversion<string>();
        modelBuilder.Entity<GroupPolicy>().Property(p => p.WithdrawalApprovalMode).HasConversion<string>();
        modelBuilder.Entity<GroupPolicy>().Property(p => p.LoanApprovalMode).HasConversion<string>();
        modelBuilder.Entity<GroupPolicy>().Property(p => p.EventApprovalMode).HasConversion<string>();
        modelBuilder.Entity<GroupPolicy>().Property(p => p.ImportApprovalMode).HasConversion<string>();
        // JoinFee config versioning
        modelBuilder.Entity<GroupPolicy>().Property(p => p.JoinFeeMode).HasConversion<string>();
        modelBuilder.Entity<GroupPolicy>().Property(p => p.JoinFeeCaptureMode).HasConversion<string>();
        modelBuilder.Entity<GroupPolicy>().Property(p => p.JoinFeeApprovalThreshold).HasPrecision(18, 2);
        modelBuilder.Entity<GroupPolicy>().HasIndex(p => new { p.GroupId, p.Version }).IsUnique();
        modelBuilder.Entity<GroupPolicy>().HasIndex(p => new { p.GroupId, p.EffectiveFrom });

        modelBuilder.Entity<FinancialEvent>().Property(e => e.Amount).HasPrecision(18, 2);
        modelBuilder.Entity<FinancialEvent>().Property(e => e.Type).HasConversion<string>();
        modelBuilder.Entity<FinancialEvent>().Property(e => e.Source).HasConversion<string>();
        modelBuilder.Entity<FinancialEvent>().Property(e => e.Status).HasConversion<string>();
        modelBuilder.Entity<FinancialEvent>().Property(e => e.EvidenceType).HasConversion<string>().HasMaxLength(30);
        modelBuilder.Entity<FinancialEvent>().Property(e => e.EvidenceReference).HasMaxLength(200);
        modelBuilder.Entity<FinancialEvent>().HasIndex(e => new { e.GroupId, e.MemberId });
        modelBuilder.Entity<FinancialEvent>().HasIndex(e => e.CorrelationId);
        modelBuilder.Entity<FinancialEvent>().HasIndex(e => e.Status);

        modelBuilder.Entity<AuditEvent>().Property(e => e.Action).HasConversion<string>();
        modelBuilder.Entity<AuditEvent>().Property(e => e.Source).HasConversion<string>();
        modelBuilder.Entity<AuditEvent>().HasIndex(e => new { e.GroupId, e.MemberId });
        modelBuilder.Entity<AuditEvent>().HasIndex(e => e.CorrelationId);
        modelBuilder.Entity<AuditEvent>().HasIndex(e => e.Timestamp);
        modelBuilder.Entity<AuditEvent>().HasIndex(e => new { e.GroupId, e.ChainIndex }).IsUnique().HasFilter("\"ChainIndex\" IS NOT NULL");
        modelBuilder.Entity<AuditEvent>().Property(e => e.PrevHash).HasMaxLength(64);
        modelBuilder.Entity<AuditEvent>().Property(e => e.Hash).HasMaxLength(64);

        modelBuilder.Entity<WelfareEvent>().Property(e => e.RequiredContributionPerMember).HasPrecision(18, 2);
        modelBuilder.Entity<WelfareEvent>().Property(e => e.TotalExpected).HasPrecision(18, 2);
        modelBuilder.Entity<WelfareEvent>().Property(e => e.TotalCollected).HasPrecision(18, 2);
        modelBuilder.Entity<WelfareEvent>().Property(e => e.TotalDisbursed).HasPrecision(18, 2);
        modelBuilder.Entity<WelfareEvent>().Property(e => e.Type).HasConversion<string>();
        modelBuilder.Entity<WelfareEvent>().Property(e => e.Status).HasConversion<string>();
        modelBuilder.Entity<WelfareEvent>().Property(e => e.CollectionMethod).HasConversion<string>();

        modelBuilder.Entity<WelfareObligation>().Property(o => o.RequiredAmount).HasPrecision(18, 2);
        modelBuilder.Entity<WelfareObligation>().Property(o => o.PaidAmount).HasPrecision(18, 2);
        modelBuilder.Entity<WelfareObligation>().Property(o => o.Status).HasConversion<string>();
        modelBuilder.Entity<WelfareObligation>().HasIndex(o => new { o.WelfareEventId, o.MemberId }).IsUnique();

        modelBuilder.Entity<WelfareContribution>().Property(c => c.Amount).HasPrecision(18, 2);
        modelBuilder.Entity<WelfareDisbursement>().Property(d => d.Amount).HasPrecision(18, 2);

        modelBuilder.Entity<MemberStatusHistory>().Property(h => h.FromStatus).HasConversion<string>();
        modelBuilder.Entity<MemberStatusHistory>().Property(h => h.ToStatus).HasConversion<string>();
        modelBuilder.Entity<MemberStatusHistory>().HasIndex(h => h.MemberId);
    }
}