# BUSINESS RULE ENGINE SPEC + POLICY VERSIONING
## Moyo wa ChamaLink — Chanzo cha Ukweli

### Kwa nini Business Rule Engine ndiyo moyo, si Allocation Engine?
Allocation Engine ni executor tu. Kama Rules ni za uongo, Allocation itatoa taarifa za uongo. Kwa hiyo Rules ndiyo chanzo.

```
Business Rules (GroupPolicy)
  ↓
Allocation Engine (Executor)
  ↓
Ledger
  ↓
Financial Position
  ↓
Reports
```

### 1. Business Rules Wizard — Maswali 10 ya Kiongozi

Kiongozi anapounda group, mfumo usimuingize moja kwa moja. Kwanza amjibu maswali — wizard:

**Q1: Monthly Contribution?**
```
Input: 10,000
Help: Kiasi ambacho kila mwanachama anatakiwa kuchangia kila mwezi
Default: 10,000
Validation: >0
```

**Q2: Joining Fee?**
```
Input: 50,000
Help: Ada ya kujiunga — inalipwa mara moja
Default: 50,000
Validation: >=0 (0 = hakuna joining fee)
```

**Q3: Late Fine?**
```
Input: 5,000
Help: Faini ya kuchelewa kuchangia
Default: 5,000
```

**Q4: Grace Period?**
```
Input: 5 days
Help: Siku ngapi baada ya due date kabla ya fine
Default: 5
```

**Q5: Max Missed Months?**
```
Input: 3
Help: Miezi mingapi akishindwa kuchangia ndipo awe NonActive
Default: 3
```

**Q6: Payment Allocation Strategy?**
```
Options:
- ContributionFirst (default): Mchango kwanza, kisha loan, kisha fine, kisha joining fee, kisha debt, kisha savings
- RepaymentFirst: Loan kwanza, kisha mchango, kisha fine, etc.
- Custom: Kiongozi apange order mwenyewe

Default: ContributionFirst
```

**Q7: Shortfall Strategy — Akikosa mchango, tufanye nini?**
```
Options:
- FineImmediately: Piga fine moja kwa moja
- UseSavingsThenFine (default kwa vikundi vingi): Kata kwenye akiba kwanza, kama haitoshi ndipo fine
- DebtOnly: Unda debt tu, hakuna fine wala kukata akiba

Default: UseSavingsThenFine
Example:
  Member anadaiwa 10k, ana akiba 20k
  FineImmediately → Fine 5k, akiba inabaki 20k, debt 10k
  UseSavingsThenFine → Akiba 20k→10k, debt 0, fine 0
  DebtOnly → Debt 10k, akiba 20k, fine 0
```

**Q8: Loan Repayment Priority?**
```
Options:
- ContributionFirst: Mchango kwanza, kisha loan
- RepaymentFirst: Loan kwanza, kisha mchango
- ProRata: Gawanya proportionally

Default: ContributionFirst (kwa Mkoba)
```

**Q9: Welfare Rules?**
```
WelfareEnabled: true/false
WelfareFineAmount: 5,000 (fine ya kutolipa welfare)
WelfareMode: DeductBalance / ContributePot
```

**Q10: Governance Rules?**
```
WithdrawalApproval: TreasurerOnly / ChairpersonOnly / TreasurerAndChairperson / Custom
LoanApproval: CustomApproval (Treasurer+Chairperson, 1 required) etc.
EventApproval: CustomApproval
ImportApproval: CustomApproval
```

Baada ya maswali 10, mfumo utengeneze `GroupPolicy V1` automatically.

### 2. GroupPolicy Entity — Versioned

```
GroupPolicy {
  Version: int
  EffectiveFrom: DateTime
  EffectiveTo: DateTime? (null = current)
  IsActive: bool
  CreatedBy: Guid
  Reason: string
  
  // Rules (kama Q1-Q10)
  MonthlyContribution, JoiningFee, LateFine, GracePeriodDays, MaxMissedMonths,
  AllocationStrategy, ShortfallStrategy, ComplianceStrategy,
  LoanEnabled, LoanInterestRate, LoanStrategy, etc.
}
```

