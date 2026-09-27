# CHANGELOG — ChamaLink

Historia ya mabadiliko inaandikwa hapa, si ndani ya code. Lebo za zamani zilizokuwa kwenye comments (`AWAMU 1.5`, `AWAMU C`, `Fixed19` … `Fixed32c`, `PHASE A FIX`) zimeondolewa kwenye comments za code na maelezo yenyewe yamebaki. Historia kamili ya awamu hizo iko kwenye git (`git log`) na kwenye `docs/v2/08_IMPLEMENTATION_PROGRESS.md`.

## [SASISHO10] 2026-09-27 — Lugha (Kiswahili/English) na mwonekano (Light/Dark)

Frontend pekee. Hakuna badiliko la backend, la hesabu wala la database.

### Lugha
- Kitufe **SW | EN** kiko kwenye kichwa cha dashibodi na kwenye kurasa za kuingia na kujisajili. Chaguo-msingi ni **Kiswahili**, na chaguo linakumbukwa (`localStorage: chamalink.lang`).
- Kiolesura chote kimetafsiriwa: menyu, vitufe, majedwali, fomu, lebo, kadi, ripoti, ujumbe wa WhatsApp, vichwa vya CSV (majina ya faili hayajabadilika), miezi na tarehe.
- Mbinu (`src/i18n/`):
  - Maandishi ya Kiswahili ndiyo **chanzo na ufunguo**: `t('Hifadhi')` hurudisha 'Save' kwa EN. Tafsiri ikikosekana, Kiswahili kinaonekana, kwa hiyo ukurasa hauvunjiki.
  - `tk()` huweka alama kwenye data ya ngazi ya moduli (tabo, hali, orodha); `tn()` hushughulikia wingi.
  - Lugha ikibadilika, kurasa pekee zinajengwa upya (`LangBoundary`). Hali ya kuingia na ya kikundi haipotei.
- Kamusi `src/i18n/en.ts` ina tafsiri 785. `npm run i18n:check` huhakikisha kila `t/tk/tn` ina tafsiri na kwamba vigeu `{x}` vinalingana.
- **Ujumbe wa server bado ni Kiswahili** (awamu ya 2: backend itarudisha misimbo ya makosa itakayotafsiriwa na frontend).

### Mwonekano
- Kitufe cha ☀/☾ kiko karibu na lugha. Chaguo-msingi ni **Light**, na chaguo linakumbukwa (`localStorage: chamalink.theme`). `index.html` huweka mwonekano kabla React haijaanza, kwa hiyo hakuna mmweko wa rangi.
- Rangi zote ni **tokens** (`src/index.css`); dark hubadilisha thamani chini ya `html[data-theme='dark']`. Hakuna `bg-white`, `text-white` au rangi ya moja kwa moja kwenye kurasa (tokens mpya: `on-accent`, `on-gold`, `overlay`).
- Kichwa na paneli ya kuingia hubaki bluu kwa `.theme-chrome`.
- `npm run contrast:check` hukagua jozi za rangi (WCAG):
  - **dark**: jozi zote zinapita (maandishi 6.2–17:1, mipaka ya jedwali na fomu inaonekana);
  - **light**: haijabadilishwa (INFO tu kwa jozi 5 zenye contrast ndogo, kama ulivyoidhinisha).
- Kichwa: menyu ya mstari mmoja kuanzia upana wa 1280px; chini ya hapo inakaa kwenye mstari wa pili unaoteleza. Kwenye simu kuna kitufe kimoja cha lugha.

### Ukaguzi
- `tsc -b`, `vite build`, `i18n:check` na `contrast:check` zimepita. Backend tests 145/145 (pamoja na `NoHardcodedRulesTests` inayokagua frontend).

## [SASISHO9] 2026-09-27 — Ujumbe wazi wa pengo la balance la PDF (kila kikundi)

- **Kugundua** mapengo kulikuwepo tayari kwa kila kikundi (`StatementIntegrity.FindGaps` kila import). Kilichoongezwa ni **maelezo**: `StatementGapDescriber` (sehemu moja, inatumika kila mahali).
- Kila pengo sasa linaonyesha:
  - muamala wa **kabla** na wa **baada** ya pengo: kumbukumbu, jina la mwanachama (au jina la M-Koba kama namba haijaunganishwa), saa, kiasi, balance;
  - balance iliyokuwepo na iliyotarajiwa, na tofauti yake;
  - ujumbe unaoeleza kuwa miamala ya mipakani ni sahihi na imeshahesabiwa, na pengo liko **kati** yake.
