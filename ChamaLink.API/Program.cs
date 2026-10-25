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

// SASISHO17: maandishi ya seva hayategemei locale ya mashine (mf. "45,000" kila mahali, si "45 000")
ChamaLink.Infrastructure.Services.Common.AppCulture.UseInvariant();

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

// SASISHO18: michango ya tukio ya zamani (EventService/EventController) imeondolewa.
// Vikundi vya "kila tukio" vinatumia EventWalletService (pochi + wajibu), na
// michango ya matukio ya kikundi cha mwezi inapita Mafao (BenefitService).

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
// SASISHO17 (M): huduma za uchunguzi/za zamani zilizotumiwa na DebugController/HistoricalJoinFeeController tu
// (ReconciliationServiceV2, VerificationService, LedgerClassificationService, ObligationEngine, PositionProofService,
// LedgerReclassificationService, HistoricalJoinFeeService) ZIMEFUTWA pamoja na controllers zao — msimbo usiotumika ni eneo la mashambulizi.
builder.Services.AddScoped<ChamaLink.Infrastructure.Services.Truth.TruthLedgerService>(); // Injini ya Ukweli (kusoma)
builder.Services.AddScoped<ChamaLink.Infrastructure.Services.Truth.TruthSetupService>();  // Injini ya Ukweli (kuandika)
builder.Services.AddScoped<ChamaLink.Infrastructure.Services.MembershipService>();                // Usajili, maombi ya kujiunga, nafasi, namba za uanachama
// Mafao (msiba/sherehe), arifa, kura za hali ya mwanachama — Awamu B/C/D ya Ushauri 2026-09-28
builder.Services.AddScoped<ChamaLink.Infrastructure.Services.Benefits.NotificationService>();
builder.Services.AddScoped<ChamaLink.Infrastructure.Services.Benefits.IEvidenceStorage, ChamaLink.Infrastructure.Services.Benefits.LocalDiskEvidenceStorage>();
builder.Services.AddScoped<ChamaLink.Infrastructure.Services.Benefits.BenefitService>();
// SASISHO18: pochi + wajibu wa matukio kwa vikundi vya "kila tukio".
builder.Services.AddScoped<ChamaLink.Infrastructure.Services.Events.EventWalletService>();
builder.Services.AddScoped<ChamaLink.Infrastructure.Services.Events.AtRiskService>(); // SASISHO19: hatarini kuondolewa + kurudi
// SASISHO21 — sanduku la malipo: kodi ya malipo, ulinganisho wa statement, madai ya wanachama.
builder.Services.AddScoped<ChamaLink.Infrastructure.Services.Events.PaymentInboxService>();
// SASISHO21 — rufaa: viongozi 2+ wanaamua; mmoja hawezi.
builder.Services.AddScoped<ChamaLink.Infrastructure.Services.Appeals.AppealService>();
// SASISHO21 — PDF za hazina (risiti + ripoti). Zinaandikwa ndani ya mfumo, hakuna maktaba ya nje.
builder.Services.AddScoped<ChamaLink.Infrastructure.Services.Documents.PaymentDocumentsService>();
// SASISHO21 (Sehemu ya 2) — SMS za no-reply. Mtoa-huduma HATAJWI kwenye msimbo;
// anabadilishwa kwa appsettings (Sms:Gateway = "Log" | "Http").
builder.Services.AddScoped<ChamaLink.Infrastructure.Services.Sms.SmsService>();
builder.Services.AddHttpClient<ChamaLink.Infrastructure.Services.Sms.ISmsGateway, ChamaLink.Infrastructure.Services.Sms.HttpSmsGateway>();
builder.Services.AddSingleton<ChamaLink.Infrastructure.Services.Sms.ISmsGateway>(sp =>
    string.Equals(sp.GetRequiredService<IConfiguration>()["Sms:Gateway"], "Http", StringComparison.OrdinalIgnoreCase)
        ? sp.GetRequiredService<ChamaLink.Infrastructure.Services.Sms.HttpSmsGateway>()
        : ActivatorUtilities.CreateInstance<ChamaLink.Infrastructure.Services.Sms.LogSmsGateway>(sp));
