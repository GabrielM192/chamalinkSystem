# CHANGELOG — ChamaLink

Historia ya mabadiliko inaandikwa hapa, si ndani ya code. Lebo za zamani zilizokuwa kwenye comments (`AWAMU 1.5`, `AWAMU C`, `Fixed19` … `Fixed32c`, `PHASE A FIX`) zimeondolewa kwenye comments za code na maelezo yenyewe yamebaki. Historia kamili ya awamu hizo iko kwenye git (`git log`) na kwenye `docs/v2/08_IMPLEMENTATION_PROGRESS.md`.

## [SASISHO15] 2026-09-30 — Mikopo salama, vikao salama (auth), miamala ya DB, na marekebisho ya ukaguzi wa nje

**Ina migration mpya** (`20261001100000_Sasisho15Hardening`). Linawekwa juu ya SASISHO14.

### Mikopo (F-H1 — hatari kubwa zaidi)
- Njia za zamani ambazo **mtu mmoja** angeweza kutoa mkopo / kutangaza umeshindikana (`POST /api/loan/group/{gid}`, `/{id}/repay`, `/{id}/mark-defaulted`) sasa zinarudisha **410 Gone** na kuelekeza kwenye njia mpya.
- Mtiririko mpya `api/loan-applications/{groupId}`: **ombi → kura za viongozi huru → Mtunza-hazina anaanzisha utoaji → kiongozi MWINGINE anathibitisha kwa rejea ya M-Koba**.
  - Masharti yote (riba, miezi, kikomo cha kiasi / mara ya michango, uanachama, deni, mkopo mmoja, akiba ya lazima, kura, siku za idhini) yanatoka **Mipangilio ya kikundi** (maker-checker), si kwenye code wala ombi.
  - Mkopaji, aliyewasilisha ombi, na ndugu zake hawapigi kura. Ombi lisiloweza kupata kura za kutosha linakataliwa mwanzoni (halikwami).
  - Utoaji unathibitishwa tu kwa utoaji halisi wa M-Koba: kiasi sawa, baada ya idhini, rejea ambayo haijatumika. **Rejea moja = jambo moja** (mafao, mkopo au maelezo ya matumizi) — `WithdrawalRegistry`.
  - Mkopo ulioidhinishwa unatengewa pesa (`reserved`) hadi utolewe au ughairiwe.
  - "Umeshindikana" kunahitaji viongozi wawili; deni halifutwi.
- Frontend: ukurasa mpya **/mikopo** (Mikopo), sehemu ya **sera ya mikopo** kwenye Mipangilio.
- Sera ya mikopo isiyokamilika inakataliwa **wakati wa kupendekeza** (si kiongozi wa pili akiidhinisha).

### Vikao salama (auth)
- Access token ya muda mfupi (`Jwt:AccessTokenMinutes`, chaguo-msingi 15) + **refresh token kwenye cookie ya HttpOnly** (`chamalink_rt`, SameSite=Strict, Path=/api/auth) inayozungushwa kila matumizi.
- Cookie iliyotumika tena (imeibiwa) → familia yote ya vikao inafungwa. Logout, **"Toka kwenye vifaa vyote"** (revoke-all) na claim `tv` inayobatilisha access token papo hapo.
- Akaunti inafungwa kwa muda baada ya majaribio mengi (`Auth:MaxLoginFailures`, `Auth:LockoutMinutes`). Usajili unarudisha 202 ile ile (hakuna kufichua akaunti zilizopo). ClockSkew dakika 1. Kikomo cha refresh/logout (`RateLimit:SessionPerIpPerMinute`).
- Token haihifadhiwi tena kwenye localStorage (iko kwenye kumbukumbu tu); ukurasa ukipakiwa upya, kikao kinarudishwa kimya kwa cookie.

### Usalama wa seva
- `SecurityHeadersMiddleware`: nosniff, X-Frame-Options DENY, CSP ya API, Referrer-Policy (HSTS nje ya Development).
- Njia za debug zinalindwa (`DebugAccessAttribute`).
- Vyeti vya mafao vinasimbwa (AES-256-GCM) vikiwa kwenye diski ukiweka `Benefits:EvidenceEncryptionKey`.
- Makosa ya mteja (4xx) yanaandikwa kama Warning bila stack trace (logi zisijae kelele); 5xx bado ni Error kamili.