- **Import ya PDF**: onyo la kwanza kabisa linasema "Mnyororo wa balance umeruka mara N …", na kila pengo linaonyeshwa kama mnyororo (kabla → ❓ → baada).
- **Muhtasari (Ukweli wa Kikundi)**: bango jekundu tofauti "Balance ya PDF ya M-Koba imeruka mara N" (`statementGapCount/Amount/Outflow` kwenye totals).
- **Tabo ya Mapengo**: kadi mpya yenye mnyororo na ujumbe. "Mtambue mwenyewe" inaonyesha wenye miamala ya mipakani kama **pendekezo** la kuchagua haraka. Hakuna kinachohesabiwa bila ushahidi na idhini ya viongozi wawili (maker-checker haijabadilika).
- Pesa iliyotoka bila mstari (balance kushuka) nayo inaripotiwa.
- Hakuna migration. Hakuna badiliko la hesabu za ukweli.
- Tests: `GapMessage_NamesBothBoundaryRows_ShowsBalances_AndWarnsOnImport`, `GapMessage_OutflowWithoutRow_IsReportedToo_AndGroupsDoNotLeak` (145/145).

## [SASISHO8B] 2026-09-27 — Marekebisho baada ya majaribio ya kwanza

- **Mwanachama mpya** (ombi limeidhinishwa bila kuunganishwa): namba ya simu aliyojisajili nayo inawekwa kama namba yake ya M-Koba, ili malipo yake yaonekane. Namba ambayo tayari ni ya mwanachama mwingine haihamishwi. Rekodi iliyounganishwa haiguswi. Test: `NewMember_GetsTheRegistrationPhoneAsMkobaPhone_ButAPhoneOwnedBySomeoneElseIsNeverMoved`.
- Menyu ya kikundi inaonyesha kifupi (`UMK`) badala ya kodi ya zamani (`CHM-…`).
- Muonekano:
  - Jina la mtumiaji kwenye kichwa halivunjiki mistari miwili.
  - Nafasi mbili kwenye kitufe "Ongeza mwanachama" imeondolewa.

## [SASISHO8] 2026-09-27 — Usajili, uanachama, namba rasmi na taarifa ya mwezi (AWAMU I/J/K)

### Imeongezwa — usajili na uanachama (backend)
- **Mwanachama ≠ Akaunti.** `GroupMember` ni rekodi ya fedha (inaweza kuwa bila akaunti, mf. imeingizwa na viongozi au import). `User` ni akaunti ya kuingia. Zinaunganishwa na kiongozi tu.
- `MembershipService` + `MembershipController` (`/api/membership/...`):
  - Kuanzisha kikundi: aliyeanzisha anakuwa Mwenyekiti, namba `{KIFUPI}-{MWAKA}-001`, na kodi ya kujiunga `KIFUPI-XXXXX` (herufi bila 0/O/1/I).
  - Kujiunga kwa kodi: ombi (`MembershipRequest`) linasubiri idhini. Mtu hawi mwanachama moja kwa moja.
  - **Kulinganisha simu ni pendekezo tu:** namba ya simu ya muombaji ikilingana na rekodi isiyo na akaunti, kiongozi anaoneshwa pendekezo na yeye ndiye anathibitisha kuunganisha. Kuunganisha kunabakiza id ile ile ya mwanachama, kwa hiyo historia yote ya michango inabaki.
  - Rekodi iliyokwisha unganishwa haiwezi kuchukuliwa na akaunti nyingine (Conflict).
  - Maombi ya Katibu au Mtunza-hazina yanaidhinishwa na **Mwenyekiti pekee**. Manaibu wanaruhusiwa (zaidi ya mmoja). Nafasi ya Mwenyekiti haiombwi; inakabidhiwa.
  - Kukataa kunahitaji sababu. Maombi yanayorudiwa yanakataliwa.
  - Kubadilisha nafasi ni kazi ya Mwenyekiti pekee. Kumpa mtu uenyekiti ni makabidhiano (aliyekuwa Mwenyekiti anakuwa Mwanachama). Rekodi bila akaunti haiwezi kushika nafasi ya uongozi.
- **Namba rasmi za uanachama** `{KIFUPI}-{MWAKA}-{NNN}`:
  - Hazibadiliki baada ya kutolewa.
  - Namba za zamani za tarakimu zinahifadhiwa (14 → `UMK-2026-014`). Namba mpya zinaruka namba za zamani ambazo bado hazijatolewa, ili zisigongane.
  - Kifupi kinafungwa baada ya namba ya kwanza kutolewa.
  - Kutoa namba ni kazi ya Mwenyekiti pekee, na ni idempotent.