// SASISHO20 — msimamizi wa MFUMO (platform). Hana uwezo wa fedha: dashboard + msaada + break-glass (4-eyes).
builder.Services.AddScoped<ChamaLink.Infrastructure.Services.Platform.PlatformAuditService>();
builder.Services.AddScoped<ChamaLink.Infrastructure.Services.Platform.PlatformAuthService>();
builder.Services.AddScoped<ChamaLink.Infrastructure.Services.Platform.PlatformApprovalService>();
builder.Services.AddScoped<ChamaLink.Infrastructure.Services.Platform.PlatformGroupService>();
builder.Services.AddScoped<ChamaLink.Infrastructure.Services.Platform.PlatformSupportService>();
builder.Services.AddScoped<ChamaLink.Infrastructure.Services.Loans.LoanApplicationService>();
// Mnyororo wa hash wa kumbukumbu za ukaguzi (SASISHO13)
if (!builder.Environment.IsEnvironment("Testing"))
    builder.Services.AddHostedService<ChamaLink.Infrastructure.Services.Truth.AuditSealingBackgroundService>();
builder.Services.AddScoped<ChamaLink.Infrastructure.Services.Benefits.MemberStatusService>();
builder.Services.AddScoped<ChamaLink.Infrastructure.Services.RoleChangeService>();
builder.Services.AddScoped<ChamaLink.Infrastructure.Services.Truth.ResetRequestService>();
builder.Services.AddScoped<ChamaLink.Infrastructure.Services.Truth.StatementImportService>(); // SASISHO17 (P0-2): PDF kwa viongozi wawili // SASISHO17 (P0-6): kufuta kwa viongozi wawili   // SASISHO17: vyeo kwa pendekezo + kura
if (!builder.Environment.IsEnvironment("Testing"))
    builder.Services.AddHostedService<ChamaLink.Infrastructure.Services.Benefits.StatusSchedulerBackgroundService>(); // SASISHO17 (K)
if (!builder.Environment.IsEnvironment("Testing"))
    builder.Services.AddHostedService<ChamaLink.Infrastructure.Services.Sms.SmsDispatchBackgroundService>(); // SASISHO21: mnyororo wa SMS
