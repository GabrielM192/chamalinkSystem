# ChamaLink

Mfumo wa usimamizi wa vyama vikuu (chama / SACCOS) — michango, mikopo, faini, madeni, matukio ya ustawi (msiba/sherehe), utoaji fedha na ripoti.

**Backend:** .NET 8 (Clean Architecture) + PostgreSQL + Entity Framework Core
**Frontend:** React 19 + TypeScript + Vite + Tailwind

---

## Muundo wa miradi

```
ChamaLink.sln
├── ChamaLink.Domain            Entities + enums + exceptions (haitegemei EF wala ASP.NET)
├── ChamaLink.Application       DTOs + interfaces
├── ChamaLink.Infrastructure    EF Core DbContext, migrations, services za biashara
├── ChamaLink.API               Controllers, middleware, Program.cs
└── ChamaLink.Tests             Majaribio (xUnit + WebApplicationFactory)

chamalink-web-new/              Frontend ya React
```

---

## Usanidi wa kwanza

### 1. PostgreSQL

```bash
# Debian/Ubuntu
sudo apt-get install postgresql
sudo -u postgres createdb chamalink
```

### 2. Siri (MUHIMU — usiruke hatua hii)

Siri **hazikubaliki** kutoka kwenye faili zilizomo git. `Program.cs` **itakataa kuanza** ikiwa:

* `Jwt:Key` haipo,
* ni fupi kuliko herufi 32,
* au ni ile ile iliyoingia kwenye GitHub zamani.

Chagua njia moja:

**Njia A — faili ya ndani (rahisi kwa maendeleo):**
```bash
cd ChamaLink.API
cp appsettings.Development.local.json.example appsettings.Development.local.json
# halafu badilisha REPLACE_ME kuwa key halisi:
openssl rand -base64 64
```
Faili `*.local.json` imezuiliwa na `.gitignore` — haiingii kwenye git kamwe.

**Njia B — User Secrets:**
```bash
cd ChamaLink.API
dotnet user-secrets set "Jwt:Key" "$(openssl rand -base64 64)"
dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
  "Host=localhost;Database=chamalink;Username=postgres;Password=NYWEWE"
```

**Njia C — environment variables (production):**
```bash
export Jwt__Key="..."
export ConnectionStrings__DefaultConnection="Host=...;Database=...;Username=...;Password=..."
```

### 3. Migrations

```bash
dotnet tool install --global dotnet-ef --version 8.*

dotnet ef database update \
  --project ChamaLink.Infrastructure \
  --startup-project ChamaLink.API
```

### 4. Endesha

```bash
# Backend (port 5168)
dotnet run --project ChamaLink.API

# Frontend (port 5173)
cd chamalink-web-new
npm install
npm run dev
```

Swagger: <http://localhost:5168/swagger> (Development pekee).

---

## Kujenga na kupima

```bash
dotnet build ChamaLink.sln -c Release
dotnet test  ChamaLink.Tests/ChamaLink.Tests.csproj
```

### Kwa nini `dotnet test` ni ya lazima (si `dotnet build` pekee)

Mfumo huu umepatwa na bug ya aina moja **mara tatu**: service iliandikwa, `dotnet build` ikapita bila error — lakini service **haikusajiliwa kwenye Dependency Injection**, kwa hiyo kila endpoint ya controller husika ilikuwa inarudisha HTTP 400/500.

Hii haiwezi kuonekana kwa compiler: ASP.NET Core huunda controller kwa DI **wakati wa ombi**, si wakati wa compile.

Nimeithibitisha hivi: niliondoa usajili mmoja kwa makusudi —

```
dotnet build  -> Build succeeded.        ← HALIKUGUNDUA
dotnet test   -> Failed: ReportsController: Unable to resolve service for type
                 'ComplianceReportService' ...    ← LILIGUNDUA MARA MOJA
```

`ChamaLink.Tests/ServiceRegistrationTests.cs` hujenga **API halisi** (`WebApplicationFactory`) na kujaribu kuunda kila controller. **Ukiongeza service mpya, iongeze kwenye `Program.cs` — jaribio litakukumbusha ukiisahau.**

---

## Kanuni za makosa (error contract)

Makosa yote yanapita `GlobalExceptionMiddleware` — **usirudishe `try/catch (Exception)` kwenye controller**.

Tumia exception maalum kutoka `ChamaLink.Domain.Exceptions`:

| Exception | HTTP | Matumizi |
|---|---|---|
| `ValidationException` | 400 | Ombi halikukidhi sheria za biashara |
| `UnauthorizedAccessException` | **403** | Anajulikana, lakini hana ruhusa |
| `ForbiddenException` | 403 | Kama hapo juu, kwa code mpya |
| `NotFoundException` | 404 | Rekodi haipo |
| `ConflictException` | 409 | Mgongano (nafasi imechukuliwa, n.k.) |
| `DbUpdateConcurrencyException` | 409 | Mabadiliko sambamba (xmin token) |
| kitu kingine chochote | 500 | Kosa la server — **bila** maelezo ya ndani kwa client |

Jibu ni RFC 7807 ProblemDetails, likiwa na `message` **na** `detail` (frontend inasoma `message`).

---

## Usalama — mambo ya kuzingatia

* **Siri** hazipo kwenye git. Angalia `.gitignore` na sehemu ya usanidi hapo juu.
* **Ulinzi wa IDOR**: actor daima anatoka JWT (`User.GetUserId()`), kamwe kutoka kwa client.
* **Ulinzi wa kikundi**: tumia `GroupAuthorizationService.RequireMembershipAsync` /
  `RequireRoleAsync` / `RequireGovernanceApprovalAsync`.
* **Rate limiting**: 120/dakika kwa ujumla, 5/dakika kwa `Auth` (kwa IP) +
  kizuizi cha majaribio 5 kwa email moja (dakika 15) ndani ya `AuthService`.
* **Ukomo wa faili**: 10 MB kwa M-Koba na Excel ya Treasury.

### Bado kuna kazi ya usalama (angalia `RIPOTI-YA-UKAGUZI.md`)

1. **Token zilizotolewa kabla ya kubadilisha key zinapaswa kuchukuliwa kuwa zimevunjwa.**
2. Hakuna **refresh token** wala njia ya **kufuta token** (revocation). Muda wa token
   unategemea `Jwt:ExpiryDays` (chaguo-msingi: siku 1).
3. Hakuna **uthibitisho wa barua pepe**, kwa hiyo `/api/Auth/register` bado inaweza
   kutumika kubaini kama email ipo (user enumeration).
4. Kizuizi cha login kiko kwenye **RAM** — hakishirikishwi kati ya instances nyingi.
5. Ukiwa nyuma ya proxy, ongeza IP za proxy zako kwenye `KnownProxies`
   (`Program.cs`, sehemu ya `UseForwardedHeaders`).

---

## Faili muhimu

| Faili | Maelezo |
|---|---|
| `RIPOTI-YA-UKAGUZI.md` | Ripoti kamili ya ukaguzi (2026-09-15) — matatizo yote + ushahidi |
| `ChamaLink.API/Program.cs` | DI, JWT, CORS, rate limiting, middleware |
| `ChamaLink.Infrastructure/Services/GroupAuthorizationService.cs` | Ulinzi wa kikundi/cheo |
| `ChamaLink.API/Middleware/GlobalExceptionMiddleware.cs` | Ramani ya makosa → HTTP |
| `ChamaLink.Domain/Exceptions/AppExceptions.cs` | Aina za makosa ya biashara |
| `ChamaLink.Tests/ServiceRegistrationTests.cs` | Ulinzi dhidi ya bugs za DI |
