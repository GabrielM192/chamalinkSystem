using System.Text;
using System.Threading.RateLimiting;
using ChamaLink.Application.Interfaces;
using ChamaLink.Infrastructure.Services;
using Microsoft.Extensions.Hosting;
using ChamaLink.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// SIRI ZA NDANI (ukaguzi 2026-09-15, C-3): faili `*.local.json` zinapakiwa
// MWISHO, yaani zinashinda appsettings.json na appsettings.{Env}.json.
// Faili hizi zimezuiliwa na .gitignore, kwa hiyo siri zako za ndani haziingii
// kwenye git kamwe. Ongeza tu appsettings.Development.local.json kwenye
// folda ya ChamaLink.API (tazama README.md, sehemu 'Usanidi wa kwanza').
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddJsonFile(
        "appsettings.Development.local.json", optional: true, reloadOnChange: true);
}
else
{
    builder.Configuration.AddJsonFile(
        "appsettings.Production.local.json", optional: true, reloadOnChange: true);
}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<GroupService>();
builder.Services.AddScoped<IMKobaParserService, MkobaParserService>();
builder.Services.AddScoped<LedgerService>();

// FIX: AccountResolverService is the one shared place that decides what
// AccountId to use for a LedgerEntry. Every service/controller that writes
// to the ledger needs this registered so it can be injected.
builder.Services.AddScoped<AccountResolverService>();

// FIX (2026-09-16): GroupMember.TotalContributionsCount na AdvanceBalance
// ni counter zilizohifadhiwa (denormalized). Njia mbili za import zilikuwa
// zinazibadilisha, lakini njia ya mchango wa mkono (LedgerService) haikuwa
// inazibadilisha - hivyo ripoti ya WhatsApp ilionyesha "Michango: Mara N"
// ndogo kuliko uhalisia. Huduma hii inahesabu counter hizo upya kutoka
// kwenye ledger (chanzo cha ukweli) badala ya kuzibadilisha moja kwa moja.
builder.Services.AddScoped<MemberCounterSyncService>();

// FIX: IEventService existed but was never registered before, so
// EventController (also new) could not be created and the Event/Tukio
// feature was completely unreachable.
builder.Services.AddScoped<IEventService, EventService>();

// NEW (Sprint 1 + Sprint 2 gaps): Fine/Debt entities, the financial
// intelligence layer (Collection Rate / Defaulters / Group Balance /
// Group Financial Summary), and the Withdrawal approval workflow.
builder.Services.AddScoped<FineService>();
builder.Services.AddScoped<DebtService>();
builder.Services.AddScoped<ComplianceSnapshotService>();
builder.Services.AddScoped<AnalyticsService>();
builder.Services.AddScoped<WithdrawalService>();

// BUG FIX: LoanService/LoanController already existed (Loan entity, DTOs,
// EF model config were all in place), but LoanService was never
// registered here - the exact same gap AccountResolverService and
// IEventService had before. Without this, every LoanController endpoint
// would throw an InvalidOperationException at request time even though
// the whole feature builds cleanly.
builder.Services.AddScoped<LoanService>();

// SECURITY FIX (audit Stage 2: centralized authorization policy). See
// GroupAuthorizationService.cs - every controller that needs to check
// "is this caller a member/leader of this group" now goes through here.
builder.Services.AddScoped<GroupAuthorizationService>();

// BUG FIX (ukaguzi 2026-09-15): hizi tatu zilikuwa hazijasajiliwa kabisa,
// ingawa classes zake zilikuwepo na build ilipita bila error - DI
// inashindwa runtime tu, si compile time. Matokeo yake:
//   * ComplianceReportService  -> ReportsController NZIMA (endpoints 16)
//                                 ilikuwa inarudisha HTTP 400 kila mara.
//   * TreasuryImportService +
//     ITreasuryExcelParserService -> TreasuryImportController (preview/commit)
//                                 ilikuwa inarudisha HTTP 400 kila mara.
    builder.Services.AddScoped<ComplianceReportService>();
    // Ulinganisho (Reconciliation Engine, 2026-09-19): PDF vs Excel vs Ledger.
    builder.Services.AddScoped<ReconciliationService>();
    // Statement ya Mwezi (Awamu 1, 2026-09-19): wajibu vs utekelezaji
    // (Lengo/Ametoa/Upungufu/Hali) kwa mwezi mmoja.
    builder.Services.AddScoped<MemberStatementService>();
    // Data Repair (Awamu 2, 2026-09-19): ukarabati wa data ya bug-era
    // (counter drift + snapshots tupu + loan repayment gaps).
    builder.Services.AddScoped<DataRepairService>();
