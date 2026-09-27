# AUDIT TRAIL SPEC — Kila Shilingi Inaelezeka
## Muhimu Kuliko Reports Zote

### Kwa nini Audit Trail ni muhimu kuliko Reports?
Reports ni matokeo. Audit Trail ni **ushahidi** wa matokeo. Bila audit trail, reports ni nambari tu — haziaminiki kwenye mkutano wa wanachama wala kwenye kesi.

Mwenyekiti akisema:
> "Kwa nini Frank anaonekana amelipa 30,000 June? Mimi sijaona pesa"

Mfumo lazima ujibu kwa sekunde 1:
```
PaymentReceived: 30,000
Date: 2026-06-15 14:32:18
User: Treasurer Peter (UKG-0002)
Source: M-Koba Import Batch #42, File: mkoba_june.csv, Hash: abc123
Import By: Treasurer Peter
Policy Version: V1 (Effective 2026-01-01, Contribution 10k)
Allocation: Contribution 10k, LoanRepayment 15k, Savings 5k
Ledger Entries: 3 created (IDs: ...)
Status: Success
```

### 1. AuditEvent Entity

```
AuditEvent {
  Id: Guid
  GroupId: Guid
  MemberId: Guid? (affected member)
  ActorId: Guid (UserId — nani alifanya)
  ActorRole: GroupRole (Chairperson/Treasurer/Secretary/Member/System)
  ActorMembershipNumber: string (e.g. UKG-0002)
  
  Action: AuditAction
  EntityType: string (e.g. FinancialEvent, LedgerEntry, GroupPolicy, WelfareEvent, MemberStatus)
  EntityId: Guid (Id ya entity iliyobadilika)
  
  Timestamp: DateTime (when)
  EffectiveDate: DateTime (when inafanya kazi — e.g. payment date vs entry date)
  
  BeforeValue: JSON? (snapshot before change)
  AfterValue: JSON (snapshot after change)
  Changes: JSON (diff — e.g. { "MonthlyContribution": { "From": 10000, "To": 20000 } })
  
  Source: AuditSource
  SourceReference: string? (e.g. Import Batch #42, FileHash, TransactionId, M-Pesa Ref)
  SourceMetadata: JSON? (e.g. { "FileName": "mkoba_june.csv", "RowNumber": 15, "RawData": "..." })
  
  PolicyVersion: int? (which policy version was active)
  AllocationResult: JSON? (for PaymentReceived events)
  
  Reason: string? (why — e.g. "Annual policy review", "Voluntary exit", "Death certificate #123")
  IpAddress: string? (for security)
  UserAgent: string? (for security)
  
  IsSystemGenerated: bool (true for background jobs, false for human)
  CorrelationId: Guid (link related audit events — e.g. one payment creates 3 ledger entries with same CorrelationId)
}

AuditAction:
- Created = 1
- Updated = 2
- Deleted = 3
- PaymentReceived = 10
- ContributionCharged = 11
- ContributionPaid = 12
- LoanIssued = 20
- LoanRepaymentPaid = 21
- FineCharged = 30
- FinePaid = 31
- JoiningFeeCharged = 40
- JoiningFeePaid = 41
- SavingsDeposited = 50
- SavingsWithdrawn = 51
- SavingsHeld = 52
- SavingsReleased = 53
- WelfareEventCreated = 60
- WelfareObligationCreated = 61
- WelfareContributionPaid = 62
- WelfareDisbursement = 63
- MemberStatusChanged = 70
- GroupPolicyCreated = 80
- GroupPolicyVersioned = 81
- GovernanceApproved = 90
- GovernanceRejected = 91
- ImportStarted = 100
- ImportCompleted = 101
- ImportFailed = 102

AuditSource:
- Manual = 1 (human via UI)
- MKobaImport = 2
- ExcelImport = 3
- System = 4 (background job)
- BackgroundJob = 5 (WelfarePenalty, ContributionCompliance, LoanPenalty)
- API = 6
- Migration = 7
```

### 2. Audit Trail kwa Kila Financial Event

```
FinancialEvent {
  Id: Guid
  AuditTrailId: Guid (link to AuditEvent)
  ...
}

LedgerEntry {
  Id: Guid
  FinancialEventId: Guid
  AuditTrailId: Guid
  ...
}

Example: Frank pays 30,000

1 AuditEvent: PaymentReceived
  Actor: Treasurer Peter
  Source: MKobaImport Batch #42
  Amount: 30,000
  Timestamp: 2026-06-15 14:32
  CorrelationId: abc-123

3 LedgerEntries with same CorrelationId abc-123:
  - ContributionPaid 10k (AuditTrailId same, FinancialEventId = PaymentReceived event)
  - LoanRepaymentPaid 15k
  - SavingsDeposited 5k

Kila LedgerEntry ina AuditTrailId yake pia, lakini CorrelationId ina-link zote 3 + AuditEvent ya PaymentReceived.

Uki-search CorrelationId abc-123, unaona kila kitu kilichotokea kwa payment hiyo moja.
```

