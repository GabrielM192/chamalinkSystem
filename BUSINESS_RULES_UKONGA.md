# Kanuni za Fedha — Ukonga mkoba wing (VIJANA UMOJA MKOBA)

**Toleo 3.0 · 2026-09-26.** Hati hii inachukua nafasi ya v2. Nakala ya zamani iko hapa: `docs/archive/BUSINESS_RULES_UKONGA_v2_legacy.md` (IMEPITWA).

## Jinsi ya kusoma hati hii

Kila sheria ina sehemu nne:

| Sehemu | Maana |
|---|---|
| **Sheria** | Ambacho viongozi wamekubaliana, kwa lugha ya kawaida |
| **Setting** | Mahali thamani ya sheria inapohifadhiwa: `TruthGroupSettings.<Safu>`. Viongozi wanaibadilisha kwenye *Kanuni za kikundi* |
| **Code** | Method inayotekeleza sheria |
| **Test** | Test inayothibitisha sheria, iliyoko `ChamaLink.Tests` |

**Kanuni ya msingi: hakuna kiasi cha fedha ndani ya code.**

- Kila kiasi, tarehe au ndiyo/hapana ya sheria inasomwa kutoka kwenye setting za kikundi.
- Kikundi ambacho setting zake hazijawekwa kinakataliwa waziwazi (`PolicyNotConfiguredException`, HTTP 409). Hakuna thamani ya kubuni inayowekwa kimya kimya.
- Ulinzi huu unasimamiwa na `NoHardcodedRulesTests`, ambayo inaanguka kiasi cha fedha (na au bila `m`) au tarehe ya kalenda ikiandikwa kwenye services, controllers, engines au frontend.

Thamani za Ukonga zilizoandikwa hapa chini ni **data ya kikundi hiki**, si code. Kikundi kingine kinaweka thamani zake.

---

## Setting za Ukonga

| Setting | Thamani ya Ukonga | Inahitajika wakati wa kuweka mara ya kwanza |
|---|---|---|
| `MonthlyContribution` | 10,000 | ndiyo (> 0) |
| `LateFine` | 5,000 | ndiyo (0 inaruhusiwa) |
| `JoiningFee` | 50,000 | ndiyo (0 inaruhusiwa) |
| `DueDateDay` | 5 | ndiyo (1–28) |
| `GracePeriodDays` | 0 | ndiyo (0–25) |
| `FineOnPartialPayment` | Hapana | ndiyo |
| `AutoDeductSavings` | Ndiyo | ndiyo |
| `ContributionStartDate` | 2026-01-01 | ndiyo |
| `FoundingMembersCutoff` | 2026-03-31 | hapana (hakuna = wote ni wapya) |
| `ManualEntryCutoff` | tarehe ya kuanza M-Koba kama chanzo pekee | hapana (hakuna = malipo ya mkononi yanakataliwa baada ya import) |
| `LoanLatePenaltyAmount` | kulingana na uamuzi wa kikundi | hapana (0 = hakuna adhabu) |

Setting zinahifadhiwa kupitia `TruthSetupService.UpdateSettingsAsync`. Setting ya kwanza ikikosa sehemu yoyote, jibu linaorodhesha kila kilichokosekana.
Test: `FirstSettings_MustSpecifyEveryMoneyRule_NoSilentDefaults`.

---

## S1 — Tarehe ya mwisho ya mwezi

- **Sheria:** Mchango wa mwezi M unatakiwa kufika kabla ya mwisho wa siku `DueDateDay` ya mwezi M+1, yaani saa 23:59:59 kwa saa za Afrika Mashariki. Siku za neema zikiwepo, zinaongezwa juu. Kwa Ukonga, mchango wa Julai unatakiwa kufika kabla ya 5 Agosti saa 23:59:59. Malipo ya tarehe 6 yamechelewa.
- **Setting:** `DueDateDay`, `GracePeriodDays`. Siku za neema za mwezi mmoja tu zinawekwa kama `TruthAdjustment` ya aina `GraceExtension`, ambayo lazima iidhinishwe.
- **Code:** `TruthPolicy.BaseDeadline`, na `ContributionTruthEngine.Replay` kwa dirisha la mwezi: (deadline ya M-1, deadline ya M].
- **Test:** `PaymentInGraceDays_BelongsToPreviousMonth`, `NoGrace_PaymentOn6th_IsLate_AndFined`, `GroupGracePeriod_ExtendsDeadline`, `ApprovedGraceExtension_ForOneMonth`.

## S2 — Deni na faini

