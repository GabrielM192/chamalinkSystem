# ALLOCATION ENGINE SPEC — Executor wa Business Rules, Si Moyo
## One Engine, One Ledger, Many Reports

### Kwa nini Allocation Engine si moyo?
Moyo ni Business Rule Engine (GroupPolicy). Allocation Engine ni executor tu — ina-execute rules.

```
Business Rules (GroupPolicy + Policy Versioning)
  ↓
Financial Event (PaymentReceived 30k)
  ↓
Allocation Engine (Executor — ina-apply rules)
  ↓
AllocationResult (Contribution 10k, Loan 15k, Savings 5k)
  ↓
Ledger Entries (3 entries)
  ↓
Financial Position (updated real-time)
  ↓
Reports (Read Only)
```

### 1. Allocation Engine — Input/Output

```
AllocationEngine {
  Allocate(PaymentReceivedEvent, MemberFinancialPosition, GroupPolicy): AllocationResult
}

Input:
  PaymentReceivedEvent {
    MemberId: Guid
    Amount: decimal (e.g. 30,000)
    ReceivedAt: DateTime (e.g. 2026-06-15)
    Source: MKobaImport/ExcelImport/Manual
    PolicyVersion: int (active policy at ReceivedAt)
  }
  
  MemberFinancialPosition {
    ContributionDebt: decimal (e.g. 0)
    OutstandingLoan: decimal (e.g. 350,000)
    NextLoanDue: decimal (e.g. 15,000 for June)
    OutstandingFine: decimal (e.g. 0)
    JoiningFeeBalance: decimal (e.g. 10,000)
    WelfareBalance: decimal (e.g. 5,000)
    SavingsBalance: decimal
    HeldSavings: decimal
    AvailableSavings: decimal
  }
  
  GroupPolicy {
    MonthlyContribution: 10,000
    JoiningFee: 50,000
    LateFine: 5,000
    AllocationStrategy: ContributionFirst/RepaymentFirst/Custom
    AllocationOrder: [Contribution, LoanRepayment, Fine, JoiningFee, Debt, Savings] (configurable)
    ShortfallStrategy: UseSavingsThenFine etc.
    ComplianceStrategy: MonthlyTarget etc.
  }

Output:
  AllocationResult {
    PaymentReceived: 30,000
    Allocations: [
      { Target: Contribution, Amount: 10,000, Policy: V1, Month: June 2026 },
      { Target: LoanRepayment, Amount: 15,000, LoanId: ..., DueDate: June 2026 },
      { Target: Savings, Amount: 5,000 }
    ]
    Unallocated: 0 (excess — goes to savings per default, or held per policy)
    Status: Paid (fully covered target) / PartiallyPaid / Overpaid
    TargetForMonth: 25,000 (Contribution 10k + Loan 15k)
    PaidForMonth: 25,000
    SavingsAdded: 5,000
    DebtCleared: 0
    FinePaid: 0
    JoiningFeePaid: 0
    CorrelationId: Guid (links to AuditEvent and LedgerEntries)
  }
}
```

### 2. Allocation Strategies — Configurable per Group

**Strategy A: ContributionFirst (default kwa Mkoba)**
```
Order: Contribution → LoanRepayment → Fine → JoiningFee → Debt → Savings

Example Frank:
  Payment: 30,000
  Target: Contribution 10k + Loan 15k = 25k
  Allocation:
    Contribution 10k (covers June contribution)
    LoanRepayment 15k (covers June loan due)
    Savings 5k (excess)
  Status: Paid
  Savings Added: 5k
```

**Strategy B: RepaymentFirst (kwa vikundi vinavyo-prioritize loan)**
```
Order: LoanRepayment → Contribution → Fine → JoiningFee → Debt → Savings

Example Frank:
  Payment: 20,000 (less than target 25k)
  Allocation:
    LoanRepayment 15k (loan first)
    Contribution 5k (remaining)
    Contribution shortfall 5k → DebtCreated 5k per ShortfallStrategy
  Status: PartiallyPaid
```

**Strategy C: Custom (kiongozi apange)**
```
Order: [Fine, Contribution, LoanRepayment, JoiningFee, Debt, Savings] (e.g. fine kwanza)

Example: Member ana fine 5k na contribution 10k, analipa 10k
  Allocation:
    Fine 5k (fine first per custom)
    Contribution 5k
    Contribution shortfall 5k → Debt
  Status: PartiallyPaid
```

### 3. Allocation na ShortfallStrategy

```
ShortfallStrategy ina-affect nini kitatokea kama payment haitoshi:

FineImmediately:
  Payment 5k, Target 10k → Shortfall 5k
  → DebtCreated 5k
  → FineCharged 5k (per LateFine)
  → Status: PartiallyPaid

UseSavingsThenFine:
  Payment 0, Target 10k, Savings 20k → Shortfall 10k
  → SavingsHeld 10k used? Au SavingsDeposited negative?
  → ContributionPaid 10k via savings
  → Status: CoveredBySavings
  → Savings 20k→10k

  Payment 0, Target 10k, Savings 5k → Shortfall 10k, Savings 5k only
  → ContributionPaid 5k via savings
  → Debt 5k
  → Fine 5k (savings haitoshi)
  → Status: PartiallyPaid

DebtOnly:
  Payment 0, Target 10k → Shortfall 10k
  → DebtCreated 10k
  → No fine, no savings deduction
  → Status: Missed
```

