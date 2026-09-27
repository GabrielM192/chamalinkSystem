> **IMEPITWA — USITUMIE.** Hati hii ni ya mfumo wa zamani (v2). Maporomoko ya malipo ya "Sheria 4" hapa chini si sahihi.
> Sheria halali ziko kwenye `BUSINESS_RULES_UKONGA.md` (v3.0), zikiwa na setting, method na test ya kila sheria.

# SHERIA ZA BIASHARA — UKONGA MKOBA WING
### (BUSINESS_RULES_UKONGA — Chanzo cha Ukweli / Source of Truth)

**Toleo:** 2.0 — 2026-09-19
**Vyanzo:** (1) Sheria zilizothibitishwa na viongozi wa Ukonga (2026-09-18),
(2) Jedwali la Excel la mtunza-hazina, (3) Taarifa ya M-Koba
(2026-01-01 hadi 2026-08-01), (4) Ukonga Rules Specification v1.2
(inayotajwa kwenye code).

> Faili hili ndilo chanzo cha ukweli cha sheria za kikundi. Mabadiliko
> yoyote ya sheria yanapaswa kuanza hapa, KISHA kuendelea kwenye code na
> majaribio. Kila sheria inaonyesha mahali ilipotekelezwa na jaribio
> linaloilinda.

---

## SHERIA 1: Mchango wa Kila Mwezi

- Kila mwanachama anachangia **TSH 10,000** kila mwezi.
- Mchango unahesabiwa kwa mwezi wa kalenda (JAN–DES).
- Mwezi ambao mwanachama hajachangia unaalamishwa **\*** kwenye ripoti.

**Imetekelezwa:** `ContributionSettings.MonthlyContribution` (inawekwa
kwenye settings za kikundi), `ContributionComplianceBackgroundService`
(inahesabu madeni kila mwezi), `TreasuryImportService.ComputeSplit`.
**Jaribio:** `UkongaImportTests` (michango ya msingi).

## SHERIA 2: Faini ya Kuchelewa

- Mwanachama anayechangia **baada ya tarehe ya mwisho** (pamoja na
  siku za neema) analipa faini ya **TSH 5,000**.
- Hivyo seli ya Excel ya **15,000 = 10,000 mchango + 5,000 faini**.

> ⚠️ **ANGALIA:** 5,000 hapa ni **FAINI ya kuchelewa**, SIYO KIANZIO.
> Usichanganye sheria hii na Sheria 4 (mgawanyo wa ziada). Jaribio la
> `LateFine_RemainsFine_NotRoutedToCascade` linalinda mpaka huu: ziada
> inayolingana haswa na faini inabaki faini, haiingii kwenye mgawanyo
> wa KIANZIO/deni/akiba.

- Ziada isiyolingana na faini (mf. 12,000 → ziada 2,000) inafuata
  Sheria 4.

**Imetekelezwa:** `ContributionSettings.LateFine`, `Fine` entity
(issued+paid), `TreasuryImportService.ComputeSplit`.
**Jaribio:** `LateFine_RemainsFine_NotRoutedToCascade`.

## SHERIA 3: KIANZIO (Joining Fee)

- Kila mwanachama mpya analipa **TSH 50,000** mara moja (KIANZIO).
- Inaweza kulipwa kwa **awamu** (vipande).
- Kilichobaki hakulipwa = **deni la KIANZIO** hadi kikamilike.
- Kwenye Excel ya mtunza-hazina kuna safu-wima maalum ya KIANZIO.

**Imetekelezwa:** `FinancialSettings.JoiningFee`, safu-wima ya KIANZIO
kwenye Excel import (`-KIANZIO` refNo), deni la KIANZIO kwenye ripoti ya
Mwezi kwa Mwezi (`JoiningFeeDebt = max(0, lengo − imelipwa)`).
**Jaribio:** `MonthlyMatrix_ShowsCells_Missed_Fines_JoiningFeeDebt`.

## SHERIA 4: Mgawanyo wa Ziada (Waterfall) ⭐

Pesa yoyote ya ziada zaidi ya mchango wa mwezi (isiyo faini) inagawanywa
katika mpangilio huu **bila kubadilishwa**:

```
Ziada (mf. 30,000 → ziada 20,000)
   ↓
1. REJESHO la mkopo linalostahili mwezi huu   (LoanRepayment, refNo …-REJESHO-…)
   ↓ (wajibu wa mwezi ukikamilika)
2. KIANZIO isiyokamilika      (entry ya JoiningFee, refNo …-KIANZIO-ZIADA)
   ↓ (KIANZIO ikikamilika)
3. Madeni ya michango ya nyuma (DebtService.ClearWithPaymentAsync, refNo …-DENI)
   ↓ (madeni yakiisha)
4. AKIBA                       (AdvanceBalance, refNo …-AKIBA)
```

