# DOMAIN MODEL V2 — ChamaLink Financial OS

## 1. Organization
```
Organization {
  Id: Guid
  Name: string (e.g. "Ukonga Mkoba")
  Code: string (e.g. "UKG")
  Type: OrganizationType (Mkoba/Vicoba/Saccos/SavingsClub/Other)
  CreatedAt: DateTime
  Status: OrganizationStatus (Active/Inactive/Archived)
}
```

**OrganizationType:**
- Mkoba = 1 (Monthly contribution based)
- Vicoba = 2 (Share based)
- Saccos = 3 (Deposit based)
- SavingsClub = 4 (Simple savings)
- Other = 5

## 2. Group (Ndani ya Organization)
```
Group {
  Id: Guid
  OrganizationId: Guid
  Name: string
  Code: string
  ContributionModel: ContributionModel (Monthly/EventBased/Hybrid)
  CurrentPolicyVersion: int
  CreatedAt: DateTime
}
```

**ContributionModel — Identity ya kikundi:**
- Monthly: Wanachama wanachangia fixed amount kila mwezi (e.g. 10k/month)
- EventBased: Wanachama wanachangia tu event ikitokea (msiba, harusi) — hakuna monthly target
- Hybrid: Monthly + EventBased

## 3. GroupPolicy — Business Rules (Versioned)
```
GroupPolicy {
  Id: Guid
  GroupId: Guid
  Version: int (1,2,3...)
  EffectiveFrom: DateTime (e.g. 2026-01-01)
  EffectiveTo: DateTime? (null = current)
  IsActive: bool
  
  // Monthly Contribution Rules
  MonthlyContribution: decimal (e.g. 10000)
  JoiningFee: decimal (e.g. 50000)
  LateFine: decimal (e.g. 5000)
  GracePeriodDays: int (e.g. 5)
  MaxConsecutiveMissedMonths: int (e.g. 3)
  
  // Allocation Strategy
  AllocationStrategy: AllocationStrategy (ContributionFirst/RepaymentFirst/Custom)
  AllocationOrder: List<AllocationTarget> (configurable order)
  
  // Shortfall Strategy
  ShortfallStrategy: ShortfallStrategy (FineImmediately/UseSavingsThenFine/DebtOnly)
  
  // Compliance Strategy
  ComplianceStrategy: ComplianceStrategy (MonthlyTarget/CumulativeTarget/Custom)
  
  // Loan Rules
  LoanEnabled: bool
  LoanInterestRate: decimal
  LoanInterestType: Flat/Reducing
  LoanMaxMultiplier: decimal?
  LoanStrategy: DirectIssue/ApplicationApproval/GuarantorRequired
  GuarantorRequired: bool
  MinGuarantors: int
  LoanLatePenalty: decimal
  
  // Welfare Rules
  WelfareEnabled: bool
  WelfareFineAmount: decimal
  
  // Governance
  WithdrawalApprovalMode: ApprovalMode
  LoanApprovalMode: ApprovalMode
  EventApprovalMode: ApprovalMode
  ImportApprovalMode: ApprovalMode
  
  CreatedBy: Guid (UserId)
  CreatedAt: DateTime
  Reason: string (e.g. "Annual policy review 2027")
}
```

**Mfano wa Versioning:**
```
Policy V1: Effective 2026-01-01, MonthlyContribution=10,000, Fine=5,000
Policy V2: Effective 2027-01-01, MonthlyContribution=20,000, Fine=2,000

Report ya Dec 2026 → inatumia V1
Report ya Jan 2027 → inatumia V2
Hakuna report ya 2026 inayobadilika baada ya V2 kuingia.
```

## 4. Member
```
Member {
  Id: Guid (GroupMember Id)
  GroupId: Guid
  UserId: Guid
  MembershipNumber: string (UKG-0001, immutable, unique per group, never reused)
  FullName: string
  Phone: string
  JoinDate: DateTime
  Status: MemberStatus (Active/Inactive/Suspended/Exited/Deceased/Archived)
  Role: GroupRole (Chairperson/Treasurer/Secretary/Member)
  StatusHistory: List<MemberStatusHistory>
}

MemberStatusHistory {
  Id: Guid
  MemberId: Guid
  FromStatus: MemberStatus
  ToStatus: MemberStatus
  Reason: string
  ChangedBy: Guid
  ChangedAt: DateTime
}
```