// SASISHO18: faini ya deni la tukio + kulipa madeni kwa amana zilizopo (vikundi vya "kila tukio" pekee).
if (!builder.Environment.IsEnvironment("Testing"))
    builder.Services.AddHostedService<ChamaLink.Infrastructure.Services.Events.EventWalletBackgroundService>();

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
            // AllowCredentials: refresh token iko kwenye HttpOnly cookie (SASISHO15) — inahitaji origins maalum (si "*").
            policy.WithOrigins(corsOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
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
// UKUAJI (Ushauri §5): mitandao ya simu Tanzania inatumia CGNAT — maelfu ya watu hushiriki IP moja. Kwa hiyo kikomo
// cha jumla sasa kinahesabiwa kwa MTUMIAJI aliyeingia (userId kutoka JWT); IP inatumika tu kwa wasioingia.
// Middleware imewekwa BAADA ya UseAuthentication ili utambulisho ujulikane. Viwango vinatoka config (RateLimit:*).
var perUserLimit = builder.Configuration.GetValue<int?>("RateLimit:PerUserPerMinute") ?? 120;
var anonLimit = builder.Configuration.GetValue<int?>("RateLimit:AnonymousPerIpPerMinute") ?? 60;
var authLimit = builder.Configuration.GetValue<int?>("RateLimit:AuthPerIpPerMinute") ?? 5;
// SASISHO15: kuendeleza kikao (refresh/logout) kunatokea kila baada ya dakika chache na kila ukurasa unapofunguliwa —
// si jaribio la neno la siri, kwa hiyo kina kikomo chake (bado kwa IP) ili tabo kadhaa zisimfungie mtumiaji nje.
var sessionLimit = builder.Configuration.GetValue<int?>("RateLimit:SessionPerIpPerMinute") ?? 30;
var uploadLimit = builder.Configuration.GetValue<int?>("RateLimit:UploadsPerUserPerMinute") ?? 6; // SASISHO17 (P1-9)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        var uid = httpContext.User?.Identity?.IsAuthenticated == true
            ? httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? httpContext.User.FindFirst("sub")?.Value
            : null;
        var key = uid is not null ? "u:" + uid : "ip:" + (httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = uid is not null ? perUserLimit : anonLimit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });

    options.AddPolicy("session", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions { PermitLimit = sessionLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

    // SASISHO17 (P1-9): kupakia faili (PDF ya M-Koba, vyeti) ni gharama kubwa (parse/hash/usimbaji) — kikomo chake kwa mtumiaji
    options.AddPolicy("upload", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: "up:" + (httpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                                   ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
            factory: _ => new FixedWindowRateLimiterOptions { PermitLimit = uploadLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// ============================================================================
// SECURITY FIX (ukaguzi 2026-09-15, C-3): siri hazikubaliki tena kutoka faili.
//
// Zamani appsettings.json ilikuwa na JWT key na neno la siri la database
// zikiwa wazi, na zilikuwa zimeingia kwenye git tangu commit ya kwanza:
//     "Jwt:Key": <key ya sandbox iliyovuja — sasa iko kwenye orodha ya kukataa: ProductionGuard.LeakedSandboxJwtKey>
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
        IssuerSigningKey = new SymmetricSecurityKey(signingKey),
        // SASISHO15: access token ni ya dakika chache — ClockSkew ya dakika 5 (chaguo-msingi) ingeiongezea muda
        ClockSkew = TimeSpan.FromMinutes(1),
        // SASISHO16: algorithm moja tu (HS256), token lazima iwe na muda wa kuisha na sahihi
        ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
        RequireExpirationTime = true,
        RequireSignedTokens = true
    };
    // SASISHO16: kichwa cha WWW-Authenticate kisieleze sababu ("token expired at …", "signature invalid")
    options.IncludeErrorDetails = false;
    options.MapInboundClaims = true;
    // Vikao vilivyofutwa ("funga vikao vyote"): TokenVersion ya token lazima ilingane na ya mtumiaji sasa
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async ctx =>
        {
            var sub = ctx.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                      ?? ctx.Principal?.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
            var tv = ctx.Principal?.FindFirst(ChamaLink.Infrastructure.Services.AuthService.TokenVersionClaim)?.Value;
            if (!Guid.TryParse(sub, out var uid)) { ctx.Fail("sub"); return; }
            var db = ctx.HttpContext.RequestServices.GetRequiredService<ChamaLink.Infrastructure.ApplicationDbContext>();
            var current = await db.Users.AsNoTracking().Where(u => u.Id == uid).Select(u => (int?)u.TokenVersion).FirstOrDefaultAsync();
            if (current is null || (tv ?? "0") != current.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))
                ctx.Fail("Kikao kimefungwa");
        }
    };
});