- **Sheria:** Tarehe ya mwisho ikipita bila mchango, mwezi unakuwa deni la `MonthlyContribution`, pamoja na faini ya `LateFine`.
  - Kama `FineOnPartialPayment` = Hapana (Ukonga), faini inatozwa tu kwa mwezi ambao mwanachama hakulipa chochote kwa wakati.
  - Aliyelipa sehemu kwa wakati anabaki na deni la kiasi kilichobaki, bila faini.
- **Setting:** `MonthlyContribution`, `LateFine`, `FineOnPartialPayment`.
- **Code:** `ContributionTruthEngine.Replay` (sehemu ya kufunga mwezi).
- **Test:** `PartialPaymentOnTime_NoFine_RemainderIsArrears`, `NoGrace_PaymentOn6th_IsLate_AndFined`.

## S3 — Msamaha wa faini

- **Sheria:** Faini inafutwa tu kwa msamaha uliorekodiwa na kiongozi mmoja na kuidhinishwa na kiongozi mwingine (`TruthAdjustment` ya aina `FineWaiver`). Msamaha usioidhinishwa hauna athari.
- **Setting:** hakuna; ni uamuzi wa kila tukio.
- **Code:** `ContributionTruthEngine.Replay`, `TruthSetupService` (maker-checker).
- **Test:** `ApprovedFineWaiver_RemovesFine_UnapprovedDoesNot`.

## S4 — Mpangilio wa kulipa (waterfall)

- **Sheria:** Kila shilingi inayoingia inagawanywa kwa mpangilio huu:
  1. rejesho la mkopo linalodaiwa (na adhabu yake);
  2. deni la mwezi wa zamani zaidi: kwanza mchango wake, kisha faini yake;
  3. miezi inayofuata kwa mpangilio huo huo, hadi mwezi uliopo;
  4. Kianzio (join fee) kilichobaki;
  5. ziada inayobaki inakuwa akiba.
- **Setting:** thamani zinatoka S1, S2 na S5.
- **Code:** `ContributionTruthEngine.Replay`.
- **Test:** `Frank_MatchesLeadersTruth`, `Tunganege_MatchesLeadersTruth`, `JoinFee_NeverSwallowsContributionDebt`, `Loan_RepaymentIsTakenFirst_ThenNormalWaterfall`, `Replay_IsDeterministic_RegardlessOfInputOrder`.

> Hati ya v2 ilieleza mpangilio tofauti ("Sheria 4", ambapo Kianzio kilitangulia deni). Mpangilio huo si sahihi na ndio uliosababisha ripoti za uongo.

## S5 — Kianzio (join fee)

- **Sheria:**
  - **Waanzilishi** (waliojiunga kabla au siku ya `FoundingMembersCutoff`) wanadaiwa `JoiningFee`. Wanaweza kulipa taratibu. Kilichobaki kinabaki deni hadi kilipwe na hakigusi michango ya mwezi.
  - **Wanachama wapya** wanalipa Kianzio kwanza.
- **Setting:** `JoiningFee`, `FoundingMembersCutoff`. Kiasi cha mwanachama mmoja kinaweza kubadilishwa kwa `JoiningFeeOverride`.
- **Code:** `ContributionTruthEngine.Replay`, `TruthPolicy.JoinFeeRequiredUpfront`.
- **Test:** `JoinFee_NeverSwallowsContributionDebt`, `NewMember_PaysJoinFeeFirst`.

## S6 — Akiba (ziada)

- **Sheria:** Ziada inayobaki baada ya S4 inakuwa akiba.
  - Kama `AutoDeductSavings` = Ndiyo (Ukonga), na tarehe ya mwisho ikapita bila mchango, akiba inalipa mwezi huo kwanza, kwa hiyo hakuna faini.
  - Kama `AutoDeductSavings` = Hapana, akiba haiguswi.
- **Setting:** `AutoDeductSavings`.
- **Code:** `ContributionTruthEngine.Replay`.
- **Test:** `Excess_GoesToSavings_AndIsDeductedAtDeadlineIfMemberDoesNotPay`, `Savings_NotTouched_WhenMemberPaysOnTime`, `GroupThatDisablesAutoDeduct_KeepsSavings_AndMissedMonthBecomesDebt`.

## S7 — Msiba / sherehe (welfare)

- **Sheria:** Kabla mwanachama hajalipwa msaada wa msiba au sherehe, deni lake la Kianzio linakatwa kwanza. Kukata pia deni la michango ni hiari ya viongozi kwa kila tukio.
- **Setting:** kiasi kinaidhinishwa kwa kila tukio. Kigezo cha kukata deni la michango ni `alsoDeductArrears`.
- **Code:** `WelfareDeductionRule.Compute`.
- **Test:** `Welfare_DeductsJoinFeeDebtFirst`.

