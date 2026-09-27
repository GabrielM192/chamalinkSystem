# Marekebisho — 2026-09-15

Hii ni rekodi ya kazi iliyofanywa baada ya ukaguzi (`RIPOTI-YA-UKAGUZI.md`).
Kila kitu kilichotajwa hapa **kimejengwa na kuthibitishwa**, si kupendekezwa tu.

---

## Matokeo ya uthibitisho (mwisho wa kazi)

| Jaribio | Amri | Matokeo |
|---|---|---|
| Build ya backend | `dotnet build ChamaLink.sln -c Release` | ✅ **0 Errors, 0 Warnings** |
| Majaribio | `dotnet test ChamaLink.Tests` | ✅ **14/14 yamepita** |
| Build ya frontend | `npm run build` | ✅ 2359 modules, 0 errors |
| Migrations | `dotnet ef database update` (PostgreSQL 17 halisi) | ✅ **Done.** |
| API halisi | `dotnet run` + endpoints 16 za Reports | ✅ **16/16 → HTTP 200** |
| LedgerEntry.UserId | query moja kwa moja kwenye DB | ✅ **1 entry, 0 bila UserId** |
| Faili baya (M-Koba/Treasury) | upload ya faili isiyo halisi | ✅ **HTTP 400** (zamani: 500) |
| "Huna ruhusa" | Member anajaribu kazi ya Chairperson | ✅ **HTTP 403** (zamani: 401) |
| Token ya uongo | key ya zamani iliyoingia GitHub | ✅ **HTTP 401** (zamani: 200) |
| API + key iliyovuja | `Jwt__Key=<key ya GitHub>` | ✅ **inakataa kuanza** |
| API + key fupi | `Jwt__Key=fupi` | ✅ **inakataa kuanza** |

---

## Kilichorekebishwa

### 🔴 C-1 + C-2 — Services 3 zilizokuwa hazijasajiliwa kwenye DI

`ComplianceReportService`, `TreasuryImportService`, `ITreasuryExcelParserService`.
Ziliongezwa kwenye `ChamaLink.API/Program.cs`.

* **Kabla:** endpoints **18** (Reports 16 + Treasury 2) → HTTP 400 kila mara
* **Baada:** Reports **16/16 → 200**; Treasury inafika kwenye parser
* **Uthibitisho:** niliondoa usajili mmoja kwa makusudi → `dotnet build` ilisema
  *"Build succeeded"* (haikugundua), lakini `dotnet test` ilisema
  *"Failed: ReportsController: Unable to resolve service for type 'ComplianceReportService'"*

### 🔴 C-3 — Siri zimeondoka kwenye faili za git

* `appsettings.json` na `appsettings.Development.json` **hazina tena** JWT key wala neno la siri la DB
* `Program.cs` sasa **inakataa kuanza** ikiwa `Jwt:Key`: haipo, ni fupi < 32, ni `REPLACE_ME`, au ni ile ile iliyoingia GitHub
* Njia tatu za kuweka siri: `appsettings.Development.local.json` (haipo git), User Secrets, au env vars
* `.gitignore` mpya ya root + faili **246** za `bin/obj` zimeondolewa kwenye git index
* `chamalink-web-new/.env` imeondolewa kwenye git index
* **Uthibitisho:** `Jwt__Key=<key ya GitHub>` → `Unhandled exception: Unatumia JWT key ILE ILE iliyo kwenye GitHub ya umma`; token niliyotengeneza kwa key ya zamani → **HTTP 401**

> ⚠️ **Bado unahitaji kufanya hili mwenyewe:** purge git history (`git filter-repo`) au anzisha repo mpya. Siri iliyokwisha-ingia kwenye history haiondolewi kwa kuifuta faili tu. Na chukulia token zote zilizotolewa kabla ya leo kuwa zimevunjwa.

### 🟠 H-1 — `LedgerEntry` ilikuwa inaandikwa bila `UserId`

`LedgerService.RecordContributionAsync` sasa inaweka `UserId = groupMember.UserId`.

* **Kabla:** `LedgerEntries: jumla = 1, bila_userId = 1`
* **Baada:** `1 entries, 0 bila UserId`

### 🟠 H-2 — `RecordContributionDto.MemberId` → `UserId`

Jina lilikuwa linapotosha (thamani ilikuwa inatumika kama `userId`). Frontend bado haitumii endpoint hii, kwa hiyo hakuna kilichovunjika.

### 🟠 H-3 — Majaribio yameanzishwa

`ChamaLink.Tests` (xUnit + `WebApplicationFactory`), **majaribio 14**:

* `ServiceRegistrationTests` — hujenga **API halisi** na kujaribu kuunda kila controller
  (ndio ulinzi dhidi ya C-1/C-2 zisirudie)
* `ExceptionMappingTests` — ramani ya exception → HTTP status
* Ulinzi wa usalama: API inakataa key iliyovuja; endpoint bila token → 401

`[assembly: CollectionBehavior(DisableTestParallelization = true)]` imewekwa kwa sababu
majaribio mawili yanatumia `Environment.SetEnvironmentVariable` (hali ya jumla kwa process).

### 🟠 H-4 — 401 → 403 kwa "huna ruhusa"

`GlobalExceptionMiddleware` sasa inapeleka `UnauthorizedAccessException` → **403**.
Hii ni muhimu kwa sababu `chamalink-web-new/src/api/client.ts` humrudisha mtumiaji
kwenye login kwa 401 yoyote.

* **Uthibitisho:** Member anajaribu kuongeza mwanachama → **403**; bila token → **401**

### 🟠 H-5 — N+1 query kwenye `GetEventReport`

Matukio 50 yalikuwa = queries 51. Sasa ni **queries 2** bila kujali idadi ya matukio
(`ToLookup` ndani ya memory).

### 🟠 M-7 + M-8 — Mfumo wa exceptions maalum