builder.Services.AddScoped<ITreasuryExcelParserService, TreasuryExcelParserService>();
builder.Services.AddScoped<TreasuryImportService>();

// V2 — Financial OS foundation (Option C controlled rebuild)
builder.Services.AddScoped<BusinessRuleEngine>();
builder.Services.AddScoped<ObligationLedgerService>(); // Single source for obligation queue
builder.Services.AddScoped<AllocationEngine>();
builder.Services.AddScoped<FinancialPositionService>();
builder.Services.AddScoped<MemberStatusEngine>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<WelfareService>();
builder.Services.AddScoped<ReconciliationServiceV2>();
// Verification & Reconciliation (Data Repair kwanza, frontend baadaye)
builder.Services.AddScoped<VerificationService>();
// Ledger Classification Audit (uchunguzi wa 135k ya Tunganege)
builder.Services.AddScoped<LedgerClassificationService>();
// Obligation Engine + Source-of-Truth Audit
builder.Services.AddScoped<ObligationEngine>();
builder.Services.AddScoped<PositionProofService>();
// Ledger Reclassification (generic, no hardcoded)
builder.Services.AddScoped<LedgerReclassificationService>();
// Historical JoinFee with approval workflow
builder.Services.AddScoped<HistoricalJoinFeeService>();
builder.Services.AddScoped<ChamaLink.Infrastructure.Services.Truth.TruthLedgerService>(); // Injini ya Ukweli (kusoma)
builder.Services.AddScoped<ChamaLink.Infrastructure.Services.Truth.TruthSetupService>();  // Injini ya Ukweli (kuandika)
builder.Services.AddScoped<ChamaLink.Infrastructure.Services.MembershipService>();                // Usajili, maombi ya kujiunga, nafasi, namba za uanachama

// SECURITY FIX (audit Stage 2: "CORS haipo"): with no CORS policy at
// all, a browser blocks the React/TS frontend from calling this API
// from any origin other than the API's own (its default, same-origin-
// only behaviour) - so every cross-origin call from the actual app
// (dev server on a different port, or a separate production domain)
// would fail in the browser before ever reaching a controller. Origins
// come from configuration (Cors:AllowedOrigins in appsettings), never
// hardcoded and never AllowAnyOrigin - only the frontend's own known
// origins are ever allowed to call this API directly from client JS.
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        if (corsOrigins.Length > 0)
        {
            policy.WithOrigins(corsOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        }
        // No origins configured -> the policy allows nothing. Safer
        // default than silently falling back to wide-open.
    });
});