## S8 — M-Koba ndio chanzo cha ukweli

- **Sheria:**
  - Malipo yanahesabiwa kutoka PDF ya M-Koba.
  - Mwanachama anatambuliwa kwa **namba ya simu**, si jina.
  - Muamala mmoja (reference moja) unahesabiwa mara moja tu, hata PDF ikipakiwa mara nyingi.
- **Setting:** namba za simu za wanachama (`MemberPhones`), zinazowekwa na viongozi.
- **Code:** `MkobaParserService`, `TruthSetupService.ImportMkobaStatementAsync`, `MkobaPlatform.NormalizePhone`.
- **Test:** `DuplicateMkobaReference_CountedOnce`, `PhoneNumbers_Normalize`.

## S9 — Malipo ya mkononi (historia ya kabla ya M-Koba)

- **Sheria:** Malipo yasiyo kwenye M-Koba (mfano pesa taslimu kwa mtunza-hazina kabla ya M-Koba) yanaingizwa kwa masharti haya yote:
  1. yawe ya tarehe kabla au siku ya `ManualEntryCutoff`, vinginevyo yanakataliwa;
  2. yawe na maelezo **na ushahidi**, yaani aina (risiti, daftari la fedha, kumbukumbu za kikao, tamko la kiongozi, stakabadhi ya benki au simu, au nyingine) pamoja na mahali ushahidi ulipo;
  3. yaidhinishwe na **kiongozi mwingine**, si aliyeingiza. Kabla ya idhini hayana athari yoyote.
- `OccurredAt` ni tarehe malipo yalipofanyika. `CreatedAt` ni tarehe yalipoingizwa. Mgawanyo unatumia `OccurredAt`.
- **Setting:** `ManualEntryCutoff`.
- **Code:**
  - `TruthSetupService.RequestManualPaymentAsync`: tarehe, maelezo na ushahidi.
  - `TruthSetupService.DecideManualPaymentAsync`: maker-checker.
- **Test:** `ManualAfterCutoff_IsRejected_NotSilentlyCounted`, `ManualPayment_WithoutTraceableEvidence_IsNotSaved`, `ManualPayment_StoresEvidence_AndApproverIsRecorded`.

## S10 — Mistari inayokosekana kwenye PDF

- **Sheria:**
  - Salio la mstari mmoja likitofautiana na la mstari unaofuata, mfumo unaonyesha pengo.
  - Pengo linahesabiwa kwa mwanachama tu baada ya kiongozi wa pili kuidhinisha.
  - Kwa Ukonga, mapengo mawili ya 10,000 yamepatikana, kabla ya DB45MPN1AAJ na kabla ya DB75MR687FZ.
- **Setting:** hakuna.
- **Code:** `StatementIntegrity.FindGaps`.
- **Test:** `StatementIntegrity_FindsTheTwoMissingRowsInUkongaPdf`, `Gap_AssignedToMember_CountsOnlyAfterSecondLeaderApproves`.

## S11 — Mikopo

- **Sheria:**
  - Rejesho linadaiwa kwa awamu sawa hadi tarehe ya mwisho ya mkopo.
  - Kila malipo yanalipa rejesho linalodaiwa kwanza (S4).
  - Rejesho likichelewa, adhabu ni `LoanLatePenaltyAmount`.
- **Setting:** `LoanLatePenaltyAmount`, pamoja na masharti ya kila mkopo.
- **Code:** `TruthLoan.EqualInstallments`, `ContributionTruthEngine.Replay`.
- **Test:** `Loan_RepaymentIsTakenFirst_ThenNormalWaterfall`.

---

## S12 — Kumbukumbu za ukaguzi (audit trail)

- **Sheria:** Kila kitendo cha binadamu kinachobadilisha ukweli wa fedha kinaacha kumbukumbu isiyofutika:
  - nani (UserId, nafasi yake, namba ya uanachama);
  - nini (hali ya kabla na baada);
  - lini;
  - kwa nini (sababu au ushahidi).