**AWAMU 1 (2026-09-19):** hatua ya REJESHO iliongezwa KWANZA kwenye
ziada — rejesho ni WAJIBU wa mwezi (sehemu ya Lengo), hivyo linapewa
kipaumbele kuliko matumizi ya hiari (KIANZIO ziada/akiba). Zamani pesa
ya rejesho ilienda akiba na mikopo ilibaki "Active" milele — huu ulikuwa
chanzo kikuu cha "taarifa zisizo za kweli".

**Ulinzi dhidi ya marudio:** ikiwa mwezi huu una rejesho la MKONO
(lisiyo na alama ya `-REJESHO-`), import haiingati mikopo na inatoa
ONYO — pesa moja isihesabiwe mara mbili.

Mfano halisi (Ukonga):
- 30,000 = 10,000 mchango + 15,000 rejesho + 5,000 akiba (Frank: rejesho 15,000/mwezi)
- 12,000 = 10,000 mchango + 2,000 → rejesho (ikiwa linastahili) au KIANZIO
- 14,000 = 10,000 + [1,000 KIANZIO + 3,000 deni] (hakuna mkopo, KIANZIO ilibaki 1,000)
- 13,000 = 10,000 + 3,000 akiba (hakuna mkopo, KIANZIO imekamilika, hakuna deni)

**Historia:** Kabla ya 2026-09-18 Excel import ilikuwa inapeleka ziada
YOTE akiba moja kwa moja — kinyume na sheria hii. M-Koba import ilikuwa
sahihi tayari (STEP 3.4→3.5→4). Sasa njia zote mbili zinafanana.

**Mambo muhimu ya kiufundi:**
- Pending ya KIANZIO inafuatwa "running" kwa mwanachama mmoja katika
  import moja (SumAsync haiwezi kuona entries zisizohifadhiwa bado —
  bila hii KIANZIO ingepita lengo).
- Kiasi cha KIANZIO cha safu-wima kinatolewa kwenye pending ili ziada
  isikijaze mara mbili.
- Import ni salama kurudia (guards za refNo).

**Imetekelezwa:** `TreasuryImportService` hatua (3-rejesho)/3a/3b/3c;
`MkobaImportController` STEP 3.3/3.4/3.5/4.
**Majaribio:** `Excess_GoesToJoiningFee_First_NotSavings`,
`Excess_Cascades_JoiningFee_ThenDebt_ThenSavings`,
`Excess_GoesToSavings_Only_When_JoiningFeeDone_NoDebt`,
`Rerun_SkipsDuplicates_NoDoubleEntries`,
`Preview_PlannedSplit_Shows_JoiningFeeAllocation`.

## SHERIA 5: Madeni ya Michango

- Mwezi usiolipwa unakuwa deni (Outstanding) kiotomatiki baada ya
  siku za neema kupita.
- Mgawanyo wa malipo ya madeni: **CurrentMonthFirst** — malipo mapya
  yanahesabiwa kama mchango wa mwezi wa sasa kwanza; madeni ya nyuma
  yanafutwa tu kwa ziada mahususi (kupitia waterfall ya Sheria 4).
- Mikakati mingine (`OldestDebtFirst`, `ManualAllocation`) **inakataliwa
  waziwazi** (400) badala ya kunyamaza — kulinda usahihi wa hesabu.

**Imetekelezwa:** `DebtService.RecordShortfallAsync` /
`ClearWithPaymentAsync` (inasoma `DebtAllocationStrategy` na kukataa
isiyotekelezwa), `ContributionComplianceBackgroundService`.
**Majaribio:** `SilentFallthroughTests` (6).

## SHERIA 6: Hali za Mwanachama na NON ACTIVE

- **Active** → anachangia kawaida.
- **Warning** → amekosa mwezi lakini bado hajafikia kikomo.
- **NON ACTIVE (Inactive)** → miezi **3 mfululizo** bila kuchangia.
- Mwanachama wa NON ACTIVE **anabaki kwenye rekodi** (haifutwi) —
  historia yake yote ya michango inahifadhiwa, na alama ya NON ACTIVE
  inaonyeshwa kwenye ripoti.