// SECURITY FIX (audit Stage 2: "Rate limiting haipo"): nothing in front
// of this API stopped a single client from hammering any endpoint as
// fast as the network allowed - most importantly POST /api/Auth/login
// and /api/Auth/register, which carry no [Authorize] at all and are the
// textbook brute-force / credential-stuffing / fake-account-spam
// targets. Two tiers: a generous global limit so normal use is never
// affected, and a much tighter "auth" policy applied only to those two
// endpoints (see AuthController). Both are partitioned per client IP so
// one abusive caller can't burn through the limit that's meant to cover
// everyone else sharing this process.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// ============================================================================
// SECURITY FIX (ukaguzi 2026-09-15, C-3): siri hazikubaliki tena kutoka faili.
//
// Zamani appsettings.json ilikuwa na JWT key na neno la siri la database
// zikiwa wazi, na zilikuwa zimeingia kwenye git tangu commit ya kwanza:
//     "Jwt:Key": "ChamaLinkSandboxSecretKey2024ForTestingOnlyMinimum32Bytes!!"
//
// Hii si nadharia - NILIITHIBITISHA. Nilitumia key hiyo kutengeneza JWT yangu
// mwenyewe (Python + HMAC-SHA256) kwa mtumiaji asiyeopo hata kwenye database,
// na API iliikubali:
//     GET /api/Group/my-groups -> HTTP 200     (token niliyotengeneza mimi)
//     control: token yenye signature iliyoharibiwa -> HTTP 401
// Yaani ulinzi wote wa GroupAuthorizationService unategemea tu kuwa JWT
// haiwezi kuundwa nje ya server - na ulinzi huo ulikuwa umevunjwa.
//
// Sasa key inatoka KWA LAZIMA kutoka environment variable au User Secrets,
// na mfumo UNASHINDWA KUANZA ikiwa haipo au ni dhaifu - bora mfumo usianze
// kuliko uanze ukiwa wazi.
// ============================================================================
// Configure JWT Authentication.
//
// Key INASOMWA NDANI YA LAMBDA hii, wakati wa kutumika - si hapa juu. Kwa
// nini: `builder.Configuration` (inayopatikana kabla ya Build()) haina
// vyanzo vyote vya configuration, hasa vile vinavyoongezwa na
// WebApplicationFactory kwenye majaribio. `IConfiguration` inayochukuliwa
// hapa ndani ya lambda ni ile kamili (ConfigurationRoot), kwa hiyo inapatana
// kabisa na ukaguzi wa siri ulio chini (baada ya Build()).
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    var jwtSettings = builder.Configuration.GetSection("Jwt");
    var signingKey = Encoding.UTF8.GetBytes(jwtSettings["Key"]!);

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(signingKey)
    };
});

builder.Services.AddControllers();
// INJINI YA UKWELI: kazi za nyuma za ZAMANI zinaandika faini/madeni kwenye meza za
// zamani (Fines, Debts, Loans.LatePenaltyCharged) kwa kanuni tofauti na Injini ya Ukweli.
// Zikiendelea, mfumo una "ukweli" miwili unaogongana. Kwa hiyo zimezimwa kwa chaguo-msingi;
// ziwashe tu kwa makusudi: appsettings -> "Legacy": { "BackgroundJobs": true }.
if (builder.Configuration.GetValue<bool>("Legacy:BackgroundJobs"))
{
    builder.Services.AddHostedService<WelfarePenaltyBackgroundService>();
    builder.Services.AddHostedService<ContributionComplianceBackgroundService>();
    builder.Services.AddHostedService<LoanPenaltyBackgroundService>();
}
builder.Services.AddEndpointsApiExplorer();

// Swagger with Authorization Support
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "ChamaLink API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Ingiza JWT Token hivi: Bearer {token}",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// Fail-fast kwa production: kabla ya hata kujenga app.
// builder.Configuration hapa ina appsettings*.json + environment variables -
// ndizo njia mbili za kawaida za uzalishaji.
{
    var earlyKey = builder.Configuration["Jwt:Key"] ?? string.Empty;
    if (earlyKey.Length > 0 && earlyKey == "ChamaLinkSandboxSecretKey2024ForTestingOnlyMinimum32Bytes!!")
    {
        throw new InvalidOperationException(
            "Unatumia JWT key ILE ILE iliyo kwenye GitHub ya umma - mtu yeyote anaweza " +
            "kutengeneza token za uongo kwa key hiyo. Tengeneza key mpya: openssl rand -base64 64");
    }
}

var app = builder.Build();

