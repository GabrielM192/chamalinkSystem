using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ChamaLink.Infrastructure.Services.Sms;

/// <summary>Matokeo ya kutuma SMS moja kwa mtoa-huduma.</summary>
public sealed record SmsSendResult(bool Success, string? Reference, string? Failure)
{
    public static SmsSendResult Ok(string? reference) => new(true, reference, null);
    public static SmsSendResult Fail(string reason) => new(false, null, reason);
}

/// <summary>
/// Lango la kutuma SMS. Mtoa-huduma HALITAJWI kwenye msimbo wala kwenye
/// nyaraka kwa sababu kikundi kinaweza kubadilisha kampuni yoyote.
/// Mabadiliko yanafanyika kwenye appsettings.json pekee.
/// </summary>
public interface ISmsGateway
{
    string Name { get; }
    Task<SmsSendResult> SendAsync(string phone, string body, CancellationToken ct = default);
}

/// <summary>
/// Lango la majaribio/maendeleo: linarekodi ujumbe kwenye kumbukumbu ya mfumo tu.
/// Hii ni chaguo-msingi ili kutuma SMS kwa bahati mbaya kutoka kompyuta ya
/// maendeleo lisitokee kamwe.
/// </summary>
public sealed class LogSmsGateway : ISmsGateway
{
    private readonly ILogger<LogSmsGateway> _log;
    public LogSmsGateway(ILogger<LogSmsGateway> log) => _log = log;

    public string Name => "Log";
    public Task<SmsSendResult> SendAsync(string phone, string body, CancellationToken ct = default)
    {
        _log.LogInformation("[SMS] -> {Phone}: {Body}", Mask(phone), body);
        return Task.FromResult(SmsSendResult.Ok($"LOG-{Guid.NewGuid():N}"[..20]));
    }

    internal static string Mask(string phone)
        => string.IsNullOrWhiteSpace(phone) || phone.Length < 6 ? phone : string.Concat(phone.AsSpan(0, 4), "****", phone.AsSpan(phone.Length - 3));
}

/// <summary>
/// Lango la HTTP: linaunganisha mtoa-huduma YEYOTE kwa URL na ufunguo.
///
///    Sms:Gateway   = "Http"
///    Sms:BaseUrl   = "https://<mtoa-huduma>/api/send"     (anwani ya mtoa-huduma)
///    Sms:ApiKey    = "<ufunguo>"                          (huwekwa kwenye env var, si git)
///    Sms:SenderId  = "J3FOREVER"                          (husajiliwa kwa mtoa-huduma)
///
/// Mwili wa ombi: { "to": "2557...", "senderId": "...", "message": "..." }
/// Jibu linatarajiwa kuwa na "id" au "messageId"; vinginevyo ombi linachukuliwa kuwa limekwenda.
/// </summary>
public sealed class HttpSmsGateway : ISmsGateway
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    public HttpSmsGateway(HttpClient http, IConfiguration config)
    {
        _http = http;
        _config = config;
    }

    public string Name => "Http";

    public async Task<SmsSendResult> SendAsync(string phone, string body, CancellationToken ct = default)
    {
        var url = _config["Sms:BaseUrl"];
        if (string.IsNullOrWhiteSpace(url)) return SmsSendResult.Fail("Mtoa-huduma wa SMS hajawekwa (Sms:BaseUrl).");

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new
            {
                to = phone,
                senderId = _config["Sms:SenderId"],
                message = body
            })
        };
        var key = _config["Sms:ApiKey"];
        if (!string.IsNullOrWhiteSpace(key)) req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {key}");

        try
        {
            using var resp = await _http.SendAsync(req, ct);
            var text = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return SmsSendResult.Fail($"Mtoa-huduma alikataa ({(int)resp.StatusCode}): {Clip(text)}");

            string? reference = null;
            try
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    foreach (var k in new[] { "id", "messageId", "message_id", "reference" })
                        if (doc.RootElement.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String)
                        {
                            reference = v.GetString();
                            break;
                        }
            }
            catch (JsonException) { /* jibu si JSON — tunatumia reference yetu */ }

            return SmsSendResult.Ok(reference ?? $"HTTP-{Guid.NewGuid():N}"[..20]);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return SmsSendResult.Fail($"Mawasiliano na mtoa-huduma yameshindikana: {Clip(ex.Message)}");
        }
    }

    private static string Clip(string s) => s.Length <= 160 ? s : s[..160];
}