### Uthabiti wa data (miamala na ushindani)
- `DbTx`: transactions zilizo wazi + kufuli za kikundi kwa kufungua kesi, maombi ya mkopo, utoaji, na **maamuzi yote ya maker-checker** (malipo ya mkono, namba, msamaha, sera, uhusiano, viwango, "umeshindikana", maombi ya uanachama). Maombi mawili ya wakati mmoja hayawezi kuidhinisha mara mbili.
- Concurrency token (`xmin`) kwa kesi za mafao na maombi ya mkopo; unique index ya rejea ya utoaji wa mkopo.

### Mafao na Injini ya Ukweli
- F-M2: mhakiki wa sasa hahesabiwi kama mpiga kura (kesi haiingii kwenye kura isiyowezekana).
- F-M3: sera inaonyesha **tahadhari za uwezo** (`capacityWarnings`) kikundi kikiwa na viongozi wachache.
- F-L2: kuhakiki cheti kisichokubalika kwa kiwango hicho kunarudisha **onyo** kwa mhakiki.
- Uhusiano mpya wa wanachama ni **Pending** mpaka kiongozi wa pili aidhinishe (kiongozi mmoja hawezi kuwafungia wenzake kura).
- Makato ya mafao kwenye taarifa ya mwanachama yanasomwa kutoka **daftari la mafao** lisilobadilika (kesi zilizolipwa).
- Faini zilizosamehewa: idadi na **kiasi** (`finesWaivedAmount`, `fineWaivedAmount` kwa mwezi).

### Majaribio
- `dotnet test`: **197/197**.
- QA kamili kwenye PostgreSQL na data halisi ya Ukonga (PDF ya M-Koba): **326 PASS / 0 FAIL** (awamu 1–7) + UI smoke 12/12. Ripoti: `RIPOTI_YA_MAJARIBIO_SASISHO15.md`.

## [SASISHO14] 2026-09-29 — Mapengo 12 ya ukaguzi wa mafao + ujumbe wa seva kwa Kiingereza

**Ina migration mpya** (`20260930100000_BenefitsHardening2`). Sasisho hili ni **la jumla**: lina SASISHO13 ndani yake (migration `20260929100000_BenefitsHardening` pamoja na mafaili yake). Kwa hiyo linawekwa moja kwa moja juu ya SASISHO12. Kanuni zote za fedha zinatoka kwenye sera ya kikundi; hakuna namba iliyoandikwa kwenye code.

### Pengo 1 — Kura haiwezi kuidhinisha malipo bila pesa
- Kura ikipita, pesa ya M-Koba inakaguliwa (sera: `CashCheckAtApproval`). Pesa isipotosha, kesi inakuwa **`AwaitingFunds`** ("Imeidhinishwa — inasubiri pesa"), si "Imeidhinishwa".
- Mtunza-hazina anaingiza PDF mpya ya M-Koba, kisha anabonyeza **"Kagua pesa tena"** (`POST cases/{id}/recheck-funds`). Kesi inasogea tu salio likitosha.
- Wapiga kura wanaona kadi ya "Pesa ya M-Koba (ukweli)" kabla ya kupiga kura.

### Pengo 2 — Ndugu wa aliyefungua kesi
- Ndugu wa **aliyefungua kesi** (si wa mnufaika tu) hawezi kuhakiki, kupiga kura wala kulipa.
- Sera inachagua aina za uhusiano zinazozuia (`ConflictRelationTypes`: mwenza, mzazi, mtoto, ndugu, kaya, mbia, nyingine).

### Pengo 3 — Nafasi zinatoka kwenye sera
- Sera inataja nani anapiga kura (`ApproverRoles`) na nani anahakiki ushahidi (`EvidenceVerifierRoles`).
- **Wakaguzi** (`AuditorMemberIds`) ni wanachama wanaoweza kusoma tu. Wanaona Daftari la mafao, Ulinzi na hatari na uhakiki wa mnyororo, lakini hawawezi kubadilisha kitu.

### Pengo 4 — Malipo makubwa
- Kuanzia `HighValueThreshold`, aliyepiga kura ya NDIYO **hawezi** kuanzisha malipo. Lazima mtu mwingine aanzishe.

