using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;

namespace MpuTrainer.AI;

public interface IAiClient
{
    /// <summary>Sendet System- und Nutzerprompt und liefert den Antworttext.</summary>
    Task<string> CompleteAsync(string systemPrompt, string userPrompt,
                               double temperature, int maxTokens, CancellationToken ct = default);

    /// <summary>Prueft die Verbindung mit einer minimalen Anfrage.</summary>
    Task<(bool ok, string message)> TestConnectionAsync(CancellationToken ct = default);
}

/// <summary>Gemeinsamer HttpClient (Wiederverwendung verhindert Socket-Erschoepfung).</summary>
internal static class HttpClientHolder
{
    public static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(120)
    };
}

/// <summary>KI-Client fuer die Anthropic-Messages-API.</summary>
public class AnthropicClient : IAiClient
{
    private readonly string _apiKey;
    private readonly string _model;
    private readonly string _baseUrl;

    public AnthropicClient(string apiKey, string model, string? baseUrl)
    {
        _apiKey = apiKey;
        _model = model;
        _baseUrl = string.IsNullOrWhiteSpace(baseUrl)
            ? "https://api.anthropic.com"
            : baseUrl.TrimEnd('/');
    }

    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt,
        double temperature, int maxTokens, CancellationToken ct = default)
    {
        var payload = new
        {
            model = _model,
            max_tokens = maxTokens,
            temperature,
            system = systemPrompt,
            messages = new[]
            {
                new { role = "user", content = userPrompt }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/messages")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add("x-api-key", _apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");

        using var response = await HttpClientHolder.Client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Anthropic-Fehler ({(int)response.StatusCode}): {Trim(body)}");

        return ExtractAnthropicText(body);
    }

    private static string ExtractAnthropicText(string body)
    {
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("content", out var content) &&
            content.ValueKind == JsonValueKind.Array)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var block in content.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var type) &&
                    type.GetString() == "text" &&
                    block.TryGetProperty("text", out var text))
                {
                    sb.Append(text.GetString());
                }
            }
            return sb.ToString().Trim();
        }
        return string.Empty;
    }

    public async Task<(bool ok, string message)> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            var answer = await CompleteAsync(
                "Antworte knapp.", "Antworte mit dem Wort: OK", 0.0, 16, ct);
            return (true, $"Verbindung erfolgreich. Antwort: {Trim(answer)}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static string Trim(string s) =>
        s.Length > 300 ? s[..300] + " ..." : s;
}

/// <summary>KI-Client fuer OpenAI-kompatible Chat-Completions-APIs.</summary>
public class OpenAiCompatibleClient : IAiClient
{
    private readonly string _apiKey;
    private readonly string _model;
    private readonly string _baseUrl;

    public OpenAiCompatibleClient(string apiKey, string model, string? baseUrl)
    {
        _apiKey = apiKey;
        _model = model;
        _baseUrl = string.IsNullOrWhiteSpace(baseUrl)
            ? "https://api.openai.com/v1"
            : baseUrl.TrimEnd('/');
    }

    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt,
        double temperature, int maxTokens, CancellationToken ct = default)
    {
        // Neuere OpenAI-Modelle (z. B. gpt-5.x und die o-Reihe) verlangen "max_completion_tokens"
        // statt "max_tokens" und erlauben teils keine abweichende Temperatur. Aeltere bzw. lokale
        // Anbieter (Ollama, LM Studio) kennen wiederum nur "max_tokens". Deshalb wird die Anfrage bei
        // einem 400-Fehler "unsupported_parameter" automatisch angepasst und erneut gesendet.
        var payload = new Dictionary<string, object?>
        {
            ["model"] = _model,
            ["max_completion_tokens"] = maxTokens,
            ["temperature"] = temperature,
            ["messages"] = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        };

        var adapted = new HashSet<string>();

        for (int attempt = 0; attempt < 5; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions")
            {
                Content = JsonContent.Create(payload)
            };
            request.Headers.Add("Authorization", $"Bearer {_apiKey}");

            using var response = await HttpClientHolder.Client.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(body);
                var content = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();
                return (content ?? string.Empty).Trim();
            }

            // Nicht unterstuetzter Parameter -> anpassen und erneut versuchen.
            if ((int)response.StatusCode == 400 && TryAdaptPayload(payload, body, adapted))
                continue;

            throw new InvalidOperationException($"API-Fehler ({(int)response.StatusCode}): {Trim(body)}");
        }

        throw new InvalidOperationException(
            "Die KI-Anfrage wurde auch nach automatischer Parameteranpassung abgelehnt. " +
            "Bitte Modellnamen und Anbieter pruefen.");
    }

    /// <summary>
    /// Passt die Anfrage an, wenn der Anbieter einen Parameter ablehnt: tauscht "max_tokens" und
    /// "max_completion_tokens" gegeneinander oder entfernt eine nicht erlaubte "temperature".
    /// Jeder Parameter wird hoechstens einmal angepasst (kein endloses Wiederholen).
    /// </summary>
    private static bool TryAdaptPayload(Dictionary<string, object?> payload, string body, HashSet<string> adapted)
    {
        string? param = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                if (err.TryGetProperty("param", out var p) && p.ValueKind == JsonValueKind.String)
                    param = p.GetString();

                // Manche Anbieter nennen den Parameter nur im Fehlertext.
                if (string.IsNullOrEmpty(param) &&
                    err.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                {
                    var msg = m.GetString() ?? string.Empty;
                    if (msg.Contains("max_completion_tokens")) param = "max_completion_tokens";
                    else if (msg.Contains("max_tokens")) param = "max_tokens";
                    else if (msg.Contains("temperature")) param = "temperature";
                }
            }
        }
        catch { return false; }

        if (string.IsNullOrEmpty(param) || !adapted.Add(param))
            return false;

        switch (param)
        {
            case "max_tokens":
                if (payload.Remove("max_tokens", out var v1)) { payload["max_completion_tokens"] = v1; return true; }
                return false;

            case "max_completion_tokens":
                if (payload.Remove("max_completion_tokens", out var v2)) { payload["max_tokens"] = v2; return true; }
                return false;

            case "temperature":
                return payload.Remove("temperature");

            default:
                return false;
        }
    }

    public async Task<(bool ok, string message)> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            // Groesseres Budget, damit auch Reasoning-Modelle (gpt-5.x) sichtbaren Text zurueckgeben.
            var answer = await CompleteAsync(
                "Antworte knapp.", "Antworte mit dem Wort: OK", 0.0, 256, ct);
            return (true, $"Verbindung erfolgreich. Antwort: {Trim(answer)}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static string Trim(string s) =>
        s.Length > 300 ? s[..300] + " ..." : s;
}
