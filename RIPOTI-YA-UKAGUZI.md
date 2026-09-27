# Ripoti ya Ukaguzi — ChamaLink Backend + Frontend

**Tarehe ya ukaguzi:** 2026-09-15
**Repo:** https://github.com/GabrielM192/ChamaLinkBackend-fixed-3-.git
**Commit iliyokaguliwa:** `771aceb` (main)
**Mtandao:** .NET 8 (Clean Architecture: Domain / Application / Infrastructure / API) + PostgreSQL + React 19 / Vite / TypeScript

---

## 0. Nilichofanya (si kukisia — vyote vimejaribiwa)

| Hatua | Amri | Matokeo |
|---|---|---|
| Build ya backend | `dotnet build ChamaLink.sln -c Release` | ✅ **0 Errors, 0 Warnings** (13.18s) |
| Migrations kwenye DB halisi | Nili-install PostgreSQL 17, `dotnet ef database update` | ✅ Migrations **23 zote zimepita**, `Done.` |
| Model vs migrations | `dotnet ef migrations has-pending-model-changes` | ✅ "No changes have been made to the model since the last migration." |
| Build ya frontend | `npm install && npm run build` (`tsc -b && vite build`) | ✅ 2359 modules, **0 errors** (1 onyo: chunk > 500 kB) |
| API halisi ikaanzishwa | `dotnet run` kwenye port 5168 | ✅ Ikaanza, ikajibu |
| Smoke test ya endpoints | register → login → group → add-member → contribution → reports | ❌ **Hapa ndipo makosa makubwa yalipoonekana** |
| Uthibitisho wa uvunjaji wa JWT | Nilitengeneza token yangu mwenyewe kwa key iliyo kwenye repo | ❌ **API iliiamini (HTTP 200)** |

**Ukubwa wa code:** faili 134 za `.cs` + 32 za `.ts/.tsx`, jumla **~34,054 mistari**.

---

## 1. Hitimisho kwa kifupi

Msingi wa mfumo **ni mzuri sana** — Clean Architecture imefuatwa vizuri, comments zinaeleza *kwa nini* kila uamuzi ulifanywa, na inaonekana tayari umepitia duru kadhaa za security audit (CORS, rate limiting, IDOR, concurrency tokens, unique indexes, global exception middleware — vyote vipo na vimefanywa vizuri).

**Lakini kuna tatizo moja linalorudia-rudia ambalo limevunja mfumo mzima:** *classes zinaandikwa, build inapita, lakini hazisajiliwi kwenye Dependency Injection* — na hili **halionekani kwa build wala kwa compiler**, linaonekana tu wakati wa kuendesha. Program.cs mwenyewe una comments 3 zinazokiri bug hii hiyo hiyo iliyotangulia (`AccountResolverService`, `IEventService`, `LoanService`) — lakini **haikurekebishwa kwa wote**.

Na kuna **tatizo moja la usalama la kiwango cha hatari** — siri za mfumo ziko wazi kwenye GitHub.

---

## 2. MATATIZO MAKUBWA (Critical)

### 🔴 C-1. `ReportsController` YOTE (endpoints 16) haifanyi kazi — HTTP 400

**Chanzo:** `ComplianceReportService` haijasajiliwa kwenye `Program.cs`, lakini `ReportsController` inaihitaji kwenye constructor yake (mistari 36 na 47).

**Ushahidi (kabla ya fix) — kila endpoint ilirudisha 400:**
```
GET /api/Reports/whatsapp-summary/{groupId}       -> 400
GET /api/Reports/group-members-summary/{groupId}  -> 400
GET /api/Reports/group-financial-summary/{groupId}-> 400
GET /api/Reports/collection-rate/{groupId}        -> 400
GET /api/Reports/defaulters/{groupId}             -> 400
GET /api/Reports/compliance-summary/{groupId}     -> 400
GET /api/Reports/group-balance/{groupId}          -> 400
GET /api/Reports/members/{groupId}                -> 400
GET /api/Reports/loan-portfolio/{groupId}         -> 400
GET /api/Reports/fines/{groupId}                  -> 400
GET /api/Reports/debts/{groupId}                  -> 400
GET /api/Reports/withdrawals/{groupId}            -> 400
GET /api/Reports/events/{groupId}                 -> 400
GET /api/Reports/member-registry/{groupId}        -> 400
GET /api/Reports/compliance-trend/{gid}/{gmid}    -> 400
GET /api/Reports/member-profile/{gid}/{uid}       -> 400
```
Ujumbe uliorudi:
> `Unable to resolve service for type 'ChamaLink.Infrastructure.Services.ComplianceReportService' while attempting to activate 'ChamaLink.API.Controllers.ReportsController'.`