* `ChamaLink.Domain/Exceptions/AppExceptions.cs` mpya: `AppException`,
  `ValidationException` (400), `NotFoundException` (404), `ForbiddenException` (403),
  `ConflictException` (409)
* **`throw new Exception` 30** kwenye services 8 zimebadilishwa kuwa aina maalum
* **`catch` blocks 71** zimeondolewa kwenye controllers 10
* `InvalidOperationException` **sasa ni 500** (si 400) — hii ndiyo exception ya .NET DI,
  na kuionyeshwa kama 400 ndiko kulikuficha C-1/C-2
* Jibu lina `message` **na** `detail` (faili 7 za frontend zinasoma `message`)
* **Matokeo:** sasa kosa la server linaonekana kama 500, kosa la mtumiaji kama 400

### 🟠 M-10 — Ukomo wa ukubwa wa faili

`[RequestSizeLimit(10 MB)]` + ukaguzi wa `file.Length` kwenye MkobaImport na
TreasuryImport (preview + commit).

### 🟠 M-11 + M-12 — Queries za LedgerService

* `GetGroupSummaryAsync`: queries 3 → **1** (SQL aggregation)
* `GetMemberStatementAsync`: ilikuwa inabeba LedgerEntries **zote** kwenye RAM kisha
  `Sum` ndani ya C#; sasa PostgreSQL inahesabu

### 🟠 M-1 — Rate limiting nyuma ya proxy

`UseForwardedHeaders` (production pekee) ili `RemoteIpAddress` iwe IP halisi ya mteja,
si ya proxy. `KnownNetworks/KnownProxies` zimefutwa kwa sababu kumwacha mtu yeyote
atume `X-Forwarded-For` ni njia ya kuepuka rate limit.

> **TODO yako:** ukiwa nyuma ya Azure/AppGateway/K8s, ongeza IP za proxy zako kwenye
> `KnownProxies` (nimeacha alama ya `TODO` kwenye Program.cs).

### 🟠 M-14 — Paketi isiyo sahihi

`Microsoft.Extensions.Hosting` **10.0.11** (ya .NET 10!) kwenye project ya **net8.0**
imebadilishwa kuwa `Microsoft.Extensions.Hosting.Abstractions` **8.0.1**.

### 🟠 M-2/M-3/M-9 — Ushimamizi

* `.gitignore` mpya ya root (.NET + Node)
* Faili 246 za `bin/obj` + `.env` zimeondolewa kwenye git index
* `README.md` kamili: usanidi, kujenga, kupima, kanuni za makosa, usalama

### 🟠 M-5 (sehemu) — JWT

Muda wa token sasa unasomwa kutoka `Jwt:ExpiryDays` (chaguo-msingi: **siku 1**, zamani siku 7).

### 🟠 M-4/M-6 (sehemu) — Auth

Kizuizi cha login: majaribio 5 yasiyofanikiwa kwa email moja → akaunti imefungwa dakika 15.
(Kiko kwenye RAM — hakishirikishwi kati ya instances nyingi.)

### 🟢 Ziada: makosa ya kusoma faili

`TreasuryExcelParserService` na `MkobaParserService` sasa zinatoa `ValidationException`
badala ya kuacha `HeaderException`/PdfPig exception iangukie 500.

* **Uthibitisho:** faili baya → **400** *"Faili hili si Excel halisi..."* / *"Faili hili si PDF halisi..."*

---

## Kilichobaki (hakijafanywa)

| Kipaumbele | Kazi |
|---|---|
| 🔴 Haraka | **Purge git history** (`git filter-repo`) au repo mpya — siri bado iko kwenye history |
| 🔴 Haraka | Badilisha neno la siri la PostgreSQL ya production (iliyoingia git) |
| 🟠 | Refresh tokens + njia ya kufuta token (revocation) |
| 🟠 | Uthibitisho wa barua pepe (kumaliza user enumeration) |
| 🟠 | Kizuizi cha login kihamishiwe Redis/DB (si RAM) |
| 🟠 | `KnownProxies` za proxy zako halisi |
| 🟡 | CI (GitHub Actions): build + test kwenye kila PR |
| 🟡 | Dockerfile + docker-compose (API + Postgres) |
| 🟡 | Code-splitting ya frontend (bundle 549 kB → chini ya 200 kB) |
| 🟡 | Concurrency token kwenye `Account` (kama `Loan`/`Withdrawal` zina `xmin`) |
| 🟡 | `GetGroupSummaryAsync` bado haihesabu marejesho ya mikopo — tumia `AnalyticsService.GetGroupBalanceAsync` badala yake |

---

## Jinsi ya kuendesha sasa

```bash
cd ChamaLink.API
cp appsettings.Development.local.json.example appsettings.Development.local.json
# weka key halisi:  openssl rand -base64 64

dotnet ef database update --project ../ChamaLink.Infrastructure --startup-project .
dotnet run
```

Kila mara kabla ya ku-push:
```bash
dotnet build ChamaLink.sln -c Release
dotnet test ChamaLink.Tests/ChamaLink.Tests.csproj
```

---

# Marekebisho ya 2026-09-16 — Bug ya counter za michango

## Tatizo (liligunduliwa baada ya kupitia Principal Audit ya nje)

`GroupMember` ina safu mbili zilizohifadhiwa (denormalized):

| Safu | Inatumika wapi |
|---|---|
| `TotalContributionsCount` | Ripoti ya WhatsApp: `"• Michango: Mara N"` (`ReportsController.cs:124`) |
| `AdvanceBalance` | `group-members-summary` |

Mfumo una **njia tatu** zinazoandika mchango, na zilikuwa hazifanyi kazi kwa namna moja:

