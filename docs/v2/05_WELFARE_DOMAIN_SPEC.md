# WELFARE DOMAIN SPEC — Lifecycle Kamili
## Sehemu Inayodharauwa Lakini Ina Leta Migogoro Mingi

### Tatizo la Sasa
Welfare ilikuwa:
```
GroupEvent (title, target per member) + EventContribution (paid)
```
Hakuna Obligation, hakuna Disbursement, hakuna Collection Method, hakuna audit.

Matokeo: "Mary alichangia nini kwenye msiba wa John?" — hakuna jibu la uhakika.

### 1. Welfare Domain Entities — 4 Entities Rasmi

```
WelfareEvent {
  Id: Guid
  GroupId: Guid
  Title: string (e.g. "Death of Parent — John")
  Description: string
  Type: WelfareType (Death/Hospitalization/Marriage/Birth/Disaster/Emergency/Other)
  BeneficiaryMemberId: Guid? (member aliyepata tatizo — e.g. John)
  BeneficiaryName: string (e.g. "John's Parent" — kama si member)
  RequiredContributionPerMember: decimal (e.g. 10,000)
  TotalExpected: decimal (Required * Active Members count at creation)
  TotalCollected: decimal (derived from contributions)
  TotalDisbursed: decimal (derived from disbursements)
  Balance: TotalCollected - TotalDisbursed
  Status: WelfareEventStatus (Draft/Open/Collecting/Collected/Disbursing/Closed/Cancelled)
  CollectionMethod: WelfareCollectionMethod (DeductFromSavings/ManualContribution/AutoDeduct)
  CreatedBy: Guid
  CreatedAt: DateTime
  DeadlineDate: DateTime? (e.g. contributions due by 2026-09-30)
  PolicyVersion: int
}

WelfareType:
- Death = 1 (msiba)
- Hospitalization = 2
- Marriage = 3
- Birth = 4
- Disaster = 5 (mafuri, moto)
- Emergency = 6
- Other = 7

WelfareEventStatus:
- Draft = 1 (inaandaliwa, bado haija-open)
- Open = 2 (imefunguliwa, obligations zinatengenezwa)
- Collecting = 3 (wanachama wanalipa)
- Collected = 4 (kiasi kimekusanywa, tayari kwa disbursement)
- Disbursing = 5 (inamlipa beneficiary)
- Closed = 6 (imefungwa, disbursed, balanced)
- Cancelled = 7 (imefutwa, contributions returned per policy)

WelfareCollectionMethod:
- DeductFromSavings = 1 (kata moja kwa moja kutoka savings — haraka)
- ManualContribution = 2 (member analipa manually — kama mchango wa kawaida)
- AutoDeduct = 3 (system inakata automatically on next payment)
```

```
WelfareObligation {
  Id: Guid
  WelfareEventId: Guid
  MemberId: Guid (nani anatakiwa kulipa)
  RequiredAmount: decimal (e.g. 10,000)
  PaidAmount: decimal (derived from contributions)
  OutstandingAmount: Required - Paid
  Status: ObligationStatus (Pending/PartiallyPaid/Paid/Waived/Exempted)
  CreatedAt: DateTime
  DueDate: DateTime?
  WaivedBy: Guid? (nani alisamehe)
  WaivedReason: string?
}

ObligationStatus:
- Pending = 1
- PartiallyPaid = 2
- Paid = 3
- Waived = 4 (e.g. member ni beneficiary mwenyewe, au Exempted per policy)
- Exempted = 5 (e.g. Suspended members, au beneficiary)
```

```
WelfareContribution {
  Id: Guid
  WelfareObligationId: Guid
  WelfareEventId: Guid
  MemberId: Guid
  Amount: decimal
  PaidAt: DateTime
  PaidBy: Guid (UserId)
  Source: ContributionSource (Manual/SavingsDeduction/AutoDeduct/Import)
  FinancialEventId: Guid (link to FinancialEvent → Ledger)
  LedgerEntryId: Guid
  AuditTrailId: Guid
}

ContributionSource:
- Manual = 1
- SavingsDeduction = 2
- AutoDeduct = 3
- Import = 4
```

```
WelfareDisbursement {
  Id: Guid
  WelfareEventId: Guid
  BeneficiaryMemberId: Guid?
  BeneficiaryName: string
  Amount: decimal
  DisbursedAt: DateTime
  DisbursedBy: Guid (Treasurer)
  Method: DisbursementMethod (Cash/MobileMoney/BankTransfer)
  Reference: string? (e.g. M-Pesa transaction id)
  FinancialEventId: Guid
  LedgerEntryId: Guid
  Status: DisbursementStatus (Pending/Approved/Disbursed/Failed)
  ApprovedBy: Guid? (Chairperson per governance)
  AuditTrailId: Guid
}
```