**Athari:** Hii si endpoint moja — ni **moduli nzima ya Ripoti**. Frontend yako inaita endpoints 15 kati ya hizi (`chamalink-web-new/src/api/reports.ts`), ikijumuisha Dashboard, Member Profile, Collection Rate, Defaulters, Loan Portfolio, WhatsApp Summary. Yaani **ukurasa wa Reports na sehemu kubwa ya Dashboard hazifanyi kazi kabisa.**

**Dawa:** Ongeza mstari mmoja kwenye `Program.cs`:
```csharp
builder.Services.AddScoped<ComplianceReportService>();
```

---

### 🔴 C-2. `TreasuryImportController` (preview + commit) haifanyi kazi — HTTP 400

**Chanzo:** `TreasuryImportService` na `ITreasuryExcelParserService` hazijasajiliwa. `TreasuryImportController` inahitaji `TreasuryImportService`, nayo inahitaji `ITreasuryExcelParserService`.

**Ushahidi (kabla ya fix):**
```
POST /api/TreasuryImport/preview/{groupId} -> 400
"Unable to resolve service for type 'ChamaLink.Infrastructure.Services.TreasuryImportService'
 while attempting to activate 'ChamaLink.API.Controllers.TreasuryImportController'."
```

**Athari:** Ukurasa wa **Treasury** (`TreasuryPage.tsx` + `TreasuryUpload.tsx`) hauwezi kufanya kazi. Kazi yote ya `TreasuryExcelParserService.cs`, `TreasuryImportService.cs` na `MemberNameMatcher.cs` (faili 3, mamia ya mistari) **haifikwi kabisa**.

**Dawa:**
```csharp
builder.Services.AddScoped<ITreasuryExcelParserService, TreasuryExcelParserService>();
builder.Services.AddScoped<TreasuryImportService>();
```

---

### ✅ UTHIBITISHO: Dawa imefanya kazi

Niliongeza mistari 3 hiyo kwenye `Program.cs`, nikajenga upya (0 errors), nikaanzisha API upya, nikapima tena:

```
########## BAADA YA FIX: ReportsController ##########
  whatsapp-summary         -> HTTP 200     members                -> HTTP 200
  group-members-summary    -> HTTP 200     loan-portfolio         -> HTTP 200
  group-financial-summary  -> HTTP 200     fines                  -> HTTP 200
  collection-rate          -> HTTP 200     debts                  -> HTTP 200
  defaulters               -> HTTP 200     withdrawals            -> HTTP 200
  compliance-summary       -> HTTP 200     events                 -> HTTP 200
  group-balance            -> HTTP 200     member-registry        -> HTTP 200
  compliance-trend         -> HTTP 200     member-profile         -> HTTP 200
                                            (16/16 = 200 OK)
########## BAADA YA FIX: TreasuryImport ##########
  preview/{gid} -> HTTP 400 {"message":"Invalid file signature."}
  (sasa ni parser inayozungumza — si tena DI. Yaani service imefikika.)

Data halisi iliyorudi:
{"groupId":"e0e7c724-...","totalContributions":10000.00,"cashAvailable":10000.00,
 "outstandingLoans":0,"outstandingFines":0.0,"outstandingDebts":0.0,
 "totalWelfareHeld":0,"totalWithdrawalsPaid":0.0,"totalWalletWithdrawals":0.0,
 "totalAssets":10000.00}
```

Mabadiliko yameandikwa tayari kwenye `ChamaLink.API/Program.cs` ndani ya workspace yako.

---

### 🔴 C-3. Siri za mfumo ziko wazi kwenye GitHub (hatari kubwa zaidi)

**Nini kiko wazi:** `ChamaLink.API/appsettings.json`, ndani ya git tangu commit ya kwanza (`43b80e3`):