**Imetekelezwa:** `MemberStatus` (Active/Warning/Inactive),
`MaxConsecutiveMissedMonths = 3` (migration
`AddComplianceSettingsToContribution`), `ComplianceSnapshot`,
beji ya NON ACTIVE kwenye ripoti ya Mwezi kwa Mwezi.

## SHERIA 7: Ripoti ya Mwezi kwa Mwezi

Viongozi wa Ukonga wanahitaji jedwali linalolingana na Excel yao:

| S/N | JINA | JAN | FEB | … | KIANZIO | MATUKIO | JUMLA |
|-----|------|-----|-----|---|---------|---------|-------|
| 1 | Amina | 10,000 | \* | … | 30,000 (−20,000) | 5,000 | 145,000 |

- Kila seli = kilicholipwa mwezi huo (mchango + faini).
- `*` nyekundu = hajachangia; `F` = kulipwa na faini; `K`/`D` = ziada
  ilienda KIANZIO/deni; tooltip = mgawanyo kamili.
- JUMLA za kila mwezi, KIANZIO zote, MATUKIO (MSIBA/SHEREHE), na
  **AKIBA ILIYOPO** = michango + kianzio + faini − mikopo − ustawi −
  share-out.

**Imetekelezwa:** `ComplianceReportService.GetMonthlyMatrixAsync`,
`GET /api/Reports/monthly-matrix/{groupId}?year=`, tab "Mwezi kwa
Mwezi" kwenye Ripoti.
**Jaribio:** `MonthlyMatrix_ShowsCells_Missed_Fines_JoiningFeeDebt`.

## SHERIA 8: Matukio ya Ustawi (MSIBA / SHEREHE)

- Vikundi vya Ukonga hukusanya michango maalum ya matukio (msiba,
  sherehe) — kwenye Excel ni safu-wima za MSIBA na SHEREHE.
- Hii ni **payout/ustawi**, sio mchango wa kawaida.
- Kwenye ripoti ya Mwezi kwa Mwezi: `EventContribution` +
  `WelfareTopUp` → safu ya MATUKIO.
- Excel import ya v1: MSIBA/SHEREHE zinaonyeshwa kwenye **onyo**
  (warnings), hazijaingizwa kiotomatiki — zinahitaji model ya tukio +
  katiba (kazi ya v2).

---

# AINA ZA MAKUNDI (Group Type Strategy)

ChamaLink imejengwa kwa **Ukonga kama mfano wa kwanza**, lakini
architecture inatengeneza nafasi ya aina nyingine:

| Aina | Hali | Mfano |
|---|---|---|
| **MonthlySavings** | ✅ Inafanya kazi kikamilifu | Ukonga Mkoba Wing: mchango wa kila mwezi 10,000, KIANZIO 50,000, faini 5,000, mikopo kutoka akiba |
| **Hybrid** | 🟡 `GroupType.Hybrid` ipo kwenye enum; modules za Event + Contribution zinaweza kuwasha pamoja | Kikundi chenye mchango wa kila mwezi + matukio yenye michango maalum |
| **EventBased** | 🔴 Bado — itahitaji specification yake ya biashara (hakuna kikundi halisi kinachoitumia bado) | Vikundi vya sherehe/tukio pekee |

**Kanuni ya ujenzi:** Hakuna architecture ya kispekulativa kwa aina
zisizokuwepo. Aina mpya itajengwa **ikiwa na kikundi halisi**
kinachoitumia na specification iliyothibitishwa na viongozi wake —
kama ilivyofanywa kwa Ukonga (faili hili).

---

## SHERIA 9: Ulinganisho (Reconciliation) — 2026-09-19

Kipimo cha mwisho cha uaminifu wa hesabu si "features zipo" bali:

```
M-Koba PDF  =  Excel ya viongozi  =  Ledger ya ChamaLink
```

kwa kila mwanachama × kila mwezi. Chombo cha **Ulinganisho**
(tab kwenye Ripoti) hufanya hivi:

- Hupakia taarifa ya M-Koba (PDF) na/au Excel ya mtunza-hazina.
- Huparse kwa parsers ZILE ZILE za import (chanzo kimoja cha ukweli).
- Hulinganisha na LedgerEntries (Contribution + FinePayment +
  JoiningFee + EventContribution) kwa kila mwezi.
