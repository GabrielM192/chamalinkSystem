# CHAMALINK V2 — AWAMU 0 OVERVIEW
## Financial Operating System Foundation

**Status:** AWAMU 0 — Domain & Rules Design (BILA CODE)
**Date:** 2026-09-20
**Architect:** CTO Mode — 25+ years experience

### Lengo la AWAMU 0
Kabla ya line moja ya code kuandikwa, tunafreeze business rules na domain model. Hii ndiyo tofauti ya "kurekebisha mfumo" na "kuweka foundation ya miaka 10".

### Flow ya Kweli ya ChamaLink (Baada ya marekebisho yako)
```
Organization (Mkoba/Vicoba/Saccos/SavingsClub)
  ↓
Contribution Model (Monthly / EventBased / Hybrid) — Identity ya kikundi
  ↓
Business Rules Wizard → GroupPolicy + Policy Versioning (EffectiveDate)
  ↓
Member Enrollment → Membership Number (UKG-0001, immutable)
  ↓
Financial Events (PaymentReceived, LoanIssued, FineCharged, etc.)
  ↓
Business Rule Engine (Executor wa GroupPolicy)
  ↓
Allocation Engine (Executor wa Rules tu — si moyo)
  ↓
Ledger (Single Source of Truth — CR/DR entries)
  ↓
Financial Position Engine (Real-time: Savings, Held, Available, Debts, Net)
  ↓
Reports (Read Only — kutoka Ledger + Financial Position)
  ↓
Audit Trail (Who/What/When/Before/After/Source/Batch)
```

### Docs 7 za AWAMU 0
1. `01_DOMAIN_MODEL_V2.md` — Organization, ContributionModel, GroupPolicy, Member
2. `02_BUSINESS_RULE_ENGINE_SPEC.md` — Wizard, Rules, Policy Versioning
3. `03_FINANCIAL_POSITION_SPEC.md` — Savings/Held/Available, Debt, Loan, Net
4. `04_MEMBER_STATUS_ENGINE_SPEC.md` — Lifecycle: Active/Inactive/Suspended/Exited/Deceased/Archived
5. `05_WELFARE_DOMAIN_SPEC.md` — WelfareEvent, Obligation, Contribution, Disbursement
6. `06_AUDIT_TRAIL_SPEC.md` — AuditEvent, traceability ya kila shilingi
7. `07_ALLOCATION_ENGINE_SPEC.md` — Input/Output, executor wa rules

### Kanuni Kuu 5 za V2
1. **Ledger First:** Hakuna balance update moja kwa moja — kila kitu ni Event → Ledger
2. **Business Rules First:** Allocation ni matokeo ya Rules, si chanzo
3. **Policy Versioning:** Contribution 2026=10k, 2027=20k — reports za 2026 zisibadilike
4. **Available vs Held Savings:** Savings Balance 300k, Held 150k (collateral), Available 150k
5. **Auditability:** Kila shilingi inaelezeka: Who, When, Source, Batch, Allocation, Ledger Entries

### Success Criteria ya AWAMU 0
- Docs zote 7 zikikubaliwa na founder
- Hakuna code mpya bado
- Kila doc ina formulas, examples, na edge cases
- Domain model inaweza kudumu miaka 10 bila breaking change
