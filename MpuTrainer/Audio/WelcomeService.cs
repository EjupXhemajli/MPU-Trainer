using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MpuTrainer.Models;
using MpuTrainer.Services;
using NAudio.Lame;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MpuTrainer.Audio;

public interface IWelcomeService
{
    /// <summary>Spielt (falls aktiviert) das Start-Intro ab: Stimme + dezente Corporate-Musik.</summary>
    Task PlayAsync();
}

/// <summary>
/// Start-Intro: Eine professionelle Sprachausgabe ("Willkommen bei der B.F.K.") wird ueber ein
/// mitgeliefertes, dezentes Corporate-Musikbett gemischt (Musik laeuft leiser unter der Stimme,
/// weiches Ein-/Ausblenden und Ducking). Das Ergebnis wird als MP3 (44,1 kHz, 192 kbps) erzeugt,
/// zwischengespeichert und abgespielt. Bei OpenAI wird gezielt die seriöse Männerstimme "onyx"
/// verwendet. Faellt eine Sprachausgabe aus, ertoent nur das Musikbett. Fehler sind unkritisch
/// und blockieren den Start nie.
/// </summary>
public class WelcomeService : IWelcomeService
{
    private const int Rate = 44100;
    private const string WelcomeText = "Willkommen bei der B F K";

    private readonly ITtsService _tts;
    private readonly IAudioPlayer _player;
    private readonly ISettingsService _settings;
    private readonly ISecretStore _secrets;

    public WelcomeService(ITtsService tts, IAudioPlayer player, ISettingsService settings, ISecretStore secrets)
    {
        _tts = tts;
        _player = player;
        _settings = settings;
        _secrets = secrets;
    }

    public async Task PlayAsync()
    {
        var s = _settings.Current;
        if (!s.PlayWelcomeOnStartup) return;

        var dir = Path.Combine(Path.GetTempPath(), "MpuTrainerWelcome");
        try { Directory.CreateDirectory(dir); } catch { return; }

        var apiKey = _secrets.Load("tts");
        var cached = Path.Combine(dir, $"intro_{VoiceSig(s, apiKey)}.mp3");

        // Bereits fertiges Intro vorhanden -> direkt abspielen.
        if (File.Exists(cached))
        {
            try { await _player.PlayAsync(cached, s.SpeakerId, s.Volume); } catch { }
            return;
        }

        var musicWav = Path.Combine(dir, "intro_music.wav");
        var voiceWav = Path.Combine(dir, "intro_voice.wav");

        if (!TryExtractMusic(musicWav)) return; // ohne Musikbett kein Intro

        float[] music;
        try { music = ReadMono(musicWav, Rate); }
        catch { return; }

        // Stimme erzeugen (optional). Nur bei Erfolg wird das fertige Intro zwischengespeichert.
        float[]? voice = null;
        try
        {
            await _tts.SpeakToWaveFileAsync(WelcomeText, voiceWav, BuildIntroOptions(s, apiKey));
            var v = ReadMono(voiceWav, Rate);
            if (v.Length > 0) voice = v;
        }
        catch { voice = null; }

        var target = voice is not null ? cached : Path.Combine(dir, "intro_nur_musik.mp3");
        try
        {
            BuildIntroMp3(music, voice, target);
            await _player.PlayAsync(target, s.SpeakerId, s.Volume);
        }
        catch { /* Intro ist optional */ }
    }

    // ---- Stimme: Optionen + Cache-Kennung -----------------------------

    /// <summary>
    /// Baut die TTS-Optionen fuer das Intro. Bei OpenAI wird gezielt "onyx" (seriöse Männerstimme)
    /// erzwungen; andernfalls wird die normal eingestellte Sprachausgabe verwendet.
    /// </summary>
    private static TtsOptions BuildIntroOptions(AppSettings s, string? apiKey)
    {
        if (s.TtsProvider == TtsProvider.OpenAI && !string.IsNullOrWhiteSpace(apiKey))
        {
            var model = string.IsNullOrWhiteSpace(s.OpenAiTtsModel) ? "tts-1-hd" : s.OpenAiTtsModel;
            return new TtsOptions(TtsProvider.OpenAI, s.VoiceName, (int)Math.Round(s.Volume * 100),
                apiKey, "onyx", model, s.ElevenLabsVoiceId, s.ElevenLabsModel);
        }

        return new TtsOptions(s.TtsProvider, s.VoiceName, (int)Math.Round(s.Volume * 100),
            apiKey, s.OpenAiTtsVoice, s.OpenAiTtsModel, s.ElevenLabsVoiceId, s.ElevenLabsModel);
    }

