using System;
using System.IO;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MpuTrainer.Audio;

public interface IAudioPlayer
{
    bool IsPlaying { get; }

    /// <summary>Spielt eine Audiodatei auf dem gewaehlten Lautsprecher ab.</summary>
    Task PlayAsync(string filePath, string? speakerId, double volume, CancellationToken ct = default);

    void Stop();
}

/// <summary>
/// Spielt WAV-/Audiodateien ueber WASAPI auf dem ausgewaehlten Ausgabegeraet ab.
/// Die Lautstaerke wird beruecksichtigt; Stop bricht laufende Wiedergabe ab.
/// </summary>
public class AudioPlayer : IAudioPlayer
{
    private readonly IAudioDeviceService _devices;
    private WasapiOut? _output;

    public bool IsPlaying { get; private set; }

    public AudioPlayer(IAudioDeviceService devices) => _devices = devices;

    public async Task PlayAsync(string filePath, string? speakerId, double volume, CancellationToken ct = default)
    {
        if (!File.Exists(filePath)) return;

        Stop(); // evtl. laufende Wiedergabe beenden

        var device = _devices.ResolveRenderDevice(speakerId)
                     ?? throw new InvalidOperationException("Kein Lautsprecher gefunden.");

        await using var reader = new AudioFileReader(filePath)
        {
            Volume = (float)Math.Clamp(volume, 0.0, 1.0)
        };

        using (device)
        using (_output = new WasapiOut(device, AudioClientShareMode.Shared, true, 120))
        {
            _output.Init(reader);
            _output.Play();
            IsPlaying = true;

            // Wiedergabeende abwarten, ohne den UI-Thread zu blockieren.
            await Task.Run(() =>
            {
                while (_output is not null &&
                       _output.PlaybackState == PlaybackState.Playing &&
                       !ct.IsCancellationRequested)
                {
                    Thread.Sleep(60);
                }
            }, ct).ConfigureAwait(false);

            try { _output?.Stop(); } catch { /* ignorieren */ }
            IsPlaying = false;
            _output = null;
        }
    }

    public void Stop()
    {
        try
        {
            if (_output is not null)
            {
                _output.Stop();
                _output.Dispose();
                _output = null;
            }
        }
        catch { /* ignorieren */ }
        IsPlaying = false;
    }
}