```json
"ConnectionStrings": {
  "DefaultConnection": "Host=localhost;Database=chamalink;Username=postgres;Password=sandbox123"
},
"Jwt": {
  "Key": "ChamaLinkSandboxSecretKey2024ForTestingOnlyMinimum32Bytes!!"
}
```

**Kwa nini hii ni hatari kubwa:** Mtu yeyote anayepata repo hii anaweza **kutengeneza JWT yake mwenyewe** na kuwa mtumiaji yeyote — Chairperson, Treasurer — bila kujua neno la siri la mtu yeyote.

**Ushahidi — nilifanya hivyo mimi mwenyewe:**
```
Nilitumia Python + HMAC-SHA256 kutengeneza token yenye:
  sub = 00000000-0000-0000-0000-000000000001  (mtumiaji asiyeopo kwenye database)
  iss = ChamaLinkAPI,  aud = ChamaLinkUsers

  GET /api/Group/my-groups -> HTTP 200      ← API ILIIKUBALI
  (control test: token yenye signature iliyoharibiwa -> HTTP 401, yaani ni key pekee inayofungua)
```

Hapa nilitumia sub isiyo halisi; mtu mwenye nia mbaya angechagua `sub` ya Chairperson halisi (au anaweza kujiandikisha kwanza, kisha kubadilisha role). Kila ulinzi wa `GroupAuthorizationService` unategemea tu kuwa JWT haiwezi kuundwa nje ya server — **ulinzi huo umevunjwa**.

**Dawa (kwa mpangilio):**
1. **Badilisha JWT key mara moja** — kama mfumo umeshawekwa mahali (production), chukulia tokens zote zilizotolewa kuwa zimevunjwa.
2. Ondoa siri kwenye `appsettings.json`; tumia **environment variables** au **User Secrets** (tayari una `UserSecretsId` kwenye `.csproj`):
   ```json
   "Jwt": { "Key": "" }
   ```
   ```bash
   dotnet user-secrets set "Jwt:Key" "key-mpya-ya-herufi-64-bila-kupatikana-kwahisi"
   # Production:
   export Jwt__Key="..."
   export ConnectionStrings__DefaultConnection="Host=...;Password=..."
   ```
3. Ongeza `builder.Configuration` validation ya kushindwa ikiwa key ni fupi au ni ile ya zamani.
4. **Purge git history** (`git filter-repo`), au anzisha repo mpya — siri iliyokwisha-ingia kwenye history haiondolewi kwa kuifuta faili tu.

---

## 3. MATATIZO YA KATI (High)

### 🟠 H-1. `LedgerEntry` inaandikwa **bila `UserId`** kutoka `LedgerService`

`LedgerService.RecordContributionAsync` (mstari 25–34) inaunda `LedgerEntry` bila kuweka `UserId`, ingawa uwanja huo upo (`Guid? UserId`) na migration `20260903122113_AddUserIdToLedgerEntry` iliongezwa kwa makusudi.

**Ushahidi wa kuhesabu — kila mahali pangine panaweka:**
| Faili | `new LedgerEntry` | yenye `UserId =` |
|---|---|---|
| EventService.cs | 1 | 1 ✅ |
| FineService.cs | 2 | 2 ✅ |
| **LedgerService.cs** | **1** | **0 ❌** |
| LoanService.cs | 2 | 2 ✅ |
| TreasuryImportService.cs | 5 | 5 ✅ |
| MkobaImportController.cs | 6 | 5 (1 bila) |
| WelfareController.cs | 1 | 1 ✅ |

**Ushahidi kwenye database halisi** (baada ya `POST /api/Ledger/contribution`):
```
               GroupId                |              AccountId               | UserId |  Amount  |     Type     | ReferenceNo
--------------------------------------+--------------------------------------+--------+----------+--------------+------------
 e0e7c724-eda1-429f-8ee5-3bd0040cc285 | b4a988a5-0758-411e-a934-015ca9df3691 |        | 10000.00 | Contribution | T-CONFIRM
                                                                          ^^^^^^^^ NULL
 jumla = 1,  bila_userId = 1
```

**Athari:** Ripoti yoyote inayochuja kwa `LedgerEntry.UserId` (badala ya kupitia `Account → GroupMember`) itaona michango hii kama 0. Mchango uliorekodiwa kwa mkono na mchango ulioingizwa kutoka M-Koba **hawaendani** kwa njia hii.