- Migration `20260927100000_MembershipAndRegistration`:
  - Safu mpya: `Groups.ShortCode`, `Groups.JoinCode` (unique), `GroupMembers.MemberNumberIssuedAt`.
  - Index ya unique `(GroupId, MemberNumber)`. Namba zinazojirudia ndani ya kikundi kimoja zinafutwa, isipokuwa ya kwanza; tazama SOMA_KWANZA kwa ukaguzi wa kabla.
  - Jedwali jipya `MembershipRequests`.
- Kila kitendo cha uanachama kinaandika `AuditEvent` ndani ya `SaveChanges` ile ile (`AuditTrail.cs`, inayotumiwa pia na `TruthSetupService`).
- `AuthService`: namba za simu zinasawazishwa (0712… / +255 712… → 255712…).

### Imeongezwa — taarifa ya mwezi
- `GET /api/truth/{groupId}/monthly-report?year=&month=` (viongozi). Kwa kila mwanachama: lengo, alicholipa, tarehe, hali, faini, deni la mwezi, madeni yote, na Kianzio kilichobaki.
- Jumla za mwezi, pamoja na ujumbe wa WhatsApp uliotengenezwa na seva (simu zimefichwa: `0712***984`).
- Onyo linaonyeshwa pale data ya M-Koba haifiki tarehe ya mwisho ya mwezi.

### Imeongezwa — frontend
- `/jisajili`: kufungua akaunti. Kiungo `/jiunge/KODI` kinaendelea hata baada ya kuingia au kujisajili.
- `/karibu`: kujiunga kwa kodi, au kuanzisha kikundi (pamoja na kunakili kodi na kiungo). Pia maombi yako yanayosubiri, ambayo unaweza kuyaondoa. Aliyeingia bila kikundi analetwa hapa.
- `/wanachama` (viongozi) lina tabo tatu:
  - Orodha: namba rasmi au ya zamani, ana akaunti au la, simu, na kubadilisha nafasi.
  - Maombi: pendekezo la simu, kuunganisha na rekodi yoyote isiyo na akaunti, na onyo kama simu tayari ina akaunti.
  - Kodi na namba.
- Ripoti: tabo mpya **Taarifa ya Mwezi**, yenye jedwali (kadi kwenye simu), CSV, na "Nakili kwa WhatsApp".
- Menyu ya kikundi ina "Jiunge au anzisha kikundi kingine".
- Rangi ya karatasi imekuwa nyeupe zaidi (`#f4f4ef` / `#fdfdfb`).

### Imeondolewa
- `GET /api/Reports/defaulters/{groupId}` (ya mfumo wa zamani). Wadaiwa sasa wako kwenye taarifa ya mwezi.

### Test mpya
- `MembershipServiceTests`: test 10, zinazohusu pendekezo la simu, kuunganisha, namba mpya, ruhusa za Mwenyekiti, sababu ya kukataa, namba zisizobadilika, rekodi bila akaunti, makabidhiano, na kodi ya kujiunga.
- `MonthlyReport_July_MatchesLeadersTruth_ForFrankAndTunganege_WithWhatsAppText`, `MonthlyReport_BeforeTheGroupStarted_IsRejected_NotSilentlyEmpty`.
- E2E ya Postgres: ukaguzi 37 umepita, pamoja na mtiririko wa kivinjari (jisajili → jiunge → idhinisha na unganisha).

## [SASISHO7] 2026-09-26 — Kumbukumbu za ukaguzi na ziba mianya ya hardcode

### Imebadilishwa
- **AuditEvents za Injini ya Ukweli sasa zinaandikwa na `TruthSetupService`**, ndani ya `SaveChanges` ile ile ya kitendo (atomiki). Kila kumbukumbu ina hali ya **kabla na baada**, anayefanya kitendo (UserId, nafasi yake, namba ya uanachama), sababu, na reference.
  - Vitendo vinavyokaguliwa: kanuni, reset, import ya PDF, namba za simu (kuongeza na kuondoa), malipo ya mkononi (ombi, idhini, kukataa), msamaha na siku za neema, na mapengo ya PDF.
  - Awali `TruthController` iliandika kumbukumbu baada ya kitendo kuhifadhiwa, bila hali ya kabla. Pengo likikataliwa, ushahidi wake ulipotea. Nakala hiyo ya controller imeondolewa, ili kumbukumbu iandikwe mahali pamoja tu.
  - Sababu ya mwidhinishaji ya kukataa msamaha au pengo sasa inahifadhiwa.
- Default za kanuni zilizobaki kwenye entities za mfumo wa zamani zimeondolewa:
  - `DueDateDay`, `GracePeriodDays`, `MaxConsecutiveMissedMonths`, `RepaymentDays` / `LoanRepaymentDays`, `LoanCollateralPercent`, `WelfareCollectionThreshold`.
  - Huduma ya mkopo sasa inakataa waziwazi pale siku za kurejesha hazijawekwa.