// SASISHO15: usitangaze aina ya server (Kestrel) kwenye kila jibu
builder.WebHost.ConfigureKestrel(k => k.AddServerHeader = false);

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(o =>
    {
        // SASISHO16: makosa ya muundo wa ombi (JSON mbovu, aina isiyo sahihi) yalikuwa yanarudisha ujumbe wa .NET
        // ("The JSON value could not be converted to System.Guid. Path: $.x | LineNumber…") — unafichua aina za
        // ndani. Sasa: jina la sehemu tu + ujumbe wa jumla unaotafsirika, pamoja na correlationId.
        o.InvalidModelStateResponseFactory = ctx =>
        {
            var fields = ctx.ModelState.Where(kv => kv.Value is { Errors.Count: > 0 })
                .Select(kv => ChamaLink.API.Middleware.RequestValidation.FieldName(kv.Key))
                .Where(f => f.Length > 0).Distinct().Take(10).ToList();
            string msg = fields.Count > 0
                ? $"Ombi halina ukamilifu: angalia {string.Join(", ", fields)}."
                : "Ombi halina ukamilifu.";
            // Ujumbe wetu wenyewe wa DataAnnotations (Kiswahili) unarudi; wa framework unafichwa
            var own = ctx.ModelState.Values.SelectMany(v => v.Errors)
                .Select(ChamaLink.API.Middleware.RequestValidation.OwnMessage).FirstOrDefault(m => m is not null);
            if (own is not null) msg = own;
            var errors = ctx.ModelState.Where(kv => kv.Value is { Errors.Count: > 0 })
                .GroupBy(kv => ChamaLink.API.Middleware.RequestValidation.FieldName(kv.Key)).Where(g => g.Key.Length > 0).Take(10)
                .ToDictionary(g => g.Key, g => g.SelectMany(kv => kv.Value!.Errors)
                    .Select(e => ChamaLink.API.Middleware.RequestValidation.OwnMessage(e) ?? "Thamani si sahihi.").Distinct().ToArray());
            var body = new
            {
                type = "https://httpstatuses.io/400", title = "Ombi halina ukamilifu", status = 400,
                detail = msg, message = msg,
                template = own ?? (fields.Count > 0 ? "Ombi halina ukamilifu: angalia {0}." : "Ombi halina ukamilifu."),
                args = own is null && fields.Count > 0 ? new[] { string.Join(", ", fields) } : Array.Empty<string>(),
                errors, correlationId = ctx.HttpContext.TraceIdentifier
            };
            return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(body) { ContentTypes = { "application/problem+json" } };
        };
    });
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
    // Aina zenye jina moja kwenye controller tofauti (mf. VoteRequest) zisigongane
    c.CustomSchemaIds(t => (t.FullName ?? t.Name).Replace("+", "."));
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
    if (earlyKey.Length > 0 && earlyKey == ChamaLink.API.Security.ProductionGuard.LeakedSandboxJwtKey)
    {
        throw new InvalidOperationException(
            "Unatumia JWT key ILE ILE iliyo kwenye GitHub ya umma - mtu yeyote anaweza " +
            "kutengeneza token za uongo kwa key hiyo. Tengeneza key mpya: openssl rand -base64 64");
    }
}

var app = builder.Build();

// ============================================================================
// SASISHO20 — msimamizi wa KWANZA wa mfumo (bootstrap)
//
// Hii ni amri ya CONSOLE pekee: `dotnet run --project ChamaLink.API -- --seed-platform-admin`
// Inasoma PLATFORM_ADMIN_EMAIL / PLATFORM_ADMIN_PASSWORD / PLATFORM_ADMIN_NAME kutoka env.
// Hakuna neno la siri lililoandikwa kwenye code, na hakuna endpoint ya umma inayounda
// msimamizi wa kwanza (mtu mwenye console ya server ana uwezo wa kila kitu — hivyo hii
// si njia ya nyuma). Msimamizi huyo ataombwa kuwasha MFA akiingia mara ya kwanza.
// ============================================================================
if (args.Contains("--seed-platform-admin"))
{
    using var scope = app.Services.CreateScope();
    var seeder = scope.ServiceProvider.GetRequiredService<ChamaLink.Infrastructure.Services.Platform.PlatformAuthService>();
    var email = Environment.GetEnvironmentVariable("PLATFORM_ADMIN_EMAIL");
    var password = Environment.GetEnvironmentVariable("PLATFORM_ADMIN_PASSWORD");
    var name = Environment.GetEnvironmentVariable("PLATFORM_ADMIN_NAME") ?? "Msimamizi wa Mfumo";
    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
    {
        Console.WriteLine("Weka PLATFORM_ADMIN_EMAIL na PLATFORM_ADMIN_PASSWORD (angalau herufi 12).");
        return;
    }
    try
    {
        await seeder.SeedFirstAdminAsync(email, password, name);
        Console.WriteLine($"Msimamizi wa kwanza ameundwa: {email} (SuperAdmin).");
        Console.WriteLine("Ingia kwenye /admin — utaombwa kuwasha MFA (Google Authenticator / Authy).");
    }
    catch (Exception ex)
    {
        Console.WriteLine("Imeshindikana: " + ex.Message);
    }
    return;
}

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
    if (configuredJwtKey == ChamaLink.API.Security.ProductionGuard.LeakedSandboxJwtKey)
    {
        throw new InvalidOperationException(
            "Unatumia JWT key ILE ILE iliyo kwenye GitHub ya umma - mtu yeyote anaweza " +
            "kutengeneza token za uongo kwa key hiyo. Tengeneza key mpya: openssl rand -base64 64");
    }
}