### Pengo 5 — Mhusika yule yule
- Mhusika mmoja (mf. "Mama X") ni dai moja **bila kujali** uhusiano ulioandikwa (mzazi/mwenza…).
- Sera inaweza kuzuia mhusika huyo kudaiwa na mwanachama mwingine (`BlockSameSubjectAcrossMembers`). Ikiwa haizuii, inabaki tahadhari kwa wahakiki.

### Pengo 6 — Anayelipa anaona anamlipa nani
- Hatua ya "Anzisha malipo" inaonyesha jina, **namba ya mwanachama** na **simu iliyoidhinishwa** ya mnufaika.
- Kulipa simu nyingine kunahitaji uthibitisho maalum na maelezo.

### Pengo 7 — Deni la mkopo
- Sera `DeductLoanArrears`: deni la mkopo lililochelewa linakatwa kando (`LoanArrearsDeducted`).
- Linaandikwa kwenye daftari kama `MAFAO-MKOPO-{kesi}`.

### Pengo 8 — Taslimu ya kihistoria (kabla ya M-Koba)
- Kesi ya zamani inaweza kurekodiwa kama **Taslimu** (`PaymentChannel = Cash`). Inahitaji yafuatayo:
  - tarehe iliyo **kabla** ya `ManualEntryCutoff` ya kikundi;
  - sababu ya kipekee;
  - jina (na simu) ya aliyepokea;
  - **risiti** iliyohakikiwa.
- Watu watatu tofauti wanahitajika: aliyerekodi ≠ mhakiki wa risiti ≠ mthibitishaji.
- Ikishathibitishwa, kesi hiyo **haipokei ushahidi mpya** (snapshot lock).

### Pengo 9 — Sera haianzi nyuma
- Toleo jipya la sera lina `EffectiveFrom`, na haliwezi kuanzia tarehe iliyopita.
- Sera ya baadaye inatumika kuanzia tarehe yake. Kesi hutumia sera iliyokuwa hai siku ya tukio.

### Pengo 10 — Kiwango kinaisha; tarehe ya kuhesabu
- Kiwango (rate) kina `EffectiveTo`.
- Sera inachagua **tarehe ya kuhesabu wanachama** (`HeadcountAsOf`): siku ya tukio au siku ya kufungua kesi.

### Pengo 11 — Daftari ndani ya mnyororo wa hash
- Kila andiko la fedha la mafao (Approved, JoinFeeOffset, ArrearsOffset, LoanArrearsOffset, CashPaid) linaingia pia kwenye mnyororo wa ukaguzi (`BenefitLedgerPosted`).
- "Kagua pesa tena" nayo inaingia (`BenefitFundsRechecked`).

### Pengo 12 — Ujumbe wa seva kwa Kiingereza
- Ujumbe wa seva sasa unabeba **kiolezo + vigeu** (`Loc`). Mifano:
  - makosa (`template`, `args`, `details`);
  - ukaguzi wa ustahiki;
  - bendera za hatari;
  - tahadhari;
  - arifa (`LocJson`);
  - sababu za kuzuiwa;
  - uhakiki wa mnyororo.
- Frontend inatafsiri violezo hivi (`src/i18n/server-en.ts`, `src/lib/loc.ts`). Kwa hiyo EN mode haionyeshi tena Kiswahili kutoka seva.
- Kiswahili kinabaki chaguo-msingi.

### Marekebisho yaliyopatikana wakati wa majaribio (PostgreSQL + API + kivinjari)
- **Tarehe za malipo (`PaidAt`)**: Npgsql inakataa `DateTime` isiyo na Kind=Utc. Malipo ya taslimu na ya kiasi-sifuri yalikuwa yanaanguka na "Hitilafu ya database". Yamerekebishwa.
- Tests sasa zina `UtcOnlyInterceptor`, inayokataa tarehe isiyo UTC kwenye mafao, ili kosa hili lisirudi.
- Bendera ya "Utoaji wa M-Koba usioelezwa" kwenye Ulinzi na hatari sasa inatafsiriwa.
- Kichwa cha arifa ya mgao ("Msiba: …") na arifa za kura ya hali ya mwanachama sasa zinatafsiriwa.
- Uwazi: "inasubiri malipo/pesa" inaonyesha kiasi kwa koma. Lebo "Pesa iliyolipwa" haisemi tena "(M-Koba)", kwa sababu inajumuisha taslimu ya kihistoria.
- Swagger (`/swagger/v1/swagger.json`) ilikuwa inatoa 500 kwa sababu `VoteRequest` mbili zilikuwa na jina moja. Imerekebishwa (`CustomSchemaIds`).

