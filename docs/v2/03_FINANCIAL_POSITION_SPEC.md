# FINANCIAL POSITION SPEC — Real-Time, Si Report
## Taarifa Viongozi Watauliza Kila Siku

### Kwa nini Financial Position ni muhimu kuliko Monthly Statement?
- Monthly Statement: "Frank March alilipa nini?" — ya mwezi
- Financial Position: "Frank leo ana nini?" — ya leo, real-time

Viongozi hawatauliza matrix kila siku. Watauliza: "Frank ana akiba ngapi? Ana deni gani? Net position yake ni nini?"

### 1. Financial Position Entity (Real-Time, Derived from Ledger)

```
MemberFinancialPosition {
  MemberId: Guid
  MembershipNumber: string (UKG-0001)
  FullName: string
  AsOf: DateTime (real-time, e.g. 2026-09-20 14:32)
  PolicyVersion: int (current active policy)
  
  // Savings
  SavingsBalance: decimal (SUM of Savings LedgerEntries)
  HeldSavings: decimal (portion held as collateral for active loans)
  AvailableSavings: SavingsBalance - HeldSavings
  
  // Contribution
  TotalExpectedContributions: decimal (from JoinDate to AsOf, based on Policy versions)
  TotalPaidContributions: decimal (SUM of ContributionPaid ledger)
  ContributionDebt: TotalExpected - TotalPaid (if positive)
  ContributionCompliancePercent: TotalPaid / TotalExpected * 100
  
  // Joining Fee
  JoiningFeeTarget: decimal (from Policy)
  JoiningFeePaid: decimal (SUM of JoiningFeePaid ledger)
  JoiningFeeBalance: JoiningFeeTarget - JoiningFeePaid
  
  // Loan
  TotalLoansIssued: decimal (SUM of LoanIssued)
  TotalLoansRepaid: decimal (SUM of LoanRepaymentPaid)
  OutstandingLoan: TotalIssued - TotalRepaid
  OutstandingLoanInterest: calculated
  NextLoanDueDate: DateTime?
  DaysPastDue: int?
  LoanRiskStatus: Normal/Warning/Defaulted
  
  // Fine
  TotalFinesCharged: decimal (SUM of FineCharged)
  TotalFinesPaid: decimal (SUM of FinePaid)
  OutstandingFine: TotalCharged - TotalPaid
  
  // Welfare
  TotalWelfareObligations: decimal (SUM of WelfareObligationCreated)
  TotalWelfareContributions: decimal (SUM of WelfareContributionPaid)
  WelfareBalance: Obligations - Contributions
  TotalWelfareBenefitsReceived: decimal (SUM of WelfareDisbursement where beneficiary)
  
  // Net Position
  NetPosition: AvailableSavings - ContributionDebt - JoiningFeeBalance - OutstandingLoan - OutstandingFine - WelfareBalance
  // Positive = ana akiba zaidi ya madeni, Negative = ana deni
  
  // Calculated At
  CalculatedAt: DateTime
  CalculatedFromLedgerUpTo: DateTime
}
```

### 2. Available Savings vs Held Savings — Muhimu Sana

**Tatizo bila hii:**
```
Savings Balance = 300,000
Member anaomba kutoa 300,000
Mfumo unamruhusu — lakini ana loan active 500,000
Kikundi kina-risk — akishindwa kulipa loan, hakuna collateral
```

**Suluhisho na Held Savings:**
```
Savings Balance = 300,000
Loan Collateral Hold = 150,000 (50% ya outstanding loan, au fixed per policy)
Available Savings = 150,000

Member anaomba kutoa 200,000 → System: "Unaweza kutoa max 150,000 tu, 150,000 imehold kama collateral ya loan yako"
```

**Kanuni za Held Savings:**
```
HeldSavings = SUM(SavingsHeld ledger) - SUM(SavingsReleased ledger)

When LoanIssued 500,000:
  - Policy inasema CollateralPercent = 30%
  - HeldSavings += 150,000 (30% of 500k)
  - Ledger: SavingsHeld 150,000

When LoanRepaymentPaid 100,000:
  - HeldSavings -= proportionally (e.g. 30k released)
  - Ledger: SavingsReleased 30,000

When Loan fully repaid:
  - HeldSavings = 0
  - All collateral released
```