| Njia | `TotalContributionsCount` | `AdvanceBalance` |
|---|---|---|
| `TreasuryImportService` (Excel ya mtunza-hazina) | ✅ imebadilishwa | ✅ imebadilishwa |
| `MkobaImportController` (M-Koba PDF) | ✅ imebadilishwa | ✅ imebadilishwa |
| `LedgerService` (mchango wa mkono) | ❌ **HAIKUBADILISHA** | ❌ **HAIKUBADILISHA** |

**Athari halisi:** mchango uliorekodiwa kwa mkono na Treasurer haukuonekana kwenye ripoti.
Mwanachama mwenye michango 12 (5 ya mikono, 7 ya M-Koba) alionyeshwa **"Michango: Mara 7"**.
Hii si hatari ya baadaye — ilikuwa inatokea.

## Suluhisho

Badala ya kuongeza counter kwenye kila njia (ambayo inaruhusu njia ya nne kuongezwa
baadaye na kusahau tena), counter sasa **zinahesabiwa kutoka kwenye ledger** — chanzo
cha ukweli wa mfumo.

**Faili mpya:** `ChamaLink.Infrastructure/Services/MemberCounterSyncService.cs`
- `ReconcileMemberAsync(groupMemberId)` — mwanachama mmoja
- `ReconcileGroupAsync(groupId)` — kikundi kizima (query 2 tu, siyo N+1)

**Faili zilizobadilishwa:**
- `LedgerService.cs` — inaita `ReconcileMemberAsync` baada ya kurekodi mchango
- `Program.cs` — `AddScoped<MemberCounterSyncService>()`
- `LedgerController.cs` — endpoint mpya `POST /api/Ledger/reconcile-counters/{groupId}`
  kwa kurekebisha data ya zamani iliyovurugika (idempotent, viongozi tu)

### Kwa nini siyo `[NotMapped] computed property`?

Ukaguzi wa nje ulipendekeza hivyo. EF Core haiwezi kutafsiri `=> LedgerEntries.Count()`
kwenye safu iliyohifadhiwa bila migration kubwa na kubadilisha kila mahali panaposomwa.
Njia hii inatoa matokeo sawa (hakuna drift) kwa hatari ndogo zaidi.

## Uthibitisho (umeendeshwa, siyo kukadiriwa)

Mfumo halisi umewashwa: PostgreSQL 17 + API kwenye `:5168`.

| Jaribio | Kabla | Baada |
|---|---|---|
| Michango 3 ya mikono (TZS 10,000 kila moja) | `Count=0 Advance=0` | **`Count=3 Advance=30000.00`** |
| Ripoti ya WhatsApp | `"Michango: Mara 0"` | **`"Michango: Mara 3"`** |
| Counter zilizoharibiwa (99 / 123.45) → `reconcile-counters` | — | **`Count=3 Advance=30000.00`**, drift iliripotiwa |
| Kuita `reconcile-counters` mara ya pili | — | **"hakuna kilichorekebishwa"** (idempotent) |

```
dotnet build ChamaLink.sln -c Release   →  0 Warning(s), 0 Error(s)
dotnet test                              →  Passed! Failed: 0, Passed: 19, Total: 19
```

Majaribio 5 mapya yameongezwa (`ChamaLink.Tests/MemberCounterTests.cs`):
- `RecordContribution_UpdatesBothCounters` ← hii ndiyo bug yenyewe
- `Reconcile_RecomputesFromLedger_NotFromStoredValues`
- `Reconcile_IsIdempotent`
- `Reconcile_MemberWithoutSavingsAccount_GetsZeroes`
- `Reconcile_OnlyCountsContributions_NotOtherTransactionTypes`

## Vitu viwili vipya vilivyogunduliwa wakati wa kuthibitisha

1. **Enum hazisomwi kama string kwenye JSON.** Hakuna `JsonStringEnumConverter`
   kwenye `Program.cs`, kwa hiyo `POST /api/Group/create` inahitaji `"type": 0`
   (namba) — lakini database inahifadhi enum kama **string** (`HasConversion<string>()`).
   Kutolingana huku kwa API/DB kunaweza kuchanganya mtumiaji wa API.

2. **Njia ya ripoti ni path parameter, siyo query string.**
   Sahihi: `GET /api/Reports/whatsapp-summary/{groupId}` (siyo `?groupId=`).
   Query string inarudisha 404 kimya kimya.

---

# Marekebisho ya 2026-09-16 (sehemu 2) — "Silent fallthrough"

## Kanuni

Mfumo unapaswa **KUKATAA kwa ujumbe wazi** badala ya kunyamaza. Kipengele ambacho
mtumiaji anakichagua kwenye settings, mfumo unakubali, kisha unafanya kitu kingine
bila kusema chochote — ni hatari zaidi kuliko kosa linaloonekana, kwa sababu
mtunza-hazina anaamini namba zinazoonekana kwenye skrini.

## 1. `LoanInterestType.Reducing` — ilikuwa inatoza Flat kimya kimya

**Uthibitisho:**
```
grep -rn "InterestType" ChamaLink.{Infrastructure,Application,API}
  → ApplicationDbContext.cs:134-135 pekee  (ramani ya column, siyo biashara)
```
`GroupService` inaruhusu mtunza-hazina kuchagua `Reducing`, inahifadhiwa kwenye DB,
lakini `LoanService.cs:77` daima ilikokotoa `PrincipalAmount * rate / 100` = **Flat**.

**Sasa:** `LoanService.IssueLoanAsync` inakataa kwa `ValidationException` ikiwa
kikundi kimechagua `Reducing`, na inaeleza nini cha kufanya.

**Kwa nini kukataa badala ya kukokotoa?** Riba ya reducing balance inahitaji ratiba
ya marejesho (amortization schedule) — siyo kubadilisha fomula tu. Kuikokotoa vibaya
ni mbaya zaidi kuliko kukataa.