// ============================================================================
// UKAGUZI WA SIRI (ukaguzi 2026-09-15, C-3)
//
// Kwa nini hapa (baada ya Build()) na si kabla?
// Program.cs inasoma `builder.Configuration` kabla ya Build() - wakati huo
// vyanzo vya configuration vya WebApplicationFactory (UseSetting,
// AddInMemoryCollection kwenye majaribio) HAVIJAWEKWA bado. Kwa kuangalia
// `app.Configuration` (ConfigurationRoot kamili) tunapata vyanzo vyote,
// ikiwemo zile za majaribio - na ndiyo sababu ChamaLink.Tests inaweza
// kuanzisha API hii bila faili halisi ya siri.
// ============================================================================
{
    var configuredJwtKey = app.Configuration["Jwt:Key"] ?? builder.Configuration["Jwt:Key"] ?? string.Empty;

    if (string.IsNullOrWhiteSpace(configuredJwtKey) || configuredJwtKey.Contains("REPLACE_ME"))
    {
        throw new InvalidOperationException(
            "Jwt:Key haijawekwa. Weka environment variable Jwt__Key, au tengeneza " +
            "appsettings.Development.local.json (haipo kwenye git). Angalia README.md, " +
            "sehemu 'Usanidi wa kwanza'.");
    }

    if (configuredJwtKey.Length < 32)
    {
        throw new InvalidOperationException(
            $"Jwt:Key ni fupi sana (herufi {configuredJwtKey.Length}). HS256 inahitaji " +
            "angalau herufi 32. Tengeneza moja kwa: openssl rand -base64 64");
    }

    // Onyo la wazi ikiwa bado unatumia key iliyokwisha-vuja kwenye GitHub.
    if (configuredJwtKey == "ChamaLinkSandboxSecretKey2024ForTestingOnlyMinimum32Bytes!!")
    {
        throw new InvalidOperationException(
            "Unatumia JWT key ILE ILE iliyo kwenye GitHub ya umma - mtu yeyote anaweza " +
            "kutengeneza token za uongo kwa key hiyo. Tengeneza key mpya: openssl rand -base64 64");
    }
}

// SECURITY FIX (audit 2.3/2.4): registered first so it wraps every other
// middleware/controller below it - it is the last line of defence for
// anything that isn't already handled by a controller's own try/catch.
app.UseMiddleware<ChamaLink.API.Middleware.GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// FIX (ukaguzi 2026-09-15, M-1): rate limiting inagawanya (partition) kwa
// `Connection.RemoteIpAddress`. Nyuma ya reverse proxy (nginx, Azure App
// Service, Kubernetes ingress, Cloudflare) RemoteIpAddress ni IP YA PROXY -
// yaani watumiaji WOTE wanashiriki kikomo kimoja cha 120/dakika, na mtu mmoja
// anayepiga sana anaweza kuwafungia wengine wote (na kwa policy ya "auth",
// kuwafungia watu wote nje ya mfumo kwa dakika nzima).
//
// UseForwardedHeaders husoma X-Forwarded-For na kuweka RemoteIpAddress kuwa
// IP halisi ya mteja, kwa hiyo rate limiter inafanya kazi tena kwa usahihi.
//
// MUHIMU: tumia tu ukiwa NYUMA YA PROXY unayoimiliki. Nimeipunguza kwa
// loopback + kufuta KnownProxies zilizo default kwa sababu kumwacha mtu
// yeyote atume X-Forwarded-For bila kikomo ni njia ya kuepuka rate limit.
// Ukiwa nyuma ya Azure/AppGateway/K8s, ongeza IP/subnet ya proxy yako hapa.
if (!builder.Environment.IsDevelopment())
{
    var fwdOptions = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
    };
    fwdOptions.KnownNetworks.Clear();
    fwdOptions.KnownProxies.Clear();
    // TODO(kuhamia production): ongeza hapa proxy zako halisi, mfano
    // fwdOptions.KnownProxies.Add(IPAddress.Parse("10.0.0.1"));
    app.UseForwardedHeaders(fwdOptions);
}

app.UseHttpsRedirection();
app.UseCors("Frontend");
app.UseRateLimiter();
app.UseAuthentication(); // Must be before UseAuthorization
app.UseAuthorization();
app.MapControllers();

app.Run();

// Jaribio (ukaguzi 2026-09-15): Program.cs inatumia "top-level statements",
// ambayo huzalisha class ya `Program` ya ndani (internal). WebApplicationFactory
// kwenye ChamaLink.Tests inahitaji kuirejelea ili kujenga API halisi na
// kuthibitisha kuwa kila controller inaweza kuundwa - angalia
// ChamaLink.Tests/ServiceRegistrationTests.cs.
public partial class Program { }