- Hutoa jedwali: MWANACHAMA | MWEZI | PDF | EXCEL | MFUMO | TOFAUTI |
  HALI | SABABU, pamoja na hali:
  - **SAWA** — vyanzo vyote vinapatana
  - **NYARAKA HAZILINGANI** — PDF vs Excel zinatofautiana (tofauti =
    faini inatambuliwa kiotomatiki)
  - **MFUMO CHINI** — mchango bado haujaingizwa mfumo
  - **MFUMO ZIADA** — mfumo una zaidi (mchango wa mkono, ziada halisi,
    au **import mara mbili** — inatambuliwa na kuonywa wazi)
- Mstari maalum wa **KIANZIO** kwa kila mwanachama (Excel vs ledger;
  ziada ya waterfall inaelezwa).
- Maonyo: miamala ya M-Koba isiyolingana na mwanachama, withdrawals,
  MSIBA/SHEREHE.
- **READ-ONLY**: hakibadilishi data yoyote; hufanya kazi kwa data ya
  zamani bila migration.

**Imetekelezwa:** `ReconciliationService`,
`POST /api/Reports/reconciliation/{groupId}?year=`, tab "Ulinganisho".
**Majaribio:** `ReconciliationTests` (7).

## SHERIA 10: Marejesho ya Mikopo + Statement ya Mwezi — 2026-09-19 (Awamu 1)

**Rejesho linalostahili (LoanSchedule rahisi):**
- Mkopo wa Flat: installment = (Principal + Interest) / miezi yote
  (DisbursedAt..DueDate, inclusive). Mwezi wa mwisho = salio lote.
- Import inakata installment ya MWEZI huo tu kwa kila mkopo (FIFO —
  mkopo mzee kwanza). Malipo ya mapema (early payoff) yanabaki mkono.
- Kila entry ya import ina alama `-REJESHO-` kwenye ReferenceNo —
  hii ndiyo inayotofautisha na rejesho la mkono (ulinzi wa marudio).

**Lengo la mwezi** = mchango unaotarajiwa + rejesho linalostahili.
Hii ndiyo msingi wa Statement ya Mwezi (siyo snapshots — hesabu ni
LIVE kutoka ledger, ili mtunza-hazina apate usahihi mara tu baada ya
import).

**Statement ya Mwezi** (`GET /api/Reports/member-statement/{groupId}?year=&month=`):
- Safu: LENGO | AMETOA | UPUNGUFU | FAINI | REJESHO | AKIBA | DENI BAKI | HALI.
- HALI (ya mwezi huu TU, siyo maisha yote): Lengo=0 → "—";
  paid ≥ Lengo → **Amelipa**; 0 < paid < Lengo → **Amelipa Sehemu**;
  0 → **Hajalipa**.
- Tofauti na Sheria 7 (Mwezi kwa Mwezi = Ledger View: pesa ngapi
  zimeingia), hii ni **Compliance View**: alitakiwa nini vs alifanya nini.
  Ripoti zote mbili zinahitajika — hazibadilishani.
- "Amelipa kupitia akiba" (UseSavingsThenFine) itakuja Awamu 4.

**ComplianceSnapshots wiring fix:** `UpsertSnapshotAsync` zamani
haikuitwa KAMWE — jedwali lilikuwa tupu milele na ripoti za
Uzingatiaji zilisoma jedwali tupu. Sasa background job inaiita kwa
kila mwanachama, pamoja na `ExpectedLoanRepayment`/`PaidLoanRepayment`.

**Imetekelezwa:** `LoanService` (schedule + import repayment),
`MemberStatementService`, STEP 3.3 (M-Koba), hatua (3-rejesho)
(Treasury), tab "Statement ya Mwezi".
**Majaribio:** `Awamu1LoanRepaymentTests` (10).

---

## SHERIA 11: Ukarabati wa Data ya Bug-Era — 2026-09-19 (Awamu 2)