### Majaribio
- `dotnet test`: **181/181**.
- Migration ilijaribiwa kutoka hali ya SASISHO12 kwa `dotnet ef database update`, kwenye DB yenye kesi iliyolipwa. Matokeo:
  - maandishi 4 ya fedha yaliandikwa;
  - matoleo ya sera na tarehe za kuanza yalijazwa;
  - trigger ya AuditEvents inazuia kufuta;
  - schema inalingana 100% na model.
- API halisi: sera → kesi → ushahidi → kura → AwaitingFunds → malipo; taslimu ya kihistoria; kesi ya kihistoria ya M-Koba (DFNO319K1X, 20,000 × 32).
- UI imejaribiwa kwa SW na EN, light na dark, kama Mwenyekiti, Mtunza-hazina na Mkaguzi.

## [SASISHO13] 2026-09-29 — Ulinzi wa mafao: mgawanyo wa majukumu, pesa halisi, mnyororo wa fedha, ukaguzi usiofutika

**Ina migration mpya** (`20260929100000_BenefitsHardening`). Imejengwa juu ya SASISHO12. Inatekeleza mapendekezo B1–B10 ya mapitio ya nje. Backend ilitangulia, frontend ikafuata.

### B1 — Sera yenye matoleo
- Kila sera iliyoidhinishwa ina **toleo** (1, 2, 3…). Kesi inahifadhi toleo lililotumika (`PolicyVersion`). Sera ya zamani haibadiliki kamwe; mabadiliko ni toleo jipya linaloidhinishwa na kiongozi wa pili.
- Sehemu mpya za sera:
  - `RequireJoinFeePaid`;
  - `CooldownDays`;
  - `MaxBenefitAmountPerPeriod` pamoja na `MaxBenefitPeriodDays`;
  - `RequireCashAvailability`;
  - `PaymentInitiatorRoles` na `PaymentConfirmerRoles`;
  - `ApproverCannotInitiatePayment`;
  - `ExcludeRelatedParties`;
  - vizingiti vya tahadhari `FrequentClaimsFlagCount` na `RecentPolicyChangeFlagDays`.
- Hakuna thamani iliyoandikwa kwenye code. Sera inakataliwa ikiwa si salama au inajipinga. Kwa mfano: nafasi za malipo lazima ziwe za viongozi, na huwezi kuchagua "Kianzio kiwe kimelipwa" pamoja na "kata Kianzio". Mtu mmoja hawezi kuanzisha na kuthibitisha malipo yale yale, hata kama nafasi yake iko pande zote mbili.
- **Sera zilizopo** zinapewa: Mtunza-hazina anaanzisha; Mwenyekiti au Katibu anathibitisha; pesa ya M-Koba lazima iwepo; ndugu hawashiriki. Viongozi wanaweza kupendekeza toleo jipya.

### B2 — Ustahiki na mgongano wa maslahi
- Ukaguzi mpya:
  - **COOLDOWN**: siku za kusubiri kati ya madai;
  - **MAX_AMOUNT_PERIOD**: kikomo cha jumla kwa kipindi;
  - **JOIN_FEE_PAID**;
  - **SUBJECT_OTHER_MEMBER**: tahadhari kwa wahakiki ikiwa mhusika huyo tayari anadaiwa na mwanachama mwingine (pia ni bendera ya hatari).
- **Uhusiano wa wanachama** (mwenza, mzazi, mtoto, ndugu, kaya moja, mbia wa biashara…) unasajiliwa na viongozi. Kuuondoa kunahitaji kiongozi wa pili.
- Ndugu wa mnufaika **hawezi** kuhakiki cheti, kupiga kura, kuanzisha malipo wala kuyathibitisha.

### B3 — Vyeti
- SHA-256 inakaguliwa upya kila cheti kinapofunguliwa au kuhakikiwa. Faili likibadilishwa: hatua inazuiwa na tukio `EVIDENCE_TAMPERED` linarekodiwa.