**Dawa:**
```csharp
var entry = new LedgerEntry
{
    Id = Guid.NewGuid(),
    GroupId = dto.GroupId,
    UserId = groupMember.UserId,      // <-- ongeza hii
    AccountId = account.Id,
    ...
};
```
Kumbuka pia kuangalia ile ya pili ndani ya `MkobaImportController` (moja kati ya 6 haina `UserId`).

---

### 🟠 H-2. `RecordContributionDto.MemberId` jina lake linapotosha

`LedgerService` inapitisha `dto.MemberId` kama **userId**:
```csharp
var groupMember = await _accountResolver.GetGroupMemberAsync(dto.GroupId, dto.MemberId);
// signature halisi: GetGroupMemberAsync(Guid groupId, Guid userId)
```
**Ushahidi — nilijaribu kwa GroupMember.Id halisi:**
```
POST /api/Ledger/contribution  memberId = <GroupMember.Id>  -> 400 "Mwanachama hajapatikana kwenye kikundi hiki."
POST /api/Ledger/contribution  memberId = <User.Id>         -> 200 OK
```
**Dawa:** Badilisha jina liwe `UserId`, au (bora zaidi) fanye code itafute kwa `GroupMemberId` kwa kuwa ndilo jina linaloeleweka kwa mwandamizi wa API. Angalia pia `LedgerController` — haithibitishi kuwa `dto.GroupId` ni kikundi ambacho mtumiaji yuko (kwa sababu `RequireRoleAsync` inafanya hivyo, hii ni salama kwa sasa — lakini ni tegemezi lisilo wazi).

---

### 🟠 H-3. Hakuna majaribio (tests) hata moja

**Ushahidi:** `find . -iname "*test*"` → hakuna matokeo. Hakuna test project kwenye `ChamaLink.sln` (projects 4 tu: Domain, Application, Infrastructure, API).

Hili ndilo sababu kuu ya C-1 na C-2: **bug ya aina hii haiwezi kuonekana na compiler — inahitaji jaribio moja tu la "anzisha host, piga kila controller".**

**Dawa ya haraka yenye thamani kubwa zaidi** (smoke test moja inayonasa bugs zote za DI mara moja):
```csharp
// ChamaLink.Tests/ServiceRegistrationTests.cs
[Fact]
public void Kila_Controller_Inaweza_Kuundwa()
{
    var services = new ServiceCollection();
    // ... jenga DI sawa na Program.cs ...
    var provider = services.BuildServiceProvider(new ServiceProviderOptions
    {
        ValidateOnBuild = true,          // <-- HII ndiyo funguo
        ValidateScopes  = true
    });

    foreach (var t in typeof(Program).Assembly.GetTypes()
                 .Where(t => typeof(ControllerBase).IsAssignableFrom(t)))
        Assert.NotNull(ActivatorUtilities.CreateInstance(provider, t));
}
```
`ValidateOnBuild = true` pekee yake ingegundua C-1 na C-2 wakati wa kuanza app, si baada ya mtumiaji kubonyeza.

---

### 🟠 H-4. `UnauthorizedAccessException` inarudisha **401** badala ya **403**

`GlobalExceptionMiddleware.MapException` (mstari 107) inapeleka `UnauthorizedAccessException` → `401 Unauthorized`.

Kisemantiki: **401 = "sijui wewe ni nani"**, **403 = "nakujua, lakini huna ruhusa"**. Controllers nyingi zinashughulikia hili vizuri (`catch (UnauthorizedAccessException) → 403`), lakini zile zisizo na catch — na njia yoyote inayofika kwenye middleware — zinarudisha 401.

**Kwa nini ina maana:** Frontend yako (`api/client.ts`, mstari 27–33) **inamrudisha mtumiaji kwenye login screen** kwa 401 yoyote:
```ts
if (error.response?.status === 401) {
  localStorage.removeItem('token'); ...
  window.location.href = '/login';
}
```
Yaani mwanachama anayejaribu kufanya kitu asichoruhusiwa **anafukuzwa kwenye mfumo mzima** badala ya kuona "huna ruhusa". Pia hii inachanganya takwimu za usalama (401 nyingi zitaonekana kama mashambulizi ya nenosiri).