    private static string VoiceSig(AppSettings s, string? apiKey)
    {
        var raw = s.TtsProvider switch
        {
            TtsProvider.OpenAI => "oai|onyx|" + (string.IsNullOrWhiteSpace(apiKey) ? "nokey" : "key"),
            TtsProvider.ElevenLabs => "el|" + s.ElevenLabsVoiceId,
            _ => "win|" + s.VoiceName
        };
        var hash = System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
    }

    // ---- Eingebettetes Musikbett --------------------------------------

    private static bool TryExtractMusic(string destWav)
    {
        var asm = typeof(WelcomeService).Assembly;
        var name = asm.GetManifestResourceNames()
                      .FirstOrDefault(n => n.EndsWith("intro_music.wav", StringComparison.OrdinalIgnoreCase));
        if (name is null) return false;

        using var src = asm.GetManifestResourceStream(name);
        if (src is null) return false;

        using var fs = File.Create(destWav);
        src.CopyTo(fs);
        return true;
    }

    // ---- Audio: Lesen (Mono) + Mischen + MP3 --------------------------

    /// <summary>Liest eine Audiodatei als Mono-Samples in der Zielabtastrate (resampelt bei Bedarf).</summary>
    private static float[] ReadMono(string path, int rate)
    {
        using var reader = new AudioFileReader(path);
        ISampleProvider sp = reader;

        if (sp.WaveFormat.Channels == 2)
            sp = new StereoToMonoSampleProvider(sp) { LeftVolume = 0.5f, RightVolume = 0.5f };
        else if (sp.WaveFormat.Channels > 2)
        {
            var mux = new MultiplexingSampleProvider(new[] { sp }, 1);
            mux.ConnectInputToOutput(0, 0);
            sp = mux;
        }

        if (sp.WaveFormat.SampleRate != rate)
            sp = new WdlResamplingSampleProvider(sp, rate);

        var list = new System.Collections.Generic.List<float>();
        var buffer = new float[rate];
        int read;
        while ((read = sp.Read(buffer, 0, buffer.Length)) > 0)
            for (int i = 0; i < read; i++) list.Add(buffer[i]);
        return list.ToArray();
    }

    /// <summary>
    /// Mischt Stimme (volle Lautstaerke, ab ca. 0,7 s) ueber das Musikbett (leise, mit Ducking
    /// waehrend der Stimme) und schreibt das Ergebnis als MP3 (44,1 kHz, 192 kbps).
    /// </summary>
    private static void BuildIntroMp3(float[] music, float[]? voice, string outMp3)
    {
        int voiceStart = (int)(0.7 * Rate);
        int tail = (int)(0.8 * Rate);
        int voiceLen = voice?.Length ?? 0;
        int total = Math.Max(music.Length, voiceStart + voiceLen + tail);

        const float musicGain = 0.22f;  // Grundlautstaerke der Musik (dezent)
        const float duckGain = 0.60f;   // zusaetzliche Absenkung waehrend der Stimme
        int ramp = (int)(0.05 * Rate);  // 50 ms weiche Rampe fuer das Ducking

        var mixed = new float[total];
        for (int i = 0; i < total; i++)
        {
            float m = i < music.Length ? music[i] : 0f;

            float duck = 1f;
            if (voice is not null)
            {
                int vs = voiceStart, ve = voiceStart + voiceLen;
                if (i >= vs && i < ve)
                {
                    float r = 1f;
                    if (i - vs < ramp) r = (float)(i - vs) / ramp;
                    else if (ve - i < ramp) r = (float)(ve - i) / ramp;
                    duck = 1f - (1f - duckGain) * r;
                }
            }

            float val = m * musicGain * duck;

            if (voice is not null)
            {
                int vi = i - voiceStart;
                if (vi >= 0 && vi < voice.Length) val += voice[vi];
            }

            mixed[i] = val;
        }

        // Gegen Uebersteuern normalisieren.
        float peak = 0f;
        for (int i = 0; i < total; i++)
        {
            float a = Math.Abs(mixed[i]);
            if (a > peak) peak = a;
        }
        float norm = peak > 0.99f ? 0.99f / peak : 1f;

        var pcm = new byte[total * 2];
        for (int i = 0; i < total; i++)
        {
            int v = (int)Math.Round(Math.Clamp(mixed[i] * norm, -1f, 1f) * 32767f);
            short sv = (short)v;
            pcm[i * 2] = (byte)(sv & 0xFF);
            pcm[i * 2 + 1] = (byte)((sv >> 8) & 0xFF);
        }

        var format = new WaveFormat(Rate, 16, 1);
        using var mp3 = new LameMP3FileWriter(outMp3, format, 192);
        mp3.Write(pcm, 0, pcm.Length);
    }
}