### B4/B5 — Idhini ≠ Malipo, na pesa halisi ya M-Koba
- Hali mpya **PaymentInitiated**. Mtiririko sasa ni: Imeidhinishwa → **Anzisha malipo** (Mtunza-hazina) → **Thibitisha kwa rejea ya M-Koba** (mtu mwingine, mf. Mwenyekiti/Katibu) → Imelipwa.
- Pesa inasomwa kutoka salio la mwisho la M-Koba (si `Group.Balance`), ukiondoa kesi nyingine zinazosubiri malipo. Pesa isipotosha, malipo hayaanzishwi (409) na bendera `CASH_INSUFFICIENT` inawekwa.
- Masharti ya uthibitisho:
  - Anayethibitisha ≠ aliyeanzisha.
  - Aliyepiga kura ya NDIYO asianzishe malipo (kama sera inataka).
  - Kiasi lazima kilingane kabisa na kilichoidhinishwa.
  - Utoaji lazima uwe baada ya idhini, na rejea moja haitumiki mara mbili.
- Pesa ikienda kwa namba isiyo ya mnufaika: kiongozi lazima akiri na aeleze, na bendera `PAYMENT_TO_OTHER_PHONE` inawekwa.
- Kesi iliyolipwa haibadilishwi wala kufutwa.

### B6 — Mafao kwenye mnyororo wa fedha
- Kila kesi iliyolipwa inaandikiwa `BenefitLedgerEntries`: **Approved = JoinFeeOffset + ArrearsOffset + CashPaid**.
- Ulinganisho unakagua mambo matatu: hesabu hiyo, kwamba rejea ya M-Koba ipo na ina kiasi sawa, na kwamba makato yanaonekana kwenye Injini ya Ukweli.
- Kesi zilizokwisha kulipwa zinaandikiwa na migration.

### B7 — Ukaguzi usiofutika na bendera za hatari
- `AuditEvents` ina mnyororo wa hash (`ChainIndex`, `PrevHash`, `Hash`), unaofungwa na huduma ya nyuma kila baada ya muda.
- Trigger ya PostgreSQL inazuia DELETE, na UPDATE ya kitu chochote isipokuwa kujaza hash mara moja.
- `GET audit/verify` inaonyesha tukio lililobadilishwa.
- Dashibodi ya hatari inaonyesha:
  - kumbukumbu zilizoharibiwa;
  - mhusika mmoja kudaiwa mara mbili, madai mengi;
  - aliyeidhinisha ni mnufaika au ndugu yake;
  - aliyehakiki = aliyepakia, mthibitishaji = mweka-malipo;
  - malipo kabla ya idhini, kiasi tofauti, namba nyingine;
  - pesa haitoshi;
  - kumbukumbu za mkononi baada ya cutoff;
  - sera kubadilika kabla ya dai;
  - utoaji usioelezwa, malipo yasiyoonekana M-Koba;
  - maandishi ya fedha yasiyolingana;
  - kila **jaribio lililozuiwa** (`BLOCKED_*`).

### B8 — Uwazi
- Tabo **Uwazi** (kila mwanachama) inaonyesha kila kesi: tukio, mnufaika, kiwango × idadi, jumla, makato, kilicholipwa, rejea ya M-Koba na hali. Haina vyeti wala namba za simu.

### B9 — Frontend
- **Kesi:**
  - hatua 3 za malipo, pamoja na kadi ya pesa ya M-Koba (salio, zilizotengwa, inapatikana, inahitajika);
  - sababu ya kitufe kuzimwa;
  - kukiri malipo kwa namba nyingine;
  - mnyororo wa fedha na ulinganisho;
  - toleo la sera;
  - onyo la mgongano wa maslahi.
- **Sera:** sehemu mpya za ulinzi, malipo (nafasi za kuanzisha/kuthibitisha) na tahadhari.
- **Ulinzi na hatari** (viongozi): bendera za hatari, hali ya mnyororo wa ukaguzi, ulinganisho, na uhusiano wa wanachama.
- Maandishi yote yapo kwa Kiingereza pia.

### B10 — Tests
- Tests 10 mpya za udanganyifu na idhini: sera isiyo salama, ndugu, pesa haitoshi, aliyeidhinisha kuanzisha, cooldown na kikomo, mhusika mmoja, mnyororo wa hash, uwazi, cheti kilichobadilishwa, kesi ya Net 0.
- FullFlow imeandikwa upya kwa hatua ya kuanzisha malipo.
- **Tests 169/169.** Migration na trigger zimejaribiwa kwenye PostgreSQL 17.