**Dawa:**
```csharp
UnauthorizedAccessException => ((int)HttpStatusCode.Forbidden, "Huna ruhusa", ex.Message),
```

---

### 🟠 H-5. N+1 query kwenye `ReportsController.GetEventReport`

Mstari ~477: ndani ya `foreach (var evt in events)` kuna `await _context.EventContributions.Where(...).ToListAsync()` — query moja kwa kila tukio.

**Dawa:** pakia kwa mara moja:
```csharp
var eventIds = events.Select(e => e.Id).ToList();
var allContribs = await _context.EventContributions
    .Where(ec => eventIds.Contains(ec.GroupEventId)).ToListAsync();
var byEvent = allContribs.ToLookup(c => c.GroupEventId);
```

---

## 4. MATATIZO MADOGO (Medium)

| # | Tatizo | Ushahidi / Mahali | Dawa |
|---|---|---|---|
| M-1 | **Rate limiting inavunjika nyuma ya reverse proxy.** `PartitionKey = RemoteIpAddress` — nyuma ya nginx/Azure/K8s hii ni IP ya proxy, yaani **watumiaji wote wanashiriki kikomo kimoja** (na mtu mmoja anaweza kuwafungia wote). Program.cs m.97, 107 | Tumia `UseForwardedHeaders` + `ForwardedFor`, au partition kwa `sub` ya JWT pamoja na IP |
| M-2 | **Hakuna `bin/obj` .gitignore kwenye root.** Faili **246** za `bin/`+`obj/` zimeingia git; `.git` = **15 MB** (code yenyewe ni ndogo sana). `.gitignore` ya root **haipo**; ile ya `chamalink-web-new/` haitoshi kwa .NET | Ongeza root `.gitignore` ya kawaida ya .NET, kisha `git rm -r --cached **/bin **/obj` |
| M-3 | **`chamalink-web-new/.env` imo kwenye git.** Kwa sasa ina URL tu (`VITE_API_BASE_URL`), si siri — lakini tabia hii ni hatari; siku ukiiweka API key itaingia git moja kwa moja | `git rm --cached chamalink-web-new/.env`; `.env.example` inatosha |
| M-4 | **Nenosiri dhaifu:** `MinimumLength = 6`, hakuna ukaguzi wa ugumu wala orodha ya manenosiri yaliyovujishwa (`AuthDtos.cs` m.24) | Panda hadi 8–10; ongeza ukaguzi wa kawaida |
| M-5 | **JWT ya siku 7, hakuna refresh token, hakuna revocation.** `AuthService.cs` m.71 `AddDays(7)`. Token inahifadhiwa kwenye `localStorage` (`AuthContext.tsx` m.38) → inaweza kuibwa kwa XSS, na haiwezi kufutwa mpaka iishe | Access token fupi (15–60 dk) + refresh token kwenye httpOnly cookie; au angalau punguza muda na ongeza denylist |
| M-6 | **User enumeration kwenye register.** `AuthService.RegisterAsync` m.26: `"Email tayari imeshasajiliwa."` inamwambia mvamizi email ipo | Rudisha ujumbe usio na maana ("angalia barua pepe yako") au thibitisha kwa barua pepe |
| M-7 | **Mkataba wa makosa hauendani.** `AuthService` inatumia `throw new Exception(...)` (si `ArgumentException`), na `AuthController`/`LoanController`/`LedgerController` bado zina `catch (Exception) → 400`. GlobalExceptionMiddleware iliyoandikwa vizuri **haifikiwi** kwa sababu controllers zinakamata kila kitu kwanza — na `ex.Message` mbichi inapelekwa kwa client (mf. ujumbe wa Postgres) | Acha controllers zisitumie `catch (Exception)`; tumia exception maalum (`AppException`, `NotFoundException`) na uache middleware ifanye kazi |
| M-8 | **DI error inarudi kama 400, si 500.** `InvalidOperationException` → 400 (middleware m.122). Tatizo la server linaonekana kama kosa la mtumiaji — ndiyo maana C-1/C-2 zilikuwa ngumu kuona | Tenganisha: `InvalidOperationException` kutoka business logic vs. kutoka DI. Bora: zima `catch(Exception)` (M-7) na utumie `AppException` |
| M-9 | **Hakuna CI/CD, hakuna Dockerfile, hakuna README.** `find` ya `Dockerfile`/`*.yml` → hakuna. `README.md` wa root ni mtupu. Hakuna `.github/workflows` | GitHub Actions: build + test + `dotnet ef migrations script` kwenye kila PR |
| M-10 | **Hakuna ukomo wa ukubwa wa faili la M-Koba/Treasury.** `MkobaImportController` m.71–76 unakopi faili nzima kwenye `MemoryStream` bila ukomo → mtu anaweza kupakia faili kubwa na kuisha RAM | `[RequestSizeLimit]` + `file.Length > MAX → 400`; tumia `IFormFile.OpenReadStream()` moja kwa moja badala ya kuikopi |
| M-11 | **`GetGroupSummaryAsync` inatoa picha isiyo kamili.** `LedgerService.cs` m.55–68: `totalLoans` = jumla ya `LoanDisbursement` pekee, **bila kutoa marejesho**; `totalSavings` inahesabu `Contribution` tu (haipati `WelfareTopUp`/`EventContribution`) | Tumia `AnalyticsService.GetGroupBalanceAsync` ambayo tayari inafanya hivi vizuri (`cashAvailable`, `outstandingLoans`, `totalAssets`) |
| M-12 | **`GetMemberStatementAsync` inasoma kwenye memory.** `LedgerService.cs` m.82–101: inachukua LedgerEntries **zote** za mwanachama kwenye RAM (`ToListAsync`) kisha `Where`/`Sum` ndani ya C# | Sogeza `Sum`/`Where` kwenye query ya SQL |
| M-13 | **`Account` haina concurrency token.** `Loan` na `Withdrawal` zina `xmin`/`IsRowVersion()`, lakini `Account` (yenye `Balance`, `AdvanceBalance`) haina. `AccountResolverService` inaelewa mbio za *kuunda* account, si mbio za *kubadilisha salio* | Ongeza `IsRowVersion()` kwenye `Account` |
| M-14 | **`Microsoft.Extensions.Hosting` 10.0.11 kwenye net8.0.** Paketi za .NET 10 kwenye project ya .NET 8 — inajenga, lakini inachanganya na pia inavuta mtegemeo mkubwa usiohitajika (`AddHostedService` tayari ipo kwenye `Microsoft.AspNetCore.App`) | Ondoa paketi hiyo, au shusha hadi `8.0.x` |
| M-15 | **Frontend: bundle 549 kB (gzip 164 kB)** — onyo la Vite. Hakuna code-splitting | `React.lazy` kwa kila ukurasa (`DashboardPage`, `ReportsPage`, n.k.) |
| M-16 | **Swagger ikiwa wazi.** Ni Development pekee (Program.cs m.189) — sawa — lakini hakikisha `ASPNETCORE_ENVIRONMENT` si `Development` kwenye production, vinginevyo ramani yote ya API iko wazi | Thibitisha env ya production |