- Kumbukumbu huhifadhiwa pamoja na kitendo chenyewe, kwa mara moja. Haiwezekani kitendo kiwepo bila kumbukumbu yake.
- Kumbukumbu hazifutwi hata na "futa data za majaribio".
- **Setting:** hakuna.
- **Code:** `TruthSetupService.AddAuditAsync`, inayoitwa na kila method ya kuandika.
- **Test:**
  - `ManualPayment_LeavesCompleteAuditTrail_WhoWhatWhenEvidenceAndApprover`
  - `RejectedManualPayment_IsStillInAuditTrail`
  - `SettingsChange_IsAudited_WithBeforeAndAfter`
  - `AuditTrail_SurvivesTestDataReset`
  - `RejectedGap_ClearsGap_ButAuditKeepsTheEvidence`
  - `TruthAudit_IsWrittenInOnePlace_InsideTheSameSaveChanges`

---

## S13 — Usajili na uanachama

- **Sheria:**
  - Mwanachama (rekodi ya fedha) na akaunti ya kuingia ni vitu viwili.
  - Mtu anajisajili, kisha anaomba kujiunga kwa kodi ya kikundi. Kiongozi anaidhinisha.
  - Namba ya simu ya muombaji ikilingana na rekodi iliyopo, mfumo **unapendekeza** tu. Kiongozi ndiye anathibitisha kuunganisha. Hakuna kuunganisha kiotomatiki kwa jina wala kwa simu.
  - Maombi ya Katibu au Mtunza-hazina yanaidhinishwa na Mwenyekiti pekee. Manaibu wanaruhusiwa.
  - Namba rasmi `{KIFUPI}-{MWAKA}-{NNN}` haibadiliki baada ya kutolewa.
  - Mwanachama wa kawaida anaona taarifa yake tu. Taarifa za kikundi ni za viongozi.
- **Setting:** `Groups.ShortCode` (Mwenyekiti anaiweka, na inafungwa baada ya namba ya kwanza).
- **Code:** `MembershipService`, `MembershipController`, `TruthController` (ruhusa za viongozi).
- **Test:** `MembershipServiceTests` (10), `TruthArchitectureTests`.

---

## S14 — Taarifa ya mwezi

- **Sheria:**
  - Taarifa ya mwezi M inatoka kwenye injini ileile ya taarifa ya mwanachama.
  - Hali ya kila mwanachama inapimwa dhidi ya tarehe ya mwisho ya M (S1).
  - Kama muamala wa mwisho wa M-Koba uliongizwa ni wa kabla ya tarehe hiyo, taarifa inaonyesha onyo kwamba haijakamilika.
- **Code:** `TruthLedgerService.GetMonthlyReportAsync`, `MonthlyReportText.Format`.
- **Test:** `MonthlyReport_July_MatchesLeadersTruth_ForFrankAndTunganege_WithWhatsAppText`, `MonthlyReport_BeforeTheGroupStarted_IsRejected_NotSilentlyEmpty`.

---

## Thibitisho kwa wanachama halisi (Agosti 2026)

| Mwanachama | Deni la michango na faini | Kianzio kilichobaki | Test |
|---|---|---|---|
| Frank (255712805984) | Julai + Agosti = 30,000 | 40,000 (baada ya 10,000 taslimu kuidhinishwa, pamoja na ushahidi) | `Frank_MatchesLeadersTruth` |
| Tunganege (255742674429) | 0 | 0 | `Tunganege_MatchesLeadersTruth` |

## Huduma za zamani

Hizi ni `FinancialPositionService`, `PositionProofService`, `LedgerReclassificationService`, `AllocationEngine` na `BusinessRuleEngine`.

- Endpoints zake zinarudisha **410 Gone** (`[LegacyEndpoint]`), isipokuwa `Legacy:Endpoints=true` iwekwe.
- Hazitumiwi na ripoti za Injini ya Ukweli.
- Zimebaki kwa uchunguzi tu, na zitaondolewa kwenye toleo lijalo.
- Thamani zake za kubuni za fedha (Kianzio 50,000, faini ya msiba 5,000, `CreateDefaultPolicy`) zimeondolewa.

## Kazi za baadaye (hazijatekelezwa)

- **AWAMU L:** kujenga upya `compliance-trend` na `loan-portfolio` juu ya Injini ya Ukweli. Kwa sasa ni endpoints za zamani zinazorudisha 410.

- **Kufunga mwezi (period lock) na snapshot:** baada ya viongozi kuthibitisha mwezi, hifadhi matokeo yake na zuia mabadiliko ya nyuma bila idhini maalum.
  - Hakuna hatari ya kuhesabu mara mbili hata bila hili, kwa sababu injini inahesabu upya kutoka data ghafi kila mara.
  - Kasi kwa sasa inatosha: wanachama 33 × safu 191, hesabu ya kikundi kizima kwa query moja.