## [SASISHO12] 2026-09-28 — Maombi ya mafao kwa mwanachama, tafsiri ya tahadhari, menyu isiyogongana

Hakuna migration. Juu ya SASISHO11.

- **Mwanachama anaomba mafao mwenyewe:** Mafao → Yangu → "Omba mafao".
  - Anajaza tukio: msiba au sherehe, linamhusu nani, jina kama lilivyo kwenye cheti, na tarehe. Anaona ukaguzi wa ustahiki na kiasi, kisha anatuma ombi.
  - Baada ya kutuma, ombi linafunguka na anapakia cheti chake.
  - Viongozi wanapata arifa "Ombi jipya la mafao". Kiongozi yeyote (si mwombaji) anahakiki cheti, kisha kura zinaanza.
  - Mwanachama anaweza kuomba **kwa ajili yake mwenyewe tu** (seva inakagua), na anaweza kughairi ombi lake **kabla kura hazijaanza**.
  - "Maombi na mafao yangu" inaonyesha hali ya kila ombi. Ombi linalosubiri cheti linaonyeshwa juu kama tahadhari.
- **Tahadhari zinatafsiriwa:** seva inarudisha `code` pamoja na vigeu (`args`), na frontend inaonyesha kwa lugha iliyochaguliwa. Maelezo ya utoaji wa M-Koba nayo yanatafsiriwa. Tahadhari ya "sera haijawekwa" haijirudii tena.
- Viungo vya tahadhari vimerekebishwa: namba za simu → `/ukweli/simu`, kura za hali → `/wanachama/kura`.
- **Kichwa cha ukurasa:** menyu sasa ina mstari wake chini ya kichwa kwa upana wote. Jina la kikundi, arifa na mipangilio haviwezi tena kufunika "Ripoti"/"Mipangilio" kwenye skrini kubwa.
- Tests 159/159.

## [SASISHO11] 2026-09-28 — Mafao (msiba/sherehe), udhibiti wa watu wawili, faini ya asilimia, jumla ya michango

Utekelezaji wa ushauri `USHAURI_MAFAO_ULINZI_2026-09-28.md` (sehemu 3–6). **Ina migration mpya** (`20260928100000_BenefitsAndGovernance`).

### Mafao — ukurasa mpya "Mafao" (`/mafao`)
- **Sera ya kikundi** (viongozi 2: mmoja anapendekeza, mwingine anaidhinisha): kura za NDIYO zinazohitajika, viongozi wa chini kati yao, kama wanachama wa kawaida wanapiga kura, kura za HAPANA zinazokataa, siku za kura, kanuni ya idadi (wote walio hai / wasio na deni / waliolipa ndani ya siku N), kumwondoa mnufaika kwenye idadi, chanzo cha pesa, makato (Kianzio, deni), ustahiki (siku za uanachama, muda wa kuwasilisha, asiwe na deni), siku za malipo kuonekana M-Koba, kura za kubadilisha hali, asilimia ya kufukuza. **Hakuna namba iliyoandikwa kwenye code**: sera isipowekwa, kesi haifunguliwi (409).
- **Viwango** kwa kila aina × uhusiano, vyenye `EffectiveFrom` (vya zamani havibadiliki), vinavyoidhinishwa na kiongozi wa pili.
- **Kesi**: ombi → cheti (SHA-256, namba ya cheti; cheti kimoja hakitumiki mara mbili; anayehakiki ≠ aliyepakia ≠ mnufaika) → ukaguzi wa ustahiki wenye sababu → hesabu iliyogandishwa (`kiwango × idadi`, orodha ya waliohesabiwa, makato, Net) → kura (mnufaika na aliyefungua hawapigi) → uthibitisho kwa Ref ya M-Koba yenye kiasi = Net.
- **Kesi za zamani** (utoaji wa Feb 390k, Jun 640k, Jul 435k): zinarekodiwa kwa Ref ya M-Koba na kuthibitishwa na kiongozi wa pili.
- **Ulinganisho wa M-Koba**: kila Withdraw isiyo na kesi inaonekana kama "Haijaelezwa" (tahadhari). Viongozi wanaweza kuieleza (mkopo, gharama, marejesho…), na maelezo hayo yanaidhinishwa na kiongozi wa pili. Kesi iliyoidhinishwa isiyolipwa ndani ya siku za sera → tahadhari.
- **Daftari la mafao** (CSV/Excel) na **"Yangu"**: mafao uliyochangia/uliyopokea, na kura zinazokusubiri.
- Makato ya Kianzio kutoka kwenye mafao yanaingia kwenye taarifa ya mwanachama kama malipo (`MAFAO-JF`).

