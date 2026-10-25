using ChamaLink.Domain.Exceptions;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.API.Middleware;

// SECURITY FIX (audit 2.3/2.4: "catch (Exception) inatumika kila mahali" /
// "Hakuna global exception middleware").
//
// UPDATED (ukaguzi 2026-09-15): controllers HAZINA tena `catch (Exception)`.
// Zamani kila controller ilikuwa na `catch (Exception) -> 400`, na hii ilikuwa
// inafanya mambo mawili mabaya: (1) middleware hii haikufikiwa kabisa kwa
// makosa yaliyokuwa yakikamatwa kwanza, na (2) kosa lolote la server
// (NullReference, database, DI imefeli) lilionekana kama "400 Bad Request" -
// yaani mtumiaji alilaumiwa kwa kosa la mfumo. Ndio maana bug ya
// ReportsController nzima (endpoints 16 zilizokuwa zikishindwa kwa sababu ya
// DI) ilijificha nyuma ya 400 badala ya kuonekana kama tatizo la server.
//
// Sasa hii ndiyo njia MOJA ya kubadilisha exception kuwa HTTP response:
// makosa ya biashara (AppException + watoto wake) yanapata status sahihi na
// ujumbe ulioandikwa kwa ajili ya mtumiaji; kitu kingine chochote
// kinaangukia 500 bila maelezo ya ndani.
//
// Every response from here is a standard ProblemDetails-shaped JSON body
// plus an X-Correlation-Id header. The correlation ID is what ties a
// user's bug report ("nilipata error saa 10:03") to the specific log line
// with the full exception and stack trace - the client never sees that
// detail, only the ID.
public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private readonly bool _includeDetails;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger, IHostEnvironment env, IConfiguration config)
    {
        _next = next;
        _logger = logger;
        // SASISHO16: stack trace HAIENDI kwa mteja kamwe kwa chaguo-msingi — hata Development (mashine ya
        // mtengenezaji mara nyingi ndiyo inayotumika kama server ya majaribio kwenye mtandao). Iwashwe kwa
        // makusudi tu: Development + "Errors:IncludeExceptionDetails": true. Logi ya server ina kila kitu.
        _includeDetails = env.IsDevelopment() && config.GetValue<bool>("Errors:IncludeExceptionDetails");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.TraceIdentifier;

        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex, correlationId);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception ex, string correlationId)
    {
        // If the response has already started streaming (e.g. partway
        // through a large report), we cannot change the status code or
        // body anymore - just log and stop, rather than throwing a
        // second "headers already sent" exception on top of the first.
        if (context.Response.HasStarted)
        {
            _logger.LogError(ex, "Unhandled exception after response had already started. CorrelationId={CorrelationId}", correlationId);
            return;
        }

        var (statusCode, title, publicMessage) = MapException(ex);

        // Full exception detail (type, message, stack trace) always goes
        // to the server-side structured log, tagged with the correlation
        // ID, the path, and the authenticated user if there is one -
        // never to the client.
        // SASISHO16: kitambulisho cha mtumiaji (claim) — Identity.Name ilikuwa tupu (logi zilionyesha User=(null))
        var who = context.User?.Identity?.IsAuthenticated == true
            ? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? context.User.FindFirst("sub")?.Value ?? "authenticated"
            : "anonymous";
        if (statusCode >= 500)
            _logger.LogError(ex,
                "Unhandled exception. CorrelationId={CorrelationId} Path={Path} Method={Method} User={User} StatusCode={StatusCode}",
                correlationId, context.Request.Path, context.Request.Method, who, statusCode);
        else // SASISHO15: makosa ya mteja (4xx) ni sehemu ya kawaida ya mtiririko — Warning bila stack trace (logi zisijae kelele)
            _logger.LogWarning(
                "Request rejected. CorrelationId={CorrelationId} Path={Path} Method={Method} User={User} StatusCode={StatusCode} Error={ErrorType}: {ErrorMessage}",
                correlationId, context.Request.Path, context.Request.Method, who, statusCode, ex.GetType().Name, ex.Message);

        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = statusCode;

        var problem = new
        {
            type = $"https://httpstatuses.io/{statusCode}",
            title,
            status = statusCode,
            detail = publicMessage,
            // COMPATIBILITY (ukaguzi 2026-09-15): controllers zilikuwa
            // zinarudisha `{ message = ex.Message }`, na frontend inasoma
            // `err.response.data.message` kwenye faili 7 (MkobaPage.tsx,
            // MkobaUpload.tsx, n.k.). Kwa kuwa sasa makosa yote yanapita
            // hapa, tunaweka `message` pamoja na `detail` ya RFC 7807 ili
            // frontend isivunjike. `detail` ndiyo ya kawaida; `message` ni
            // kwa ajili ya urithi wa code iliyopo.
            message = publicMessage,
            // SASISHO14: kiolezo + vigeu -> frontend inatafsiri (EN haionyeshi Kiswahili)
            template = ex is AppException ae1 && statusCode < 500 ? ae1.Template : null,
            args = ex is AppException ae2 && statusCode < 500 ? ae2.Args : null,
            details = ex is AppException ae3 && statusCode < 500 && ae3.Details.Count > 0 ? ae3.Details : null,
            correlationId,
            // Stack traces and raw exception messages are only ever
            // included outside the "detail" field, and only in
            // Development - never in a deployed/production environment.
            debug = _includeDetails ? ex.ToString() : null
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        }));
    }

    // Maps known exception types to a status code and a SAFE, generic
    // client-facing message. Anything not recognised falls through to a
    // plain 500 with no detail beyond "server error" - we never forward
    // ex.Message for unrecognised exceptions, since that is exactly how
    // database connection strings, table/column names, and file paths
    // used to leak to clients via the old catch(Exception) pattern.
    /// <summary>
    /// SASISHO16: ujumbe wa exception za mfumo (ArgumentException, KeyNotFoundException, UnauthorizedAccessException)
    /// unasambazwa TU kama ziliandikwa na code ya ChamaLink yenyewe (ujumbe wa makusudi kwa mtumiaji). Zikitoka
    /// ndani ya .NET (Dictionary, Enum.Parse, Guid.Parse, ThrowHelper…) ujumbe wake una majina ya ndani
    /// (aina za .NET, majina ya vigezo, funguo) — mteja anapata ujumbe wa jumla.
    /// </summary>
    internal static bool IsOwnMessage(Exception ex)
    {
        if (ex is ArgumentException { ParamName: not null }) return false; // "(Parameter 'x')" = maelezo ya ndani
        var ns = ex.TargetSite?.DeclaringType?.Namespace;
        return ns is not null && ns.StartsWith("ChamaLink", StringComparison.Ordinal);
    }

    internal static (int StatusCode, string Title, string Message) MapException(Exception ex) => ex switch
    {
        // FIX (ukaguzi 2026-09-15, H-4): hii ilikuwa 401. Kisemantiki
        // 401 = "sijui wewe ni nani", 403 = "nakujua, lakini huna ruhusa".
        // GroupAuthorizationService hutoa UnauthorizedAccessException kwa
        // mtu ambaye AMETHIBITISHWA tayari (ana token halali) lakini si
        // mwanachama / hana cheo - hiyo ni 403.
        //
        // Athari iliyokuwepo: chamalink-web-new/src/api/client.ts (mstari 27)
        // humrudisha mtumiaji kwenye login screen kwa 401 yoyote. Kwa hiyo
        // mwanachama anayejaribu kitu asichoruhusiwa alikuwa anafukuzwa
        // kwenye mfumo mzima badala ya kuona "huna ruhusa". Pia ilikuwa
        // inachanganya takwimu za usalama (401 nyingi = kama mashambulizi
        // ya nenosiri, ilhali ni wanachama tu wanapiga endpoints).
        UnauthorizedAccessException => (
            (int)HttpStatusCode.Forbidden,
            "Huna ruhusa",
            IsOwnMessage(ex) ? ex.Message : "Huna ruhusa ya kufanya hili."),

        KeyNotFoundException => (
            (int)HttpStatusCode.NotFound,
            "Haikupatikana",
            IsOwnMessage(ex) ? ex.Message : "Haikupatikana."),

        // SASISHO16: mwili wa ombi mkubwa mno / mbovu (Kestrel) — si 500
        Microsoft.AspNetCore.Http.BadHttpRequestException bad => (
            bad.StatusCode is >= 400 and < 500 ? bad.StatusCode : (int)HttpStatusCode.BadRequest,
            "Ombi halikubaliki",
            bad.StatusCode == StatusCodes.Status413PayloadTooLarge ? "Faili au ombi ni kubwa mno." : "Ombi halikubaliki."),

        // NEW (ukaguzi 2026-09-15, M-7): makosa ya biashara sasa yana aina
        // maalum (ChamaLink.Domain.Exceptions) badala ya `throw new Exception`.
        // Hii ndiyo ramani yao. Kila mmoja unabeba ujumbe ulioandikwa kwa
        // makusudi kuonyeshwa kwa mtumiaji, kwa hiyo ni salama kuusambaza.
        UnauthorizedException => (
            (int)HttpStatusCode.Unauthorized,
            "Kikao kimeisha",
            ex.Message),

        ForbiddenException => (
            (int)HttpStatusCode.Forbidden,
            "Huna ruhusa",
            ex.Message),

        NotFoundException => (
            (int)HttpStatusCode.NotFound,
            "Haikupatikana",
            ex.Message),

        ConflictException => (
            (int)HttpStatusCode.Conflict,
            "Mgongano wa data",
            ex.Message),

        ValidationException => (
            (int)HttpStatusCode.BadRequest,
            "Ombi halina ukamilifu",
            ex.Message),

        // ArgumentException bado inaruhusiwa kwa sababu baadhi ya code ya
        // zamani (na Enum.Parse, Convert, n.k.) inaitoa. Lakini
        // InvalidOperationException IMEONDOLEWA hapa - ndiyo exception ambayo
        // .NET DI inaitoa wakati service haijasajiliwa, na kuionyeshwa kama
        // "400 Bad Request" ndiko kulikuficha bug ya ReportsController nzima
        // (endpoints 16 zilikuwa zikishindwa kwa sababu ya DI, zikionekana
        // kama makosa ya mtumiaji). Sasa kosa la aina hiyo linaangukia 500
        // mahali pake sahihi.
        ArgumentException => (
            (int)HttpStatusCode.BadRequest,
            "Ombi halina ukamilifu",
            IsOwnMessage(ex) ? ex.Message : "Ombi halina ukamilifu: thamani mojawapo si sahihi."),

        // Injini ya Ukweli: kikundi hakijaweka mipangilio -> ujumbe wa kueleweka (si 500)
        PolicyNotConfiguredException => (
            (int)HttpStatusCode.Conflict,
            "Mipangilio haijakamilika",
            ex.Message),

        DbUpdateConcurrencyException => (
            (int)HttpStatusCode.Conflict,
            "Mgongano wa data",
            "Rekodi hii imebadilishwa na mtu/mchakato mwingine kabla yako. Tafadhali pakia upya na ujaribu tena."),

        DbUpdateException => (
            (int)HttpStatusCode.Conflict,
            "Hitilafu ya database",
            "Operesheni hii imeshindikana kwa sababu ya mgongano wa data (mfano: taarifa inayofanana tayari ipo)."),

        TaskCanceledException or OperationCanceledException => (
            499, // client closed request - non-standard but widely used
            "Ombi limesitishwa",
            "Ombi lilisitishwa kabla halijakamilika."),

        _ => (
            (int)HttpStatusCode.InternalServerError,
            "Hitilafu ya ndani ya mfumo",
            "Kuna hitilafu isiyotarajiwa upande wa server. Timu ya kiufundi imepokea taarifa hii; tafadhali jaribu tena baadaye.")
    };
}