### 3. Audit Trail kwa GroupPolicy Versioning

```
GroupPolicy V1 → V2

AuditEvent:
  Action: GroupPolicyVersioned
  Actor: Chairperson John
  BeforeValue: { "MonthlyContribution": 10000, "Fine": 5000, "Version": 1 }
  AfterValue: { "MonthlyContribution": 20000, "Fine": 2000, "Version": 2 }
  Reason: "Annual policy review 2027 — members agreed in meeting 2026-12-15"
  Timestamp: 2026-12-15 16:00
  PolicyVersion: 2

Hii ina-allow audit: Nani alibadilisha contribution kutoka 10k kwenda 20k? Kwa nini? Lini?
```

### 4. Audit Trail kwa Member Status

```
MemberStatusHistory ina AuditEvent pia:

AuditEvent:
  Action: MemberStatusChanged
  MemberId: Frank (UKG-0001)
  Actor: System (BackgroundJob — ContributionComplianceBackgroundService)
  FromStatus: Active
  ToStatus: Warning
  Reason: "Missed 2 consecutive contributions (Feb, Mar), Max is 3 per Policy V1"
  Timestamp: 2026-04-01 00:00 (background job)
  IsSystemGenerated: true

AuditEvent:
  Action: MemberStatusChanged
  MemberId: Frank
  Actor: Treasurer Peter
  FromStatus: NonActive
  ToStatus: Active
  Reason: "Paid all debts + fine, receipt #123"
  Timestamp: 2026-06-01 10:15
  IsSystemGenerated: false
```

### 5. Audit Trail kwa Welfare

```
WelfareEvent Created:

AuditEvent:
  Action: WelfareEventCreated
  Actor: Secretary Mary
  Entity: WelfareEvent (Death of Parent — John)
  AfterValue: { "Title": "Death of Parent", "Beneficiary": "John", "Required": 10000, ... }
  Reason: "John's parent passed away 2026-09-10, death certificate attached"
  Source: Manual

WelfareDisbursement:

AuditEvent:
  Action: WelfareDisbursement
  Actor: Treasurer Peter
  ActorRole: Treasurer
  Amount: 290,000
  Beneficiary: John
  Method: M-Pesa, Ref: MP123456
  ApprovedBy: Chairperson John (Governance)
  Timestamp: 2026-09-15 14:00
```

### 6. Querying Audit Trail — Viongozi Wanauliza Maswali

```
Q: "Frank alilipa lini 30k June?"
A: Search AuditEvent where MemberId=Frank, Action=PaymentReceived, EffectiveDate between 2026-06-01 and 2026-06-30
→ Returns: 2026-06-15 14:32, Batch #42, Treasurer Peter, Allocation...

Q: "Nani alibadilisha contribution kutoka 10k kwenda 20k?"
A: Search AuditEvent where Action=GroupPolicyVersioned, GroupId=Ukonga
→ Returns: Chairperson John, 2026-12-15, Reason: Annual review

Q: "Kwa nini Frank alikuwa NonActive May?"
A: Search AuditEvent where MemberId=Frank, Action=MemberStatusChanged, ToStatus=NonActive
→ Returns: System, Missed 3 months, Policy V1 Max=3, Timestamp 2026-05-01

Q: "Welfare ya John ilikusanya kiasi gani?"
A: Search AuditEvent where EntityType=WelfareEvent, EntityId=John's event, Action=WelfareContributionPaid
→ SUM Amount = 290k, 29 members

Q: "Import ya June ilikuwa na duplicate?"
A: Search AuditEvent where Source=MKobaImport, SourceReference=Batch #42
→ Shows: FileHash abc123, RowCount 50, DuplicateCheck: 0 duplicates, ImportedBy Peter
```

### 7. Immutability na Retention

```
AuditEvent ni immutable — haihaririwi, haifutwi.
- Hakuna UPDATE kwenye AuditEvents table
- Hakuna DELETE
- Insert only

Retention: Forever — audit trail ya miaka 10 lazima ibaki.

Performance: Partition by year/month, index by GroupId, MemberId, Action, Timestamp, CorrelationId.
```

### 8. Success Criteria
- Kila FinancialEvent ina AuditTrailId na CorrelationId
- Kila LedgerEntry ina AuditTrailId
- Kila GroupPolicy version change ina Before/After na Reason
- Kila MemberStatus change ina Reason na Actor
- Kila WelfareEvent ina audit ya kila step
- AuditEvent ni immutable, insert-only
- Viongozi wanaweza kujibu "Kwa nini?" kwa kila shilingi ndani ya sekunde 10 kwa ku-search audit trail