### 4. Allocation na Policy Versioning

```
PaymentReceived date ina-determine PolicyVersion:

PaymentReceived: 2026-12-31 23:59, Amount 10k
Policy V1 Effective 2026-01-01: Contribution 10k
→ Allocation uses V1 → Target 10k → Paid

PaymentReceived: 2027-01-01 00:01, Amount 10k
Policy V2 Effective 2027-01-01: Contribution 20k
→ Allocation uses V2 → Target 20k → Shortfall 10k → Debt 10k

Hakuna payment ya 2026 inayobadilika baada ya V2 kuingia — PolicyVersion ina-lock.
```

### 5. AllocationResult → Financial Events → Ledger

```
AllocationResult ina-create FinancialEvents:

AllocationResult {
  Contribution: 10k → FinancialEvent ContributionPaid 10k → LedgerEntry CR Contribution 10k
  LoanRepayment: 15k → FinancialEvent LoanRepaymentPaid 15k → LedgerEntry CR LoanRepayment 15k
  Savings: 5k → FinancialEvent SavingsDeposited 5k → LedgerEntry CR Savings 5k
}

Kila FinancialEvent ina:
  - AuditTrailId (same CorrelationId)
  - PolicyVersion
  - Source (PaymentReceived event Id)
  - AllocationResult reference

Kila LedgerEntry ina:
  - FinancialEventId
  - AuditTrailId
  - CorrelationId (same for all 3 entries from one payment)
```

### 6. Example Kamili — Frank

```
Input:
  PaymentReceived: 30,000, Date: 2026-06-15, Member: Frank (UKG-0001), Source: MKoba Batch #42
  Financial Position:
    ContributionDebt: 0
    OutstandingLoan: 350,000, NextDue: 15,000 (June)
    OutstandingFine: 0
    JoiningFeeBalance: 0
    Savings: 20,000, Held: 0, Available: 20,000
  Policy V1: MonthlyContribution 10k, AllocationStrategy ContributionFirst, ShortfallStrategy UseSavingsThenFine

AllocationEngine.Allocate:

  Target for June: Contribution 10k + Loan 15k = 25k
  Payment: 30k

  Step 1: Contribution 10k (covers June contribution)
    Remaining: 20k
  Step 2: LoanRepayment 15k (covers June loan)
    Remaining: 5k
  Step 3: Fine 0 (no outstanding fine)
  Step 4: JoiningFee 0
  Step 5: Debt 0
  Step 6: Savings 5k (excess)

  Result:
    ContributionPaid: 10k
    LoanRepaymentPaid: 15k
    SavingsDeposited: 5k
    Status: Paid (Paid 25k >= Target 25k)
    SavingsAdded: 5k
    Unallocated: 0

FinancialEvents Created (same CorrelationId abc-123):
  - ContributionPaid 10k (June 2026)
  - LoanRepaymentPaid 15k (LoanId, Due June)
  - SavingsDeposited 5k

LedgerEntries Created (same CorrelationId):
  - CR Contribution 10k, BalanceAfter: Contribution total 60k (Jan-June)
  - CR LoanRepayment 15k, BalanceAfter: Loan repaid 90k, Outstanding 260k
  - CR Savings 5k, BalanceAfter: Savings 25k

Financial Position Updated (real-time):
  Savings Balance: 20k→25k
  Available: 25k
  ContributionDebt: 0
  OutstandingLoan: 350k→335k
  NetPosition: 25k - 0 - 335k = -310k (bado negative, lakini improved)

AuditEvent:
  Action: PaymentReceived
  Actor: Treasurer Peter, Batch #42
  Amount: 30k
  Allocation: { Contribution 10k, Loan 15k, Savings 5k }
  CorrelationId: abc-123
  PolicyVersion: 1

Monthly Statement June:
  Member: UKG-0001 Frank
  Target: 25,000
  Paid: 30,000
  Contribution: 10,000
  LoanRepayment: 15,000
  Savings Added: 5,000
  Status: Paid
```

### 7. Edge Cases

**Q: Payment 5k, Target 25k (10k contribution + 15k loan), Strategy ContributionFirst, Shortfall UseSavingsThenFine, Savings 20k?**
A:
  Contribution 5k (partial), shortfall 5k → Use savings 5k → Contribution fully paid via savings + payment
  Loan 0 → shortfall 15k → Use savings? Savings bado 15k (20k-5k) → Loan 15k via savings
  Result: Contribution 10k (5k payment +5k savings), Loan 15k (savings), Status CoveredBySavings, Savings 20k→0k

**Q: Payment 0, Target 10k, Savings 0, Shortfall FineImmediately?**
A: Debt 10k, Fine 5k, Status Missed

**Q: Payment 100k, Target 25k?**
A: Contribution 10k, Loan 15k, Savings 75k excess, Status Overpaid (au Paid + Savings 75k)

### 8. Success Criteria
- One AllocationEngine, used by all importers and manual payments
- AllocationStrategy configurable per group (ContributionFirst/RepaymentFirst/Custom)
- ShortfallStrategy configurable (FineImmediately/UseSavingsThenFine/DebtOnly)
- PolicyVersion respected — payment date locks policy version
- AllocationResult → FinancialEvents → LedgerEntries with same CorrelationId
- No duplicate allocation logic anywhere else
- Tests: 20+ scenarios including Frank 30k→25k target→5k savings, partial payments, zero payments, overpayments, savings covering, fine logic