**Tatizo:** data iliyoingizwa kabla ya 2026-09-19 ina drift tatu:
1. Counter drift — mchango wa mkono haukusasisha `TotalContributionsCount`/`AdvanceBalance`.
2. Snapshots tupu — `ComplianceSnapshots` lilikuwa tupu milele (root cause #5).
3. Loan gaps — pesa ya rejesho ilienda akiba (hakukuwa na hatua ya rejesho).

**Suluhisho (sio kuharibu ledger):**
- **Preview (READ-ONLY):** `GET /api/DataRepair/preview/{groupId}?months=` — inaonyesha
  wanachama wenye drift, miezi yenye snapshots pungufu, na miezi ambapo rejesho
  lilikosekana lakini akiba iliingizwa (bug-era pattern).
- **Repair counters:** `POST /api/DataRepair/repair-counters/{groupId}` — hesabu upya
  kutoka ledger (chanzo cha ukweli). Idempotent.
- **Repair snapshots:** `POST /api/DataRepair/repair-snapshots/{groupId}?months=` — jenga
  upya snapshots kwa miezi ya nyuma (kwa kutumia `LoanService.GetExpected...` + ledger).
  Idempotent.
- **Full repair:** counters + snapshots kwa pamoja.
- **Loan gaps ni ripoti TU** — hatuhamishi pesa moja kwa moja. Uhamishaji ni uamuzi wa
  uongozi kupitia LoanController (RecordRepayment).

**Frontend:** ukurasa `/matengenezo` (nav "Matengenezo" kwa leaders + banner kwenye
Mipangilio). Inaonyesha warnings, summary cards, na majedwali matatu (drift, snapshots,
mapengo ya rejesho). Vifungo vyote vina confirm dialog.

**Imetekelezwa:** `DataRepairService`, `DataRepairController`, `DataRepairPage`,
`dataRepair.ts`, route `/matengenezo`.
**Majaribio:** `Awamu2DataRepairTests` (4).

---


## SHERIA 12: KIANZIO Option A + Mkoba Date Bug Fix + Clean Code — 2026-09-19 (Awamu 2.5)

**KIANZIO (JoiningFee) Option A — uamuzi wa kitaalamu:**
- KIANZIO ni membership obligation TU, si monthly compliance.
- Haiingii kwenye Compliance Engine (ConsecutiveMissedMonths/Status).
- Inaonekana kwenye Wasifu (progress) na Statement ya Mwezi, lakini
  ziada ya mchango wa mwezi HAIENDI KIANZIO — inaenda: REJESHO → deni → akiba.
- KIANZIO inalipwa TU kupitia safu-wima ya JoiningFee kwenye Excel au malipo ya mkono.

**Mkoba date bug fix:**
- `ParseTransactionDate` zamani ilirudisha `UtcNow` tarehe ikishindikana kusomeka —
  pesa iliingia mwezi wa kupakia, si mwezi halisi → "taarifa zisizo za kweli".
- Sasa inarudisha null na row inarukwa (safe) badala ya kutumia leo.

**Monthly Matrix + Loan Repayment:**
- Matrix sasa ina safu ya `LoanRepayment` (R badge) — awali haikuwepo.
- `TotalLoanRepayments` inaonyeshwa kwenye cards.

**Product Configuration Layer:**
- `OrganizationType` enum mpya: Mkoba, Vicoba, Saccos, SavingsClub, Other
- `Group` ina `OrganizationType` + `GroupType` (ContributionModel)
- `ContributionSettings`: + `ShortfallStrategy`, `ComplianceStrategy`
- `LoanSettings`: + `LoanStrategy`, `GuarantorRequired`, `MinGuarantors`
- Comments zote zimesafishwa kuwa Kiingereza rahisi, logic flow safi.

**Frontend:**
- `MemberProfilePage`: sasa ina Monthly Obligations card na month/year picker
  (Target/Paid/Loan Repayment/Fine/Savings/Debt/Status) — si lifetime tu.
- `MkobaPage`: tab "Malipo" → "Utoaji (Withdrawals)" — kuepuka utata na marejesho.
- `ReportsPage`: Matrix ina R badge na TotalLoanRepayments card.

**Imetekelezwa:** MkobaParser fix, Treasury/Mkoba waterfall Option A, Matrix loan column,
MemberProfile monthly view, Enums clean, GroupSettingsModules clean.
**Majaribio:** 53/53 green (UkongaImportTests updated for Option A).

---

# KAZI ILIYOBAKI (kwa uwazi)

1. **MSIBA/SHEREHE v2** — uingizaji kamili wa matukio (model ya tukio +
   katiba ya ustawi).
2. **Mikakati ya madeni** — `OldestDebtFirst`/`ManualAllocation`
   zitakapohitajika na kikundi halisi (sasa zinakataliwa waziwazi).
3. **Usalama** — kuzungusha JWT key + neno la siri la PostgreSQL
   (`BADILISHA-NENO-LA-SIRI.md`); JWT key ya zamani bado iko wazi
   kwenye historia ya GitHub.
4. **Monthly statement ya mwanachama mmoja** (P2) — mtazamo wa
   mwanachama mmoja pekee (Jan→Des na mgawanyo kamili). Data ipo
   kwenye ripoti ya Mwezi kwa Mwezi; UI ya mtu mmoja inaweza
   kuongezwa baadaye.