**Membership Number Rules:**
- Format: {GroupCode}-{0001} e.g. UKG-0001
- Unique per group
- Never reused (hata akitoka)
- Immutable (haibadiliki maisha)
- Generated automatically on enrollment

## 5. Financial Event — Kila kitu ni Event
```
FinancialEvent {
  Id: Guid
  GroupId: Guid
  MemberId: Guid? (null for group-level events)
  Type: FinancialEventType
  Amount: decimal
  OccurredAt: DateTime
  CreatedBy: Guid (UserId)
  Source: EventSource (Manual/MKobaImport/ExcelImport/System/BackgroundJob)
  SourceReference: string? (e.g. Import Batch #42, FileHash, TransactionId)
  PolicyVersion: int (which GroupPolicy version was active)
  Metadata: JSON (extra data per event type)
  AuditTrailId: Guid
}

FinancialEventType:
- PaymentReceived (member paid money)
- ContributionCharged (monthly contribution due)
- ContributionPaid (allocation result)
- LoanIssued
- LoanRepaymentDue
- LoanRepaymentPaid
- FineCharged
- FinePaid
- JoiningFeeCharged
- JoiningFeePaid
- SavingsDeposited
- SavingsWithdrawn
- SavingsHeld (collateral hold)
- SavingsReleased (collateral release)
- WelfareEventCreated
- WelfareObligationCreated
- WelfareContributionPaid
- WelfareDisbursement
- DebtCreated
- DebtCleared
```

## 6. LedgerEntry — Single Source of Truth
```
LedgerEntry {
  Id: Guid
  GroupId: Guid
  MemberId: Guid
  AccountId: Guid (Savings/Loan/Fine/SocialFund)
  FinancialEventId: Guid (source event)
  Type: TransactionType (Contribution/LoanDisbursement/LoanRepayment/FineIssue/FinePayment/ShareOut/WelfareDeduction/WelfareTopUp/EventContribution/JoiningFee)
  Amount: decimal (positive = CR, negative = DR — au separate CR/DR columns)
  BalanceAfter: decimal (snapshot after this entry)
  CreatedAt: DateTime
  CreatedBy: Guid
  PolicyVersion: int
  AuditTrailId: Guid
}

Account {
  Id: Guid
  GroupMemberId: Guid
  Type: AccountType (Savings/Loan/Fine/SocialFund)
  Balance: decimal (derived, not source — sum of LedgerEntries)
  HeldBalance: decimal (portion held as collateral)
  AvailableBalance: Balance - HeldBalance
}
```

**Kanuni:** Balance ni derived tu — `SUM(LedgerEntries)`. Hakuna balance update moja kwa moja.

## 7. Welfare Domain (Tazama 05_WELFARE_DOMAIN_SPEC.md)
```
WelfareEvent, WelfareObligation, WelfareContribution, WelfareDisbursement
```

## 8. Audit Trail (Tazama 06_AUDIT_TRAIL_SPEC.md)
```
AuditEvent — kila FinancialEvent ina AuditTrail
```

## 9. Relationships
```
Organization 1—* Group
Group 1—* GroupPolicy (versioned)
Group 1—* Member
Member 1—* FinancialEvent
FinancialEvent 1—* LedgerEntry
Member 1—* Account (Savings, Loan, Fine, SocialFund)
Member 1—* MemberStatusHistory
Group 1—* WelfareEvent
WelfareEvent 1—* WelfareObligation
WelfareObligation 1—* WelfareContribution
WelfareEvent 1—* WelfareDisbursement
```

## 10. Non-Goals ya Domain Model V2
- Hakuna VICOBA share logic bado (Phase 6)
- Hakuna SACCOS interest logic bado (Phase 6)
- Hakuna Mobile App logic (Phase 7)
- Hakuna AI/Analytics (Phase 8)

Domain Model V2 inalenga Mkoba Monthly + EventBased + Hybrid tu, lakini imejengwa ili VICOBA/SACCOS ziweze kuongezwa bila breaking change.
