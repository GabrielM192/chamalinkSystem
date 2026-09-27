# MEMBER STATUS ENGINE SPEC
## Lifecycle ya Mwanachama — Si Status ya Mwezi Tu

### Tatizo la Sasa
Tulikuwa na:
```
Paid, PartiallyPaid, Missed, CoveredBySavings
```
Hii ni status ya **mwezi** — March alilipa au hakulipa.

Lakini mwanachama mwenyewe anahitaji status yake ya **mfumo** — Active, Suspended, Exited, etc. Hii haipo kama entity rasmi.

### 1. MemberStatus — Entity Rasmi

```
MemberStatus Enum:
- Active = 1 (default, anaweza kuchangia, kuomba loan, kupata welfare)
- Inactive = 2 (hajachangia kwa muda, lakini bado member — e.g. missed 1-2 months)
- Warning = 3 (ame-miss MaxConsecutiveMissedMonths - 1, karibu kuwa NonActive)
- NonActive = 4 (ame-miss MaxConsecutiveMissedMonths, hawezi kuomba loan, lakini bado ana akiba)
- Suspended = 5 (amesimamishwa na governance — e.g. utovu wa nidhamu)
- Exited = 6 (amejitoa mwenyewe au ameondolewa — akiba inarudishwa per policy)
- Deceased = 7 (amefariki — akiba inaenda kwa beneficiary per policy)
- Archived = 8 (data ya zamani, haionekani kwenye active reports)

Ukonga wanatumia "Non Active" leo, lakini SACCOS watasema "Deceased" au "Archived" kesho — lazima iwe configurable na extensible.
```

### 2. MemberStatusHistory — Audit Trail ya Status

```
MemberStatusHistory {
  Id: Guid
  MemberId: Guid
  FromStatus: MemberStatus
  ToStatus: MemberStatus
  Reason: string (e.g. "Missed 3 consecutive contributions", "Voluntary exit", "Deceased — death certificate #123")
  ChangedBy: Guid (UserId — nani alibadilisha)
  ChangedAt: DateTime
  EffectiveFrom: DateTime (when new status becomes effective)
  Metadata: JSON (e.g. exit settlement details)
}
```

**Example:**
```
Frank:
2026-01-15: New → Active (Joined, MembershipNumber UKG-0001 assigned)
2026-04-15: Active → Warning (Missed Feb, Mar — 2 months, Max is 3)
2026-05-15: Warning → NonActive (Missed 3 months, per Policy V1 MaxConsecutiveMissedMonths=3)
2026-06-01: NonActive → Active (Paid all debts + fine, Treasurer approved)
```

### 3. Status Transition Rules — Business Rules Engine

```
MemberStatusEngine {
  CanTransition(From, To, Reason, ChangedByRole): bool
  GetNextStatus(Member, AsOfDate, Policy): MemberStatus (auto-calculated)
  Transition(Member, ToStatus, Reason, ChangedBy): MemberStatusHistory
}

Transition Matrix (configurable per OrganizationType):

Active → Inactive: Auto when missed 1 month
Active → Warning: Auto when missed (Max-1) months
Warning → NonActive: Auto when missed Max months
Active → Suspended: Manual, by Chairperson/Treasurer with governance approval
Suspended → Active: Manual, after suspension period + governance approval
Active → Exited: Manual, voluntary or governance decision, requires settlement
Active → Deceased: Manual, with death certificate, triggers beneficiary payout
Any → Archived: Manual, after Exited/Deceased settlement complete, data retained for audit

NonActive → Active: Auto when all debts + fines cleared, or manual by Treasurer
```

**Example Rules per OrganizationType:**
```
Mkoba:
  MaxConsecutiveMissedMonths = 3
  Auto transition: Active → Warning (2 missed) → NonActive (3 missed)

Vicoba:
  MaxConsecutiveMissedMonths = 6 (more lenient)
  No auto NonActive, manual only

Saccos:
  No NonActive for missed contributions — only for loan default
```

### 4. Member Monthly Status vs Member Status — Tofauti

```
MemberStatus (lifecycle ya mwanachama — long term):
  Active, Inactive, Warning, NonActive, Suspended, Exited, Deceased, Archived

MemberMonthlyStatus (performance ya mwezi — short term):
  Paid, PartiallyPaid, Missed, CoveredBySavings, Exempted (e.g. welfare, suspended)

Mfano:
  Frank March:
    MemberStatus = Active (lifecycle)
    MonthlyStatus March = Paid (alilipa)

  John March:
    MemberStatus = Active
    MonthlyStatus March = CoveredBySavings (hakulipa, lakini akiba ilikatwa per ShortfallStrategy)

  Peter March:
    MemberStatus = NonActive (ame-miss 3 months)
    MonthlyStatus March = Missed (hajalipa na hana akiba)

  Mary March:
    MemberStatus = Suspended (amesimamishwa)
    MonthlyStatus March = Exempted (has to contribute kwa sababu suspended)
```

### 5. Status na Financial Position

```
MemberStatus ina-affect Financial Position na permissions:

Active: Can contribute, can request loan, can receive welfare, can vote
Inactive: Can contribute, cannot request loan, can receive welfare
Warning: Can contribute, cannot request loan, warning notification sent
NonActive: Cannot request loan, cannot vote, must clear debt to become Active
Suspended: Cannot contribute, cannot request loan, cannot vote, cannot receive welfare
Exited: No financial activity, settlement in progress, savings return per policy
Deceased: No activity, beneficiary payout per policy
Archived: No activity, read-only for audit

Example:
  Frank NonActive → Financial Position inaonyesha ContributionDebt 30k, na system inazuia Loan Request
  Frank akilipa debt + fine → Auto transition NonActive → Active → Loan Request inawezekana tena
```

### 6. Edge Cases

**Q: Member akifa na ana loan outstanding 500k na savings 200k?**
A: Status Deceased, Held Savings 150k inatumika kulipa loan per policy (CollateralCanBeUsedForDefault), remaining loan 350k ina-handle per SACCOS/Mkoba policy (e.g. written off, au beneficiary analipa). Audit trail inarekodi yote.

**Q: Member ame-Exit na ana ContributionDebt 20k?**
A: Settlement: Savings 100k - Debt 20k - JoiningFeeBalance 10k = 70k returned to member. Status Exited, Financial Position Net = 0 after settlement.

**Q: NonActive member anaweza kurudi Active automatically?**
A: Yes, per Policy AutoReactivation=true, akilipa debts zote. Au manual na Treasurer approval.

### 7. Success Criteria
- MemberStatus ni entity rasmi, si string kwenye report
- StatusHistory ina audit trail ya kila transition
- Transition rules ni configurable per OrganizationType
- MemberStatus na MemberMonthlyStatus ni tofauti na zinaeleweka
- Status ina-affect Financial Position na permissions (RBAC)