### Arifa
- Kengele 🔔 kwenye kichwa (inasasishwa kila dakika ukurasa ukiwa wazi). Arifa zinatumwa kwa kesi mpya, kura, uamuzi, malipo, na kura za hali.

### Hali ya mwanachama kwa kura (hakuna kiongozi mmoja)
- Kitufe **"Hali"** kwenye orodha ya wanachama → ombi (Simamishwa, Hayuko hai, Ametoka/Kufukuzwa, Amefariki, Kurejeshwa…) → kura. Kufukuzwa = asilimia ya sera ya wanachama WOTE (Katiba 12(3): ¾). Tabo **"Kura za hali"** kwa kila mwanachama.
- Mwanachama akitoka, madeni mapya yanasimama kuanzia tarehe ya uamuzi.

### Namba za simu — idhini ya watu wawili na kipindi
- Kuunganisha namba = **ombi** linalosubiri kiongozi wa pili. Mpaka lithibitishwe, malipo ya namba hiyo **hayahesabiwi** kwa mtu yeyote.
- Namba inaweza kuwa na **kipindi** (`ValidFrom/ValidTo`). Mf. Fauzia (255658479089) → Abdulla hadi tarehe fulani, kisha 255725331812.
- Kufunga namba ni ombi pia (kiongozi wa pili). Malipo ya kabla ya tarehe ya kufunga yanabaki kwa mwanachama.
- Namba zilizopo kabla ya sasisho **zinabaki zimeidhinishwa** (migration).

### Kanuni za kikundi
- **Faini: kiasi kamili AU asilimia ya mchango** (Katiba 13: 50%). Ukichagua asilimia, faini inafuata mchango ukibadilika.
- Baada ya mipangilio ya kwanza, **kila badiliko ni pendekezo** linaloanza kutumika kiongozi wa pili akiidhinisha. Kuna jedwali la tofauti (sasa → inapendekezwa) na historia. Pendekezo lisilobadilisha kitu au lenye thamani batili linakataliwa papo hapo.

### Ulinzi
- Idhini ni **fail-closed**: sera ya utoaji isipowekwa → 409 (hakuna tena `TreasurerOnly` ya kimya kimya). Aliyeomba utoaji hawezi kuuidhinisha. `WithdrawalController` ya zamani imefichwa nyuma ya `Legacy:Endpoints`.
- **Reset ya data imezimwa**: inafanya kazi tu `Truth:AllowReset=true` ikiwa kwenye appsettings (dry-run inaruhusiwa daima).
- **Kikomo cha maombi kwa kila mtumiaji** (si kwa IP; CGNAT ya mitandao ya simu). Viwango vinatoka `RateLimit:*`.

### Taarifa ya mwanachama
- Kadi **"Jumla uliyochangia tangu ujiunge"** = M-Koba + mkononi (bila makato ya mafao), pamoja na mgawanyo: michango, faini, Kianzio, mkopo, akiba. Kuna ukaguzi wa ndani (✔ au ⚠ tofauti). Kwenye "Taarifa Yangu" kuna pia mafao uliyochangia/uliyopokea na bango la kura zinazokusubiri.

### Ukaguzi
- Backend tests 158/158 (mpya: `BenefitsAndGovernanceTests`, udhibiti wa namba za simu wa watu wawili). `tsc`, `vite build`, `i18n:check` (tafsiri mpya 358) na `contrast:check` zimepita. Kila njia mpya imejaribiwa kwenye PostgreSQL halisi.

### Bado (awamu zijazo)
- Hash chain ya audit log; hesabu ya kujitoa (Katiba 8(3), hisa); vyeti kwenye object storage + signed URLs (sasa ni diski ya server, `Benefits:EvidencePath`); kuingiza PDF kama background job; cache ya Injini ya Ukweli; AWAMU L (ripoti za mwenendo/mikopo).

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