**Example:**
```
Frank:
Savings Balance: 300,000
Held Savings: 150,000 (collateral for loan 500k)
Available Savings: 150,000

Frank anataka kutoa 200k:
  → Denied, max 150k
Frank anataka kutoa 100k:
  → Approved, Savings 300k→200k, Held 150k, Available 50k
```

### 3. Contribution Debt Calculation

```
TotalExpectedContributions = 
  For each month from JoinDate to AsOf:
    Get PolicyVersion active that month
    Sum MonthlyContribution from that Policy

TotalPaidContributions = SUM(LedgerEntries where Type=ContributionPaid)

ContributionDebt = max(0, TotalExpected - TotalPaid)

Example:
  JoinDate: 2026-01-15
  Policy V1 (2026-01-01): 10k/month
  Policy V2 (2027-01-01): 20k/month
  AsOf: 2027-03-15
  
  Expected:
    2026: Jan(10k) + Feb(10k) + ... + Dec(10k) = 120k
    2027: Jan(20k) + Feb(20k) + Mar(20k) = 60k
    Total Expected = 180k
  
  Paid: 150k (from ledger)
  Debt = 30k
```

### 4. Net Position — Nambari Moja ya Kuelewa Kila Kitu

```
NetPosition = AvailableSavings 
              - ContributionDebt 
              - JoiningFeeBalance 
              - OutstandingLoan 
              - OutstandingFine 
              - WelfareBalance

Positive Net = Member ana akiba zaidi ya madeni (mzuri)
Negative Net = Member ana deni zaidi ya akiba (hatari)

Example Frank:
  AvailableSavings: 150,000
  ContributionDebt: 0
  JoiningFeeBalance: 10,000
  OutstandingLoan: 350,000
  OutstandingFine: 0
  WelfareBalance: 5,000

  Net = 150k - 0 - 10k - 350k - 0 - 5k = -215,000 (negative, ana deni)
```

Viongozi wataangalia Net Position kwanza — kama ni negative sana, hawampatii loan nyingine.

### 5. Financial Position Engine — How It Works

```
FinancialPositionEngine {
  CalculatePosition(MemberId, AsOfDate): MemberFinancialPosition
  
  Steps:
  1. Get all LedgerEntries for Member up to AsOfDate
  2. Get all GroupPolicy versions effective up to AsOfDate
  3. Calculate each component from ledger (SUM)
  4. Calculate HeldSavings from SavingsHeld/Released events
  5. Calculate Available = Balance - Held
  6. Calculate Expected Contributions from Policy history
  7. Calculate Net Position
  8. Return position with timestamp
}

Caching: Position inahesabiwa kila mara kutoka ledger — hakuna stored balance. Kwa performance, tunaweza ku-cache kwa 5 min, lakini source ni ledger.
```

### 6. Edge Cases

**Q: Member ana Savings 0, lakini ana ContributionDebt 10k na ShortfallStrategy=UseSavingsThenFine — tufanye nini?**
A: Savings 0, so UseSavings fails, so FineCharged 5k (per policy) na DebtCreated 10k.

**Q: Member ana loan 500k, Savings 100k, Held 50k, Available 50k — akishindwa kulipa loan, tunaweza kuchukua Held Savings?**
A: Yes, kwa Policy inayosema CollateralCanBeUsedForDefault=true, system ita-create event SavingsHeldUsedForLoanRepayment.

**Q: Net Position inapaswa kuwa real-time au monthly?**
A: Real-time — kila payment ikishafika ledger, position inabadilika. Hii ndiyo tofauti na Monthly Statement (ya mwezi).

### 7. Success Criteria
- Hakuna stored balance — kila kitu SUM(Ledger)
- Available Savings inaheshimu Held Savings
- Net Position inaeleweka na viongozi kwa sekunde 1
- Position inabadilika real-time baada ya kila payment
- Policy versioning inaheshimiwa kwenye Expected Contributions