### 3. Policy Versioning — Muhimu Kuliko Yote

**Tatizo bila versioning:**
```
2026: Contribution = 10,000
2027: Contribution = 20,000

Mwanachama akifungua report ya Dec 2026 mwaka 2027, ataona Target 20,000 badala ya 10,000 — REPORT YA UONGO, AUDIT FAILURE.
```

**Suluhisho na versioning:**
```
Policy V1: Effective 2026-01-01, Contribution=10k, Fine=5k, CreatedBy=Chairperson, Reason="Initial policy"
Policy V2: Effective 2027-01-01, Contribution=20k, Fine=2k, Reason="Annual review"

LedgerEntry ya Dec 2026 ina PolicyVersion=1
LedgerEntry ya Jan 2027 ina PolicyVersion=2

Report ya Dec 2026 → inasoma V1 → Target 10k (sahihi milele)
Report ya Jan 2027 → inasoma V2 → Target 20k (sahihi)

Hakuna report ya zamani inayobadilika.
```

**Kanuni za Versioning:**
1. Policy mpya hai-futi ya zamani — inaweka EffectiveTo ya zamani na inaunda V mpya
2. Kila FinancialEvent na LedgerEntry ina PolicyVersion
3. Reports zina-join na PolicyVersion ya wakati huo
4. PolicyVersion ni immutable — haihaririwi, ina-undwa mpya tu
5. Audit trail ya nani alibadilisha policy na kwa nini

**SQL Example:**
```sql
-- Policy V1
INSERT INTO GroupPolicies (GroupId, Version, EffectiveFrom, MonthlyContribution, ...) 
VALUES ('ukonga-id', 1, '2026-01-01', 10000, ...);

-- 2027, kiongozi anabadilisha
UPDATE GroupPolicies SET EffectiveTo='2026-12-31' WHERE GroupId='ukonga-id' AND Version=1;
INSERT INTO GroupPolicies (GroupId, Version, EffectiveFrom, MonthlyContribution, ...)
VALUES ('ukonga-id', 2, '2027-01-01', 20000, ...);
```

### 4. Business Rule Engine — Executor

```
BusinessRuleEngine {
  GetActivePolicy(GroupId, Date): GroupPolicy
  ValidatePolicy(Policy): ValidationResult
  CalculateTarget(Member, Month, Policy): decimal
  CalculateStatus(Paid, Target, Policy): MemberMonthlyStatus
  ShouldCreateDebt(Shortfall, Policy): bool
  ShouldChargeFine(Shortfall, Savings, Policy): bool
  ShouldUseSavings(Shortfall, Savings, Policy): decimal (kiasi cha kukata)
}
```

**Example: ShouldChargeFine**
```csharp
bool ShouldChargeFine(decimal shortfall, decimal savingsBalance, GroupPolicy policy) {
  if (policy.ShortfallStrategy == FineImmediately) return true;
  if (policy.ShortfallStrategy == UseSavingsThenFine) return savingsBalance < shortfall;
  if (policy.ShortfallStrategy == DebtOnly) return false;
  return false;
}
```

### 5. Edge Cases

**Q: Kiongozi akibadilisha policy katikati ya mwezi?**
A: Policy mpya inaanza mwezi ujao — EffectiveFrom lazima iwe 1st ya mwezi. Hii ina-epukwa confusion ya mid-month.

**Q: Member alilipa kabla ya policy mpya, lakini allocation inafanyika baada ya policy mpya?**
A: Allocation inatumia PolicyVersion ya PaymentReceived date, si allocation date.

**Q: Policy V1 ina fine 5k, V2 fine 2k — fine ya zamani inabaki 5k au inakuwa 2k?**
A: Fine iliyo-charged tayari inabaki 5k (ledger entry). Policy mpya ina-apply kwa future fines tu.

### 6. Success Criteria ya Business Rule Engine
- Hakuna hardcoded 10k, 5k, 3 months kwenye code — zote kutoka GroupPolicy
- Policy versioning inafanya kazi — reports za zamani hazibadiliki
- Wizard inauliza maswali 10 na kutengeneza GroupPolicy V1 automatically
- Kila FinancialEvent ina PolicyVersion
