namespace ChamaLink.API.Middleware;

/// <summary>
/// SASISHO15 (F-L5): vichwa vya usalama kwenye KILA jibu (hata makosa):
///   nosniff (browser isikisie aina ya faili — muhimu kwa vyeti vinavyopakuliwa), kukataa kuwekwa ndani ya iframe
///   (clickjacking), hakuna Referer nje, hakuna kamera/kipaza sauti/eneo, na CSP kali kwa majibu ya API (JSON).
///   Majibu ya /api hayahifadhiwi na proxy/browser (taarifa za fedha). HSTS inaongezwa kwenye HTTPS tu.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public Task Invoke(HttpContext ctx)
    {
        ctx.Response.OnStarting(() =>
        {
            var h = ctx.Response.Headers;
            h["X-Content-Type-Options"] = "nosniff";
            h["X-Frame-Options"] = "DENY";
            h["Referrer-Policy"] = "no-referrer";
            h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
            h["Cross-Origin-Opener-Policy"] = "same-origin";
            // SASISHO16: majibu ya API yasisomwe na tovuti za nje kama rasilimali (Spectre/XS-Leaks)
            h["Cross-Origin-Resource-Policy"] = "same-site";
            h.Remove("Server");
            h.Remove("X-Powered-By");
            var path = ctx.Request.Path;
            if (!path.StartsWithSegments("/swagger"))
                h["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
            if (path.StartsWithSegments("/api") && !h.ContainsKey("Cache-Control"))
                h["Cache-Control"] = "no-store";
            if (ctx.Request.IsHttps)
                h["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
            return Task.CompletedTask;
        });
        return _next(ctx);
    }
}