### Imeondolewa (hardcode zilizogunduliwa na test mpya)
- `new DateTime(2026, 9, 1)` kwenye `ObligationEngine` na `DebugController` (ripoti "ilisimama" tarehe hiyo).
- `AvailableSavings = 1000000` kwenye hakikisho la `TreasuryImportService`.

### Test mpya
- `Backend_Has_No_Hardcoded_Large_Numbers_Without_Decimal_Suffix`: inakamata `decimal x = 50000;` (namba bila `m`).
- `Backend_Has_No_Hardcoded_Calendar_Dates`.
- Audit: `ManualPayment_LeavesCompleteAuditTrail_WhoWhatWhenEvidenceAndApprover`, `RejectedManualPayment_IsStillInAuditTrail`, `SettingsChange_IsAudited_WithBeforeAndAfter`, `AuditTrail_SurvivesTestDataReset`, `RejectedGap_ClearsGap_ButAuditKeepsTheEvidence`, `TruthAudit_IsWrittenInOnePlace_InsideTheSameSaveChanges`.

## [SASISHO6] 2026-09-26 — Ukaguzi, ushahidi na kuondoa hardcode

### Imeongezwa
- **Ushahidi wa malipo ya mkononi:**
  - Safu mpya `FinancialEvents.EvidenceType` na `EvidenceReference` (migration `20260926100000_ManualPaymentEvidence`).
  - Ombi jipya lisilo na aina na mahali pa ushahidi linakataliwa.
  - Mwidhinishaji anaona ushahidi kabla ya kuidhinisha.
- **Setting `TruthGroupSettings.AutoDeductSavings`:** sheria ya "akiba ilipe mwezi usiolipwa" sasa ni uamuzi wa kikundi. Awali ilikuwa `true` ndani ya code.
- `NoHardcodedRulesTests`: inakagua backend na frontend. Kiasi cha fedha kikiandikwa kwenye code, build inaanguka.
- `ChamaLink.Domain/MkobaPlatform.cs`: sehemu moja ya msimbo wa nchi, saa za eneo na kusafisha namba za simu.
- `BUSINESS_RULES_UKONGA.md` v3.0: kila sheria imeunganishwa na setting, method na test.

### Imebadilishwa
- Setting za kikundi za mara ya kwanza lazima ziwe kamili. Hakuna thamani za kubuni kama 10,000, 5,000, 50,000 au tarehe 5, iwe backend au frontend.
- Vitufe vya haraka vya Kianzio kwenye frontend vinatokana na setting za kikundi.
- `WelfarePenaltyBackgroundService` inasoma faini kutoka setting za kikundi. Kikundi kisicho na setting kinarukwa, na onyo linaandikwa kwenye log.

### Imeondolewa
- `DebugController`:
  - namba za kubuni (80,000 na 50,000) kwenye position-proof;
  - `try/catch` 13 zilizorudisha `StackTrace` kwa mtumiaji.
- `BusinessRuleEngine.CreateDefaultPolicy`.
- Thamani za kubuni za fedha kwenye entities za zamani, yaani `GroupPolicy`, `JoinFeeSettings` na `EventSettings`. Mfano: Kianzio 50,000 kiliwekwa kimya kimya hata kikundi kilipoweka 0.
- Hati ya kanuni v2 imehamishiwa `docs/archive/` na alama ya IMEPITWA.

### Baadaye
- Kufunga mwezi (period lock) na snapshot za kila mwezi.
- Kuondoa kabisa huduma za zamani (endpoints zake tayari ni 410).

## [SASISHO5] — Ulinzi
- Endpoints za zamani zinarudisha 410 (`[LegacyEndpoint]`); Debug inahitaji kuingia.
- Hesabu ya kikundi kizima kwa query moja (`LoadRawAsync`); `docs/truth-engine/SCHEMA_YA_DB.md`.

## [SASISHO4] — Ripoti
- Kitabu cha fedha (cashbook) kwa mwezi, pamoja na salio la mwanzo na mistari inayokosekana. Ripoti mpya kwenye frontend.

## [SASISHO3] — Malipo ya mkononi
- Ukurasa wa malipo ya mkononi wenye hakikisho (preview) na idhini ya kiongozi wa pili. Endpoints za mapengo na za wanachama.

## [Injini ya Ukweli v2] 2026-09-25
- `ContributionTruthEngine`: mgawanyo unahesabiwa upya kutoka data ghafi ya M-Koba, pamoja na malipo ya mkononi yaliyoidhinishwa.
