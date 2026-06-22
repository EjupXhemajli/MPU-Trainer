using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;

namespace MpuTrainer.AI;

public interface ITranscriptionService
{
    /// <summary>Wandelt eine Audioaufnahme (WAV) in Text um (Sprache wird automatisch erkannt).</summary>
    Task<string> TranscribeAsync(string audioPath, string apiKey, CancellationToken ct = default);
}

/// <summary>
/// Transkribiert Aufnahmen ueber die OpenAI-Whisper-API. Liefert reinen Text,
/// der anschliessend fachlich ausgewertet werden kann. Die Sprache muss nicht
/// angegeben werden – Whisper erkennt sie selbst (wichtig fuer die vielen
/// unterstuetzten Sprachen).
/// </summary>
public class OpenAiTranscriptionService : ITranscriptionService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(180) };

    public async Task<string> TranscribeAsync(string audioPath, string apiKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Kein OpenAI-Key fuer die Transkription hinterlegt.");
        if (!File.Exists(audioPath))
            throw new FileNotFoundException("Aufnahme nicht gefunden.", audioPath);

        var bytes = await File.ReadAllBytesAsync(audioPath, ct);

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(file, "file", "antwort.wav");
        form.Add(new StringContent("whisper-1"), "model");
        form.Add(new StringContent("text"), "response_format");

        using var req = new HttpRequestMessage(HttpMethod.Post,
            "https://api.openai.com/v1/audio/transcriptions") { Content = form };
        req.Headers.Add("Authorization", $"Bearer {apiKey}");

        using var resp = await Http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Transkriptions-Fehler ({(int)resp.StatusCode}): {Trim(body)}");

        // response_format=text liefert reinen Text; zur Sicherheit auch JSON abfangen.
        var text = body.Trim();
        if (text.StartsWith("{"))
        {
            try
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("text", out var t))
                    text = t.GetString() ?? string.Empty;
            }
            catch { /* war doch reiner Text */ }
        }

        return text.Trim();
    }

    private static string Trim(string s) => s.Length > 300 ? s[..300] + " ..." : s;
}