---

## 5. Nini kimefanywa **vizuri** (nisahau kusema)

Hivi ni vya thamani — usiviharibishe:

- ✅ **Clean Architecture imefuatwa kwa uangalifu** — Domain haitegemei EF, DTOs ziko Application, EF kwenye Infrastructure.
- ✅ **`GroupAuthorizationService`** — mahali pamoja pa uidhinishaji, na `RequireGovernanceApprovalAsync` inayosomea katiba ya kila kikundi badala ya ku-hardcode roles. Hii ni design nzuri sana.
- ✅ **`ClaimsPrincipalExtensions.GetUserId()`** — IDOR imefungwa kwa njia sahihi (actor anatoka JWT, si kutoka kwa client).
- ✅ **Partial unique indexes** kwenye `Fine` (`"Period" IS NOT NULL` / `"GroupEventId" IS NOT NULL`) — suluhisho sahihi kabisa kwa tatizo la NULLs za Postgres kwenye unique indexes. Hii ni uelewa wa juu.
- ✅ **`xmin` / `IsRowVersion()`** kwenye `Loan` na `Withdrawal` — njia sahihi ya Npgsql, na imeandikwa upya kutoka `UseXminAsConcurrencyToken` iliyokuwa obsolete.
- ✅ **`AccountResolverService`** — tatizo la "AccountId tatu tofauti" limepatwa suluhisho la moja kwa moja, pamoja na kushughulikia mbio za kuunda account.
- ✅ **`GlobalExceptionMiddleware`** imeandikwa vizuri: correlation ID, `HasStarted` check, stack trace inakwenda kwenye log tu, `debug` ni Development pekee. (Tatizo ni kwamba controllers haziruhusu ifanye kazi — angalia M-7.)
- ✅ **Rate limiting** yenye tier mbili (global 120/dk, auth 5/dk) — niliithibitisha inafanya kazi: majaribio 4 ya kwanza → 400, la 5 na kuendelea → **429**.
- ✅ **Duplicate import protection** kwa SHA-256 file hash + unique index `(GroupId, FileHash)`.
- ✅ **Comments zinaeleza *kwa nini***, si tu *nini* — hii ni adimu na ya thamani kubwa kwa mtu atakayerithi code hii.
- ✅ **Ujumbe wa makosa kwa Kiswahili** unaofaa kuonyeshwa kwa mtumiaji.
- ✅ **Validation attributes** kwenye DTOs muhimu (Auth 11, Group 24, Event 6, Withdrawal 5).

