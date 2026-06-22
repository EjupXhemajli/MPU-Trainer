using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Speech.Synthesis;
using System.Text.Json;
using System.Threading;
using MpuTrainer.Models;
using MpuTrainer.Services;
using NAudio.Wave;

namespace MpuTrainer.Audio;

/// <summary>Eine auswaehlbare Premium-Stimme (z. B. von ElevenLabs).</summary>
public record TtsVoice(string Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Alle Parameter fuer eine Sprachausgabe (erlaubt Test mit ungespeicherten Werten).</summary>
public record TtsOptions(
    TtsProvider Provider,
    string? WindowsVoice,
    int VolumePercent,
    string? ApiKey,
    string? OpenAiVoice,
    string? OpenAiModel,
    string? ElevenLabsVoiceId,
    string? ElevenLabsModel);

public interface ITtsService
{
    /// <summary>Namen der installierten Windows-Stimmen (SAPI5).</summary>
    List<string> GetWindowsVoices();

    /// <summary>Feste Stimmenliste fuer OpenAI-TTS.</summary>
    IReadOnlyList<string> OpenAiVoices { get; }

    /// <summary>Laedt die im ElevenLabs-Konto verfuegbaren Stimmen.</summary>
    Task<IReadOnlyList<TtsVoice>> GetElevenLabsVoicesAsync(string apiKey, CancellationToken ct = default);

    /// <summary>Erzeugt eine WAV-Datei anhand der gespeicherten Einstellungen.</summary>
    Task SpeakToWaveFileAsync(string text, string outputWavPath, CancellationToken ct = default);

    /// <summary>Erzeugt eine WAV-Datei mit ausdruecklich uebergebenen Optionen.</summary>
    Task SpeakToWaveFileAsync(string text, string outputWavPath, TtsOptions options, CancellationToken ct = default);
}

/// <summary>
/// Sprachausgabe mit drei Anbietern: Windows (lokal, SAPI5), OpenAI und
/// ElevenLabs (beide Premium ueber API-Key). Unabhaengig vom Anbieter wird
/// immer eine WAV-Datei erzeugt, damit Wiedergabe und MP3-Bündelung
/// einheitlich bleiben (ElevenLabs-MP3 wird intern nach WAV gewandelt).
/// </summary>
public class TtsService : ITtsService
{
    private readonly ISettingsService _settings;
    private readonly ISecretStore _secrets;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(120) };

    // Mit tts-1-hd kompatible Standardstimmen.
    private static readonly string[] OpenAiVoiceNames =
        { "alloy", "echo", "fable", "nova", "onyx", "shimmer" };

    public TtsService(ISettingsService settings, ISecretStore secrets)
    {
        _settings = settings;
        _secrets = secrets;
    }

    public IReadOnlyList<string> OpenAiVoices => OpenAiVoiceNames;

    public List<string> GetWindowsVoices()
    {
        var names = new List<string>();
        try
        {
            using var synth = new SpeechSynthesizer();
            foreach (var v in synth.GetInstalledVoices())
                if (v.Enabled) names.Add(v.VoiceInfo.Name);
        }
        catch { /* keine Stimmen verfuegbar */ }
        return names;
    }

    public async Task<IReadOnlyList<TtsVoice>> GetElevenLabsVoicesAsync(string apiKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Bitte zuerst den ElevenLabs-API-Key eingeben.");

        using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.elevenlabs.io/v1/voices");
        req.Headers.Add("xi-api-key", apiKey);

        using var resp = await Http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"ElevenLabs-Fehler ({(int)resp.StatusCode}): {Trim(body)}");

        var list = new List<TtsVoice>();
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("voices", out var voices) &&
            voices.ValueKind == JsonValueKind.Array)
        {
            foreach (var v in voices.EnumerateArray())
            {
                var id = v.TryGetProperty("voice_id", out var vid) ? vid.GetString() : null;
                if (string.IsNullOrEmpty(id)) continue;
                var name = v.TryGetProperty("name", out var n) ? n.GetString() ?? id : id;
                list.Add(new TtsVoice(id, name));
            }
        }
        return list;
    }

    public Task SpeakToWaveFileAsync(string text, string outputWavPath, CancellationToken ct = default)
        => SpeakToWaveFileAsync(text, outputWavPath, OptionsFromSettings(), ct);

    public async Task SpeakToWaveFileAsync(string text, string outputWavPath, TtsOptions opt, CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(outputWavPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        switch (opt.Provider)
        {
            case TtsProvider.OpenAI:
                await OpenAiToWavAsync(text, outputWavPath, opt, ct);
                break;
            case TtsProvider.ElevenLabs:
                await ElevenLabsToWavAsync(text, outputWavPath, opt, ct);
                break;
            default:
                await WindowsToWavAsync(text, outputWavPath, opt);
                break;
        }
    }

    private TtsOptions OptionsFromSettings()
    {
        var s = _settings.Current;
        return new TtsOptions(
            s.TtsProvider,
            s.VoiceName,
            (int)Math.Round(s.Volume * 100),
            _secrets.Load("tts"),
            s.OpenAiTtsVoice,
            s.OpenAiTtsModel,
            s.ElevenLabsVoiceId,
            s.ElevenLabsModel);
    }

    // ---- Windows (lokal) ----

    private static Task WindowsToWavAsync(string text, string outputWavPath, TtsOptions opt)
    {
        return Task.Run(() =>
        {
            using var synth = new SpeechSynthesizer();
            if (!string.IsNullOrWhiteSpace(opt.WindowsVoice))
            {
                try { synth.SelectVoice(opt.WindowsVoice); }
                catch { /* faellt auf Standardstimme zurueck */ }
            }
            synth.Volume = Math.Clamp(opt.VolumePercent, 0, 100);
            synth.SetOutputToWaveFile(outputWavPath);
            synth.Speak(text ?? string.Empty);
            synth.SetOutputToNull();
        });
    }

    // ---- OpenAI (Premium) ----

    private async Task OpenAiToWavAsync(string text, string outputWavPath, TtsOptions opt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(opt.ApiKey))
            throw new InvalidOperationException("Bitte den OpenAI-API-Key in den Einstellungen eintragen.");

        var payload = new
        {
            model = string.IsNullOrWhiteSpace(opt.OpenAiModel) ? "tts-1-hd" : opt.OpenAiModel,
            voice = string.IsNullOrWhiteSpace(opt.OpenAiVoice) ? "alloy" : opt.OpenAiVoice,
            input = text ?? string.Empty,
            response_format = "wav"
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/audio/speech")
        {
            Content = JsonContent.Create(payload)
        };
        req.Headers.Add("Authorization", $"Bearer {opt.ApiKey}");

        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"OpenAI-TTS-Fehler ({(int)resp.StatusCode}): {Trim(err)}");
        }

        await using var fs = File.Create(outputWavPath);
        await resp.Content.CopyToAsync(fs, ct);
    }

    // ---- ElevenLabs (Premium) ----

    private async Task ElevenLabsToWavAsync(string text, string outputWavPath, TtsOptions opt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(opt.ApiKey))
            throw new InvalidOperationException("Bitte den ElevenLabs-API-Key in den Einstellungen eintragen.");
        if (string.IsNullOrWhiteSpace(opt.ElevenLabsVoiceId))
            throw new InvalidOperationException("Bitte zuerst eine ElevenLabs-Stimme laden und auswaehlen.");

        var url = $"https://api.elevenlabs.io/v1/text-to-speech/{opt.ElevenLabsVoiceId}?output_format=mp3_44100_128";
        var payload = new
        {
            text = text ?? string.Empty,
            model_id = string.IsNullOrWhiteSpace(opt.ElevenLabsModel) ? "eleven_multilingual_v2" : opt.ElevenLabsModel
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(payload) };
        req.Headers.Add("xi-api-key", opt.ApiKey);

        using var resp = await Http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"ElevenLabs-Fehler ({(int)resp.StatusCode}): {Trim(err)}");
        }

        var mp3 = await resp.Content.ReadAsByteArrayAsync(ct);

        // MP3 -> WAV wandeln, damit Wiedergabe und MP3-Bündelung einheitlich bleiben.
        await Task.Run(() =>
        {
            using var ms = new MemoryStream(mp3);
            using var reader = new Mp3FileReader(ms);
            WaveFileWriter.CreateWaveFile(outputWavPath, reader);
        }, ct);
    }

    private static string Trim(string s) => s.Length > 300 ? s[..300] + " ..." : s;
}