### 2. Welfare Lifecycle — Hatua 7

```
STEP 1: Event Created
  Treasurer/Secretary creates WelfareEvent
  Title: "Death of Parent — John"
  Type: Death
  Beneficiary: John (Member)
  Required: 10,000 per member
  CollectionMethod: DeductFromSavings
  Status: Draft

STEP 2: Event Opened → Obligations Created
  Governance approval (Chairperson approves)
  Status: Draft → Open
  System creates WelfareObligation for each Active member (e.g. 30 members × 10k = 300k expected)
  Exception: Beneficiary (John) is Exempted per policy (BeneficiaryExempted=true)
  So 29 obligations × 10k = 290k expected

STEP 3: Collection
  Status: Open → Collecting
  Per CollectionMethod:
    DeductFromSavings: System auto creates FinancialEvent WelfareContributionPaid for each member, deducts from savings, creates ledger
    ManualContribution: Members pay manually, Treasurer records, creates FinancialEvent

STEP 4: Tracking
  Each WelfareContribution updates WelfareObligation.PaidAmount
  WelfareEvent.TotalCollected = SUM(Contributions)

STEP 5: Collected → Ready for Disbursement
  When TotalCollected >= TotalExpected * CollectionThreshold (e.g. 90% per policy)
  Status: Collecting → Collected

STEP 6: Disbursement
  Treasurer creates WelfareDisbursement 290,000 to John
  Governance: Chairperson approves disbursement (per WithdrawalApprovalMode)
  Status: Collected → Disbursing
  System creates FinancialEvent WelfareDisbursement, ledger entries (DR Welfare pot, CR Beneficiary)

STEP 7: Closed
  After disbursement confirmed
  Status: Disbursing → Closed
  Balance = TotalCollected - TotalDisbursed (should be 0 or small per policy)
  Audit trail complete
```

### 3. Example — Msiba wa Mzazi wa John

```
Group: Ukonga, 30 Active Members
WelfareEvent:
  Title: Death of Parent — John
  Beneficiary: John (UKG-0005)
  Required: 10,000 per member
  Expected: 30 × 10k = 300k, but John exempted → 290k
  CollectionMethod: DeductFromSavings
  Status: Draft

→ Opened (Chairperson approves)
→ Obligations: 29 × 10k

Member A (UKG-0001): Obligation 10k, Paid 10k (Deducted from savings 50k→40k), Status Paid
Member B (UKG-0002): Obligation 10k, Paid 0, Status Pending
...
Member 29: Paid

TotalCollected: 270k (27 members paid, 2 pending)
Status: Collecting

→ 2 more pay → TotalCollected 290k → Status Collected

Disbursement: 290k to John
  Method: M-Pesa, Ref: MP123456
  ApprovedBy: Chairperson
  Status: Disbursed

WelfareEvent: Closed, Balance 0
John: Received 290k welfare benefit, Savings 40k→330k (if disbursed to savings) or cash

Report:
  Welfare Report for Event:
    Member | Required | Paid | Outstanding | Status
    UKG-0001 | 10k | 10k | 0 | Paid
    UKG-0002 | 10k | 0 | 10k | Pending
    ...
    Total: Required 290k, Collected 290k, Disbursed 290k, Balance 0
```

### 4. Welfare na Financial Position

```
WelfareObligations ina-affect Financial Position:

MemberFinancialPosition:
  WelfareBalance = TotalObligations - TotalContributions (outstanding welfare debt)
  NetPosition includes WelfareBalance

Example Frank:
  Savings 150k, WelfareBalance 20k (2 events × 10k pending) → Net = 150k - 20k - ... = lower
```

### 5. Edge Cases

**Q: Beneficiary ni member mwenyewe — analipa pia?**
A: Per Policy BeneficiaryExempted=true, beneficiary ha-lipi. Obligation yake ni Exempted.

**Q: Member ame-Exit na ana WelfareObligation pending?**
A: Per Policy, Exit settlement ina-deduct WelfareBalance kutoka savings return.

**Q: WelfareEvent ina-collect 290k lakini disbursement ni 250k tu — 40k inabaki?**
A: Per Policy WelfareSurplusHandling: ReturnToMembers / KeepAsGroupSavings / Donate. Audit trail inarekodi decision.

**Q: CollectionMethod DeductFromSavings lakini member hana savings?**
A: Per ShortfallStrategy: Create Debt, au Fine, per GroupPolicy.

### 6. Success Criteria
- WelfareEvent, Obligation, Contribution, Disbursement ni entities rasmi
- Lifecycle 7 steps ina-audit trail
- CollectionMethod configurable
- Beneficiary exempted logic per policy
- Welfare ina-affect Financial Position na Net Position
- Reports zinaonyesha Required/Paid/Outstanding per member per event