---

## 6. Mpangilio wa kazi ninayopendekeza

**Leo (saa 1–2):**
1. 🔴 Badilisha JWT key + DB password; ondoa siri kwenye `appsettings.json` (**C-3**)
2. 🔴 Weka mistari 3 ya DI kwenye `Program.cs` (**C-1, C-2**) — *tayari nimefanya hivi kwenye workspace yako*
3. 🟠 Weka `UserId = groupMember.UserId` kwenye `LedgerService` (**H-1**)
4. 🟠 401 → 403 kwenye `GlobalExceptionMiddleware` (**H-4**)

**Wiki hii:**
5. 🟠 Test project moja + `ValidateOnBuild = true` (**H-3**) — hii itazuia C-1/C-2 zisirudie
6. 🟠 Root `.gitignore` + `git rm -r --cached` ya bin/obj (**M-2**)
7. 🟠 N+1 ya `GetEventReport` (**H-5**) + ukomo wa ukubwa wa faili (**M-10**)
8. 🟠 Ondoa `catch (Exception)` kwenye controllers, tumia `AppException` (**M-7, M-8**)

**Baadaye:**
9. 🟡 Refresh tokens + access token fupi (**M-5**)
10. 🟡 CI (GitHub Actions) + Dockerfile (**M-9**)
11. 🟡 Rate limiting kwa forwarded headers (**M-1**), concurrency token kwenye `Account` (**M-13**)

---

## 7. Kiambatisho: jinsi ya kujaribu mwenyewe

```bash
# Backend
export PATH=$HOME/.dotnet:$PATH DOTNET_ROOT=$HOME/.dotnet
dotnet build ChamaLink.sln -c Release

# PostgreSQL ya ndani (Debian/Ubuntu)
sudo apt-get install -y postgresql-17
/usr/lib/postgresql/17/bin/initdb -D /tmp/pgdata -U postgres --auth=trust
/usr/lib/postgresql/17/bin/pg_ctl -D /tmp/pgdata -l /tmp/pg.log \
  -o "-p 5432 -c listen_addresses=127.0.0.1 -c unix_socket_directories=/tmp/pgsock" start
/usr/lib/postgresql/17/bin/psql -h 127.0.0.1 -U postgres -c "CREATE DATABASE chamalink;"

# Migrations
dotnet tool install --global dotnet-ef --version 8.*
dotnet ef database update --project ChamaLink.Infrastructure --startup-project ChamaLink.API \
  --connection "Host=127.0.0.1;Database=chamalink;Username=postgres;Password=sandbox123"

# Endesha
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://0.0.0.0:5168 \
  dotnet run --project ChamaLink.API

# Frontend
cd chamalink-web-new && npm install && npm run build
```

**Jaribio la DI (la muhimu zaidi):** baada ya kuandika service mpya yoyote, piga kila endpoint mara moja. Build ya `0 Errors` **haimaanishi** kuwa controller inaweza kuundwa.
