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
        var payload = new
        {
            model = _model,
            max_tokens = maxTokens,
            temperature,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add("Authorization", $"Bearer {_apiKey}");

        using var response = await HttpClientHolder.Client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"API-Fehler ({(int)response.StatusCode}): {Trim(body)}");

        using var doc = JsonDocument.Parse(body);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();
        return (content ?? string.Empty).Trim();
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