**Uthibitisho kwenye mfumo halisi:**
| InterestType | Kabla | Baada |
|---|---|---|
| `Flat` (chaguo-msingi) | 200 ✅ | 200 ✅ (haikubadilika) |
| `Reducing` | **200 — riba ya Flat kimya kimya** | **400 + ujumbe wazi** |

## 2. `DebtAllocationStrategy` — ukaguzi wa nje ulikosea hapa

Ukaguzi (Principal Audit, K#5) ulisema:
> "ManualAllocation silently falls through to CurrentMonthFirst"

**Hiyo si kweli.** Hali halisi ilikuwa **mbaya zaidi**:

| | Thamani |
|---|---|
| `GroupSettingsModules.cs:57` (chaguo-msingi) | `CurrentMonthFirst` |
| `DebtService.ClearWithPaymentAsync` | `OrderBy(d => d.Period)` = zamani kwanza = **`OldestDebtFirst`** |

Yaani **kila kikundi kwa chaguo-msingi** kilikuwa kinapata mgawanyo wa
`OldestDebtFirst` — kinyume kabisa na settings zake. `grep` ya
`DebtAllocationStrategy` katika code ya biashara ilirudisha kuhifadhi/kusoma
settings pekee; **hakuna iliyoiTEKELEZA**.

**Sasa:** `ClearWithPaymentAsync` inapokea `groupId` (hiari, kwa utangamano wa
nyuma) na inakataa mikakati isiyotekelezwa kabla ya kusonga pesa.

**Kwa nini si kutekeleza sasa hivi?** Mpangilio wa mgawanyo hauko ndani ya method
hii pekee — upo kwenye waterfall ya `MkobaImportController` (STEP 1 faini → 2 tukio
→ 3 mchango wa mwezi → 3.4 joining fee → 3.5 madeni → 4 ziada). `OldestDebtFirst`
ingehitaji kubadilisha mpangilio huo mzima; `ManualAllocation` inahitaji UI.

## Faili zilizobadilishwa

- `LoanService.cs` — kataa `Reducing`
- `DebtService.cs` — kataa mikakati isiyotekelezwa
- `MkobaImportController.cs` — pitisha `groupId`
- `ChamaLink.Tests/SilentFallthroughTests.cs` — **jaribio jipya** (6)

## Uthibitisho

```
dotnet build ChamaLink.sln -c Release  →  0 Warning(s), 0 Error(s)
dotnet test                            →  Passed! Failed: 0, Passed: 25, Total: 25
```

Majaribio 6 mapya:
- `IssueLoan_WithReducingInterestType_IsRejected_NotSilentlyFlat`
- `IssueLoan_WithFlatInterestType_StillWorks` ← hulinda dhidi ya kuzuia sana
- `ClearWithPayment_WithManualAllocation_IsRejected`
- `ClearWithPayment_WithOldestDebtFirst_IsRejected`
- `ClearWithPayment_WithCurrentMonthFirst_ClearsDebt`
- `ClearWithPayment_WithoutGroupId_StillWorks_BackwardsCompatible`

## Jumla ya majaribio sasa: 25 (14 → 19 → 25)

---

# MAREKEBISHO YA 2026-09-18: Sheria za Ukonga Wing

Viongozi wa Ukonga walitoa jedwali lao la Excel la mtunza-hazina na
taarifa ya M-Koba, pamoja na sheria halisi za kikundi:

1. Mchango wa kila mwezi = **10,000**
2. Seli ya **15,000** = 10,000 mchango + **5,000 faini** ya kuchelewa
3. **KIANZIO** = 50,000 (mara moja); malipo ya awamu hufuatiliwa,
   kilichobaki = **deni** hadi kikamilike
4. Ziada yoyote (mf. 12,000) inaenda: **KIANZIO isiyokamilika → madeni
   ya nyuma → AKIBA** (katika mpangilio huo)
5. Alama ya **\*** = hajachangia mwezi huo
6. **NON ACTIVE** = miezi 3 mfululizo bila kuchangia (anabaki kwenye
   rekodi)
7. Mfumo lazima **uonyeshe** mgawanyo wa kila mwanachama × kila mwezi
   kama Excel yao ("tunapata taarifa zisizo na uhakika")

## Ukaguzi uliogundua

| Sheria | M-Koba import | Excel import (kabla) |
|---|---|---|
| 15,000 = mchango + faini | ✅ | ✅ |
| KIANZIO inaagizwa | — | ✅ |
| Ziada → KIANZIO → deni → akiba | ✅ (STEP 3.4→3.5→4) | ❌ **ziada yote → akiba** |
| Ripoti ya mwezi kwa mwezi | ❌ haikuwepo | ❌ haikuwepo |

## 1. MGAWANYO MPYA WA ZIADA (TreasuryImportService)

**Bug**: `TreasuryImportService.cs` ilikuwa inapeleka ziada YOTE ya
seli kwenye akiba (comment yake yenyewe ilisema "12,000 = 10,000 +
2,000 akiba") — kinyume na makubaliano ya kikundi.

**Fix**: Ziada sasa inafuata ng'ambo (cascade):
1. **(3a)** KIANZIO isiyolipwa → entry ya `JoiningFee` (refNo `…-KIANZIO-ZIADA`)
2. **(3b)** madeni ya michango ya nyuma → `DebtService.ClearWithPaymentAsync` (refNo `…-DENI`)
3. **(3c)** kilichobaki → akiba (refNo `…-AKIBA`)

Mambo muhimu ya kiufundi:
- **Running balance ya KIANZIO**: `SumAsync` haiwezi kuona entries
  zisizohifadhiwa bado, kwa hiyo pending inahesabiwa mara MOJA kabla ya
  miezi 12 na kupunguzwa kila inapotumika. Bila hili, mwanachama mwenye
  ziada miezi mingi angepewa KIANZIO kupita lengo (test ilikamata bug
  hii: `JoiningFeesPosted` ilikuwa 2 badala ya 1).
- Kiasi cha KIANZIO cha safu-wima (kitatumwa hatua ya 4) kinatolewa
  kwenye pending ili ziada isikijaze mara mbili.
- Preview (`GetPreviewAsync`) pia sasa inaonyesha mgawanyo halisi
  (`JoiningFeeApplied`/`DebtCleared`/`Savings`) kwa kutumia running
  balance ile ile. **Bug iliyopatikana na kurekebishwa**: preview
  ilikuwa inahoji ledger kwa `GroupMember.Id` badala ya `UserId`.
- DTOs mpya: `TreasuryPlannedSplitDto.JoiningFeeApplied`,
  `.DebtCleared`; `TreasuryImportResultDto.DebtClearancesPosted`,
  `.TotalDebtClearedAmount`.
- Import bado ni salama kurudia (refNo guard kwa `-KIANZIO-ZIADA`,
  `-DENI`, `-AKIBA`).

## 2. RIPOTI MPYA: "Mwezi kwa Mwezi"

Endpoint mpya: `GET /api/Reports/monthly-matrix/{groupId}?year=2026`

- `ComplianceReportService.GetMonthlyMatrixAsync` — query MOJA ya
  LedgerEntries kwa kikundi+mwaka, grouping kwenye RAM.
- Kila seli: mchango + faini; `Missed` (hakuna mchango na alikuwa
  mwanachama); `HadFine`; mgawanyo wa ziada (KIANZIO/deni/akiba).
- KIANZIO: jumla ya miaka YOTE vs lengo → deni lililobaki.
- MATUKIO: `EventContribution` + `WelfareTopUp`.
- AKIBA ILIYOPO: michango+kianzio+faini − mikopo − ustawi − share-out.
- Tab mpya ya frontend "Mwezi kwa Mwezi" kwenye Ripoti: kadi 4 za
  muhtasari, jedwali linaloiga Excel (S/N, JINA, miezi, KIANZIO,
  MATUKIO, JUMLA), `*` nyekundu, beji ya `F` (faini), alama za `K`/`D`
  (ziada ilienda KIANZIO/deni), beji ya NON ACTIVE, safu ya JUMLA, na
  tooltip ya mgawanyo kamili kwa kila seli.

## Files zilizoguswa

- `ChamaLink.Infrastructure/Services/TreasuryImportService.cs` — cascade 3a/3b/3c + preview
- `ChamaLink.Infrastructure/Services/ComplianceReportService.cs` — `GetMonthlyMatrixAsync`
- `ChamaLink.Application/DTOs/TreasuryImportDtos.cs` — fields mpya za split/result
- `ChamaLink.Application/DTOs/MonthlyMatrixDtos.cs` — **mpya**
- `ChamaLink.API/Controllers/ReportsController.cs` — endpoint mpya
- `chamalink-web-new/src/api/reports.ts` — `getMonthlyMatrix` + types
- `chamalink-web-new/src/pages/ReportsPage.tsx` — tab "Mwezi kwa Mwezi"
- `ChamaLink.Tests/UkongaImportTests.cs` — **jaribio jipya** (7)

## Uthibitisho

```
dotnet build ChamaLink.sln -c Release  →  0 Warning(s), 0 Error(s)
dotnet test                            →  Passed! Failed: 0, Passed: 32, Total: 32
npm run build                          →  ✓ built (976.87 kB, gzip 285.78)
```

Majaribio 7 mapya (UkongaImportTests):
- `Excess_GoesToJoiningFee_First_NotSavings` ← hulinda bug kuu
- `Excess_Cascades_JoiningFee_ThenDebt_ThenSavings`
- `Excess_GoesToSavings_Only_When_JoiningFeeDone_NoDebt`
- `LateFine_RemainsFine_NotRoutedToCascade`
- `Rerun_SkipsDuplicates_NoDoubleEntries`
- `MonthlyMatrix_ShowsCells_Missed_Fines_JoiningFeeDebt`
- `Preview_PlannedSplit_Shows_JoiningFeeAllocation`

## Jumla ya majaribio sasa: 32 (25 → 32)

---

# MAREKEBISHO YA 2026-09-19: Reconciliation Engine (Ulinganisho) — P0

Kwa mujibu wa mapendekezo ya ukaguzi wa bidhaa: kipimo cha mwisho cha
uaminifu wa hesabu ni **M-Koba PDF = Excel ya viongozi = Ledger ya
ChamaLink** kwa kila mwanachama × kila mwezi. Hii ndiyo kazi iliyokuwa
imebaki kabla ya kusema Ukonga Engine iko production-ready.

## Kilichojengwa

**Backend — `ReconciliationService`** (read-only, hakuna migration):
- Huparse taarifa ya M-Koba (PDF) kwa `IMKobaParserService` ile ile ya
  import, na Excel kwa `ITreasuryExcelParserService` ile ile — chanzo
  kimoja cha ukweli cha parsing.
- M-Koba matching: simu (0→255 normalization, last-9 fallback) kisha
  jina (MemberNameMatcher, exact only).
- Excel matching: jina (exact) — safu zisizolingana → maonyo.
- Ledger: Contribution + FinePayment + JoiningFee + EventContribution
  kwa kila mwezi.
- Uainishaji wa kila mstari:
  - **SAWA** (tofauti 0)
  - **NYARAKA HAZILINGANI** (PDF ≠ Excel; tofauti = faini inatambuliwa)
  - **MFUMO CHINI** (mchango haujaingizwa)
  - **MFUMO ZIADA** (mchango wa mkono / ziada / **import mara mbili** —
    ledger = PDF + Excel inatambuliwa na kuonywa wazi)
- Mstari maalum wa KIANZIO kwa kila mwanachama (Excel vs ledger; ziada
  ya waterfall inaelezwa, sio kosa).
- Maonyo: miamala isiyolingana, withdrawals za M-Koba, MSIBA/SHEREHE.

**Endpoint:** `POST /api/Reports/reconciliation/{groupId}?year=2026`
(multipart: `mkobaFile`, `excelFile` — angalau moja; 15 MB kila moja).
READ-ONLY: haiandiki kitu kwenye DB.

**Frontend — tab "Ulinganisho"** kwenye Ripoti:
- Pachagua mwaka + faili mbili (M-Koba PDF, Excel) + kitufe cha kufanya
  ulinganisho.
- Kadi 4 za muhtasari (SAWA / NYARAKA HAZILINGANI / MFUMO CHINI /
  MFUMO ZIADA), jedwali kamili na rangi kwa hali, safu ya JUMLA,
  maonyo, na maelezo ya sababu kwa kila mstari.

**Nyaraka:** `BUSINESS_RULES_UKONGA.md` imesasishwa — Sheria 9
(Ulinganisho) + orodha mpya ya kazi iliyobaki.

## Files zilizoguswa

- `ChamaLink.Application/DTOs/ReconciliationDtos.cs` — **mpya**
- `ChamaLink.Infrastructure/Services/ReconciliationService.cs` — **mpya**
- `ChamaLink.API/Controllers/ReportsController.cs` — endpoint mpya + DI
- `ChamaLink.API/Program.cs` — usajili wa `ReconciliationService`
- `chamalink-web-new/src/api/reports.ts` — `postReconciliation` + types
- `chamalink-web-new/src/pages/ReportsPage.tsx` — tab "Ulinganisho"
- `ChamaLink.Tests/ReconciliationTests.cs` — **jaribio jipya** (7)
- `BUSINESS_RULES_UKONGA.md` — Sheria 9 + kazi iliyobaki

## Uthibitisho

```
dotnet build ChamaLink.sln -c Release  →  0 Warning(s), 0 Error(s)
dotnet test                            →  Passed! Failed: 0, Passed: 39, Total: 39
npm run build                          →  ✓ built (985.53 kB, gzip 287.52)
```

Majaribio 7 mapya (ReconciliationTests):
- `AllSourcesAgree_StatusSawa`
- `PdfVsExcel_DifferByFine_NyarakaHazilingani`
- `MissingFromLedger_MfumoChini`
- `DoubleImport_MfumoZiada_WithDuplicateHint`
- `JoiningFeeRow_WaterfallExcessExplained`
- `UnmatchedMkobaTransaction_ProducesWarning`
- `ExcelOnly_ComparesAgainstLedger`

## Jumla ya majaribio sasa: 39 (32 → 39)

---

# MAREKEBISHO YA 2026-09-19 (sehemu 2): AWAMU YA 1 — Marejesho ya Mikopo kwenye Import + Statement ya Mwezi

Mpango wa awamu 4 uliokubaliwa na mwenye kikundi (SEHEMU 3 ya
ushauri). Awamu hii inatatua **chanzo kikuu cha "taarifa zisizo za
kweli"**: pesa ya rejesho la mkopo iliyokuwa inapotea kwenye akiba.

## Mizizi 5 ya taarifa za uongo (iliyothibitishwa kwa code)

1. **Hakuna hatua ya mkopo kwenye waterfall yoyote** — pesa ya rejesho
   ilienda KIANZIO/akiba; mkopo unabaki "Active" milele.
2. **Lengo = mchango tu** — wajibu wa rejesho haukuwepo kwenye ripoti.
3. **Hatari ya kuhesabu mara mbili** — rejesho la mkono + import.
4. **Data ya demo ya zamani** (kabla ya 2026-09-16/18) — Awamu 2.
5. **`UpsertSnapshotAsync` haikuitwa KAMWE** — jedwali la
   ComplianceSnapshots lilikuwa tupu milele; ripoti zote za
   Uzingatiaji (sehemu 5/7/8) zilisoma jedwali tupu.

## Kilichofanywa

### 1. LoanSchedule rahisi (`LoanService`)

- `GetExpectedRepaymentForMonth(loan, year, month)` — installment =
  TotalPayable / miezi (inclusive); mwezi wa mwisho = salio lote;
  nje ya kipindi / Repaid = 0. Flat interest → installment sawa.
- `GetExpectedRepaymentsTotal(loans, year, month)` — jumla kwa mikopo yote.
- `ApplyRepaymentFromImportAsync(...)` — FIFO (mkopo mzee kwanza),
  kila mkopo hupata INSTALLMENT YA MWEZI TU (malipo ya mapema yanabaki
  mkono), LedgerEntry ya LoanRepayment yenye `-REJESHO-` kwenye
  ReferenceNo, mkopo ukikamilika → Status=Repaid + RepaidAt.
  HAIFANYI SaveChanges (caller ndiye anayefanya).

### 2. Waterfall mpya (M-Koba NA Excel)

Mpangilio uliokubaliwa: **mchango → REJESHO → KIANZIO → madeni ya
nyuma → akiba**. Rejesho ni wajibu wa mwezi (sehemu ya Lengo), hivyo
linapewa kipaumbele kuliko matumizi ya hiari.

- **M-Koba** (`MkobaImportController` STEP 3.3): baada ya Monthly
  Savings, kabla ya JoiningFee (STEP 3.4).
- **Excel** (`TreasuryImportService`): hatua (3-rejesho) kabla ya
  (3a) KIANZIO; preview inaonyesha `RepaymentApplied` kwa kila mwezi.

**Ulinzi dhidi ya marudio** (mzizi #3):
- Ikiwa mwezi huu una entry ya `-REJESHO-` (import iliyopita) → ruka.
- Ikiwa mwezi huu una rejesho la MKONO (lisiyo na alama) → ruka NA
  toa onyo (pesa moja isihesabiwe mara mbili).
- Excel: `-REJESHO-` imeingizwa kwenye guard ya `alreadyProcessed`.

### 3. ComplianceSnapshot + wiring fix (mzizi #5)

- Fields mpya: `ExpectedLoanRepayment`, `PaidLoanRepayment`.
- Migration: `AddLoanRepaymentToSnapshot`.
- **BUG FIX KUBWA**: `ContributionComplianceBackgroundService` sasa
  INAITA `UpsertSnapshotAsync` kwa kila mwanachama (zamani haikuitwa
  kamwe — snapshots zilikuwa tupu milele). Kila mwezi: Lengo,
  kilicholipwa, consecutive missed months, wajibu wa rejesho, na
  rejesho lililolipwa.

### 4. Statement ya Mwezi (ripoti mpya)

`MemberStatementService` — inahesabu LIVE kutoka ledger+mikopo+madeni
(haitegemei snapshots; mtunza-hazina anapata usahihi mara tu baada ya
import).

Safu: **NAMBARI | JINA | LENGO | AMETOA | UPUNGUFU | FAINI | REJESHO |
AKIBA | DENI BAKI | HALI**

- LENGO = mchango unaotarajiwa + rejesho linalostahili mwezi huo.
- HALI: Lengo=0 → "—"; paid ≥ Lengo → **Amelipa**; 0 < paid < Lengo →
  **Amelipa Sehemu**; 0 → **Hajalipa** (hali ni ya MWEZI huu tu).
- "Amelipa kupitia akiba" (UseSavingsThenFine) itakuja Awamu 4.

**Endpoint:** `GET /api/Reports/member-statement/{groupId}?year=&month=`

**Frontend:** tab mpya **"Statement ya Mwezi"** kwenye Ripoti —
vichujio vya mwezi/mwaka, kadi 4, muhtasari wa hesabu (Amelipa/Sehemu/
Hajalipa), jedwali kamili na safu ya JUMLA.

## Files zilizoguswa

- `ChamaLink.Infrastructure/Services/LoanService.cs` — schedule + import repayment
- `ChamaLink.Domain/Entities/ComplianceSnapshot.cs` — fields 2 mpya
- `ChamaLink.Infrastructure/Services/ComplianceSnapshotService.cs` — params mpya
- `ChamaLink.Infrastructure/Services/ContributionComplianceBackgroundService.cs` — wiring fix
- `ChamaLink.API/Controllers/MkobaImportController.cs` — STEP 3.3
- `ChamaLink.Infrastructure/Services/TreasuryImportService.cs` — hatua ya rejesho + preview
- `ChamaLink.Application/DTOs/MkobaImportResultDto.cs` — `TotalRepaymentsApplied`
- `ChamaLink.Application/DTOs/TreasuryImportDtos.cs` — counters + `RepaymentApplied`
- `ChamaLink.Application/DTOs/MemberStatementDtos.cs` — **mpya**
- `ChamaLink.Infrastructure/Services/MemberStatementService.cs` — **mpya**
- `ChamaLink.API/Controllers/ReportsController.cs` — endpoint mpya
- `ChamaLink.API/Program.cs` — usajili wa `MemberStatementService`
- `ChamaLink.Infrastructure/Migrations/*AddLoanRepaymentToSnapshot*` — **mpya**
- `chamalink-web-new/src/api/reports.ts` — `getMemberStatement` + types
- `chamalink-web-new/src/pages/ReportsPage.tsx` — tab "Statement ya Mwezi"
- `ChamaLink.Tests/Awamu1LoanRepaymentTests.cs` — **jaribio jipya** (10)

## Uthibitisho

```
dotnet build ChamaLink.sln -c Release  →  Build succeeded (0 errors)
dotnet test                            →  Passed! Failed: 0, Passed: 49, Total: 49
npm run build                          →  ✓ built (993.17 kB, gzip 288.81)
dotnet ef migrations add ...           →  Done.
```

Majaribio 10 mapya (Awamu1LoanRepaymentTests):
- `Installment_IsUniform_OverLoanPeriod`
- `FinalMonth_TakesRemainingBalance_NotRegularInstallment`
- `OutsideLoanWindow_ReturnsZero`
- `RepaidOrZeroBalance_ReturnsZero`
- `ApplyRepayment_OldestLoanFirst_AndCappedAtInstallment`
- `ApplyRepayment_CompletesLoan_SetsRepaidStatus`
- `ApplyRepayment_NoActiveLoans_ReturnsZero`
- `MemberStatement_LengoIncludesRepayment_AndStatusCorrect`
- `MemberStatement_PartialPayment_ShowsSehemu`
- `MemberStatement_NoPayment_ShowsHajalipa`

## Jumla ya majaribio sasa: 49 (39 → 49)

## Kilichobaki (kwa mpango)

- **Awamu 2**: data repair ya Ukonga (re-import/cleanup ya data ya
  bug-era + counter resync) — BAADA ya sheria kutulia.
- **Awamu 3**: loan engine kamili (workflow ya maombi, wadhamini,
  interest/principal split, PAR metrics).
- **Awamu 4**: platform breadth (ShortfallStrategy enum, welfare
  drill-down, hybrid groups, "Amelipa kupitia akiba").

---

# MAREKEBISHO YA 2026-09-19 (sehemu 3): AWAMU YA 2 — Data Repair ya Ukonga (bug-era) + Frontend

## Sababu

Baada ya Awamu 1, sheria zimetulia (waterfall: mchango → REJESHO → KIANZIO → deni → akiba).
Lakini data iliyoingizwa **kabla ya 2026-09-19** bado ina:

1. **Counter drift** — `TotalContributionsCount`/`AdvanceBalance` hazikusasishwa na `LedgerService`
   (mchango wa mkono) → GroupMember ina namba za zamani, WhatsApp summary inasema "Mara N" ndogo.
2. **Snapshots tupu** — `UpsertSnapshotAsync` haikuitwa KAMWE kabla ya fix ya root cause #5,
   hivyo `ComplianceSnapshots` lilikuwa tupu milele na ripoti za Uzingatiaji zilisoma tupu.
3. **Loan repayment gaps** — import haikuwa na hatua ya rejesho, hivyo pesa iliyopaswa kwenda
   mikopo ilienda akiba/KIANZIO kimakosa — mkopo unabaki Active na akiba inavimbishwa.

Awamu 2 inampa mtunza-hazina uwezo wa **kuona kwanza (READ-ONLY preview), kisha kurekebisha**.

## Design (kuhifadhi data, sio kuiharibu)

- **HAIHARIBU ledger** — inasoma tu na kurekebisha counters + snapshots.
- **Preview ni READ-ONLY** — hakuna SaveChanges, counters hazibadiliki.
- **Repair ni idempotent** — ukiirudia, inarudi 0 corrected.
- **Loan gaps ni ripoti TU** — hatuhamishi pesa moja kwa moja (uamuzi wa uongozi, si wa mfumo).
  Ikiwa unataka kuhamisha akiba → rejesho, fanya kupitia LoanController (RecordRepayment).

## Backend

### DTOs mpya — `ChamaLink.Application/DTOs/DataRepairDtos.cs`
- `CounterDriftRowDto` (old/new count/balance + hasDrift)
- `MissingSnapshotRowDto` (month, expected/actual/missing)
- `LoanRepaymentGapRowDto` (member, month, expected/paid/savings, note)
- `DataRepairPreviewDto` (groupId, membersChecked, drifts, missing, gaps, warnings, hasIssues)
- `DataRepairResultDto` (action, membersChecked/corrected, snapshotsCreated/Updated, details)

### Service — `ChamaLink.Infrastructure/Services/DataRepairService.cs`
- `GetPreviewAsync(groupId, monthsBack=24)`:
  - Counter drift: compare GroupMember vs ledger (Savings account, Type=Contribution)
  - Missing snapshots: miezi tangu group.CreatedAt hadi leo, group by ComplianceSnapshots
  - Loan gaps: miezi 12 iliyopita, expected>0 & paid=0 & savings>0 → flag
- `RepairCountersAsync(groupId)`: calls `MemberCounterSyncService.ReconcileGroupAsync` + SaveChanges
- `RepairSnapshotsAsync(groupId, monthsBack)`: kwa kila mwezi, kwa kila member:
  paidThisMonth (Contribution), expectedContribution, consecutiveMissed (from Debts),
  expected/paid repayment (LoanService.GetExpected... + ledger), UpsertSnapshotAsync
- `FullRepairAsync`: counters + snapshots

### Controller — `ChamaLink.API/Controllers/DataRepairController.cs`
- `[Authorize]`, role check: Treasurer/Chairperson/Secretary
- `GET preview/{groupId}?months=24`
- `POST repair-counters/{groupId}`
- `POST repair-snapshots/{groupId}?months=24`
- `POST full-repair/{groupId}?months=24`

### DI — `ChamaLink.API/Program.cs`
- `AddScoped<DataRepairService>()`

## Frontend — Awamu 2 (hakuna kusahau!)

### API — `chamalink-web-new/src/api/dataRepair.ts`
- Interfaces: CounterDriftRow, MissingSnapshotRow, LoanRepaymentGapRow, DataRepairPreview, DataRepairResult
- Functions: getRepairPreview, repairCounters, repairSnapshots, fullRepair

### Page — `chamalink-web-new/src/pages/DataRepairPage.tsx`
- Header: maelezo ya bug-era + READ-ONLY kwanza
- Controls: months selector (6/12/24/36), Chunguza (Preview), Rekebisha Counters, Jenga Snapshots, Ukarabati Kamili
- Warnings, Summary cards (wanachama, snapshots pungufu, mapengo ya rejesho)
- Tables:
  - Counter Drift (member, old→new count/balance)
  - Snapshots pungufu (month, expected/actual/missing)
  - Mapengo ya Rejesho (member, month, expected/paid/akiba) — ripoti tu + note "hatuhamishi pesa"
- Result banner (membersChecked/corrected, snapshotsCreated/Updated, details)
- Uses `useMyGroups` (groupId field, si id)

### Navigation
- `App.tsx`: route `/matengenezo` → `DataRepairPage`
- `DashboardPage.tsx`: NavLink "Matengenezo" kwa leaders (isLeader)
- `SettingsPage.tsx`: banner ya dhahabu inayolink kwenda /matengenezo (Awamu 2)

### Builds
- `npm run build` → ✓ built (1,009.73 kB, gzip 291.56 kB) — 0 errors
- `dotnet build` → Build succeeded (3 warnings pre-existing)

## Tests — `ChamaLink.Tests/Awamu2DataRepairTests.cs` (4 mpya)

1. `Preview_IsReadOnly_DoesNotChangeCounters` — preview inaona drift (999→2) lakini DB haibadiliki
2. `RepairCounters_FixesDrift_AndIsIdempotent` — first repair 1 corrected, second 0
3. `RepairSnapshots_CreatesMissingSnapshots` — jedwali tupu → snapshotsCreated>0, second run 0 created
4. `Preview_DetectsLoanRepaymentGaps` — akiba iliingia bila rejesho → gap flagged

Jumla: **53 tests (49 → 53), zote green**.

## Verification (re-run)

```
dotnet build ChamaLink.sln -c Release → Build succeeded
dotnet test → Passed! 53/53
npm run build → ✓ built (1,009.73 kB gzip 291.56)
```

## Kilichobaki (kwa mpango)

- **Awamu 3**: loan engine kamili (workflow ya maombi, wadhamini, interest/principal split, PAR metrics)
- **Awamu 4**: platform breadth (ShortfallStrategy enum, welfare drill-down, hybrid groups, "Amelipa kupitia akiba")