// SASISHO17 (P1-5): usanidi hatari unakataliwa nje ya Development/Testing (API haianzi kabisa)
// SASISHO17 (P0-6): usanidi hatari → API inasimama kwa exit code 78 (EX_CONFIG) na sababu zote kwenye log (bila siri),
// badala ya ajali (SIGABRT/core dump). systemd/docker wanaona kosa la usanidi waziwazi.
try { ChamaLink.API.Security.ProductionGuard.EnforceOrThrow(app.Configuration, app.Environment, app.Logger); }
catch (InvalidOperationException ex) when (ex.Message.StartsWith("ProductionGuard:", StringComparison.Ordinal))
{
    Console.Error.WriteLine(ex.Message);
    Environment.ExitCode = 78;
    return;
}

// SECURITY FIX (audit 2.3/2.4): registered first so it wraps every other
// middleware/controller below it - it is the last line of defence for
// anything that isn't already handled by a controller's own try/catch.
app.UseMiddleware<ChamaLink.API.Middleware.SecurityHeadersMiddleware>();
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
// SASISHO16 (USALAMA): zamani orodha ZOTE mbili (KnownNetworks + KnownProxies) zilifutwa. Kwa ASP.NET Core,
// orodha tupu = X-Forwarded-For inakubaliwa kutoka KWA MTU YEYOTE: mshambulizi angebadilisha IP yake kila ombi
// na kukwepa kikomo cha login (5/dakika kwa IP). Sasa header hizi zinaaminiwa TU kutoka proxy zilizotajwa
// kwenye "Network:TrustedProxies" (IP) / "Network:TrustedNetworks" (CIDR). Zisipotajwa — hazitumiki kabisa.
{
    var trustedProxies = app.Configuration.GetSection("Network:TrustedProxies").Get<string[]>() ?? Array.Empty<string>();
    var trustedNetworks = app.Configuration.GetSection("Network:TrustedNetworks").Get<string[]>() ?? Array.Empty<string>();
    if (trustedProxies.Length + trustedNetworks.Length > 0)
    {
        var fwdOptions = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = 1
        };
        fwdOptions.KnownNetworks.Clear();
        fwdOptions.KnownProxies.Clear();
        foreach (var ip in trustedProxies) fwdOptions.KnownProxies.Add(System.Net.IPAddress.Parse(ip.Trim()));
        foreach (var cidr in trustedNetworks)
        {
            var parts = cidr.Trim().Split('/');
            fwdOptions.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(System.Net.IPAddress.Parse(parts[0]), int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture)));
        }
        app.UseForwardedHeaders(fwdOptions);
    }
}

app.UseHttpsRedirection();
app.UseCors("Frontend");
app.UseAuthentication(); // Must be before UseAuthorization
app.UseRateLimiter();     // baada ya Authentication: kikomo kwa kila mtumiaji (si kwa IP ya CGNAT)
app.UseAuthorization();
app.MapControllers();

app.Run();

// Jaribio (ukaguzi 2026-09-15): Program.cs inatumia "top-level statements",
// ambayo huzalisha class ya `Program` ya ndani (internal). WebApplicationFactory
// kwenye ChamaLink.Tests inahitaji kuirejelea ili kujenga API halisi na
// kuthibitisha kuwa kila controller inaweza kuundwa - angalia
// ChamaLink.Tests/ServiceRegistrationTests.cs.
public partial class Program { }
