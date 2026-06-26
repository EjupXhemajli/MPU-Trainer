using System;
using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MpuTrainer.Audio;

public interface IAudioRecorder
{
    /// <summary>Pegel der Aufnahme (0..1) zur Anzeige im UI.</summary>
    event Action<float>? LevelChanged;

    bool IsRecording { get; }
    void Start(string? micId, string outputWavPath);
    void Stop();

    /// <summary>Stoppt die Aufnahme und kehrt erst zurueck, wenn die WAV-Datei vollstaendig geschrieben ist.</summary>
    Task StopAsync();
}

/// <summary>
/// Nimmt Mikrofonsignal ueber WASAPI auf und schreibt es als WAV-Datei.
/// Liefert waehrend der Aufnahme den Pegel fuer eine einfache Aussteuerungsanzeige.
/// </summary>
public class AudioRecorder : IAudioRecorder, IDisposable
{
    private readonly IAudioDeviceService _devices;

    private WasapiCapture? _capture;
    private WaveFileWriter? _writer;
    private MMDevice? _device;
    private TaskCompletionSource<bool>? _stopCompletion;

    public event Action<float>? LevelChanged;
    public bool IsRecording { get; private set; }

    public AudioRecorder(IAudioDeviceService devices) => _devices = devices;

    public void Start(string? micId, string outputWavPath)
    {
        if (IsRecording) return;

        _device = _devices.ResolveCaptureDevice(micId)
                  ?? throw new InvalidOperationException("Kein Mikrofon gefunden.");

        Directory.CreateDirectory(Path.GetDirectoryName(outputWavPath)!);

        _capture = new WasapiCapture(_device);

        // Schlaegt das Anlegen der Datei fehl, alle bereits belegten Ressourcen wieder freigeben.
        try
        {
            _writer = new WaveFileWriter(outputWavPath, _capture.WaveFormat);
        }
        catch
        {
            _capture.Dispose();
            _capture = null;
            _device?.Dispose();
            _device = null;
            throw;
        }

        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;

        _capture.StartRecording();
        IsRecording = true;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        _writer?.Write(e.Buffer, 0, e.BytesRecorded);

        // Spitzenpegel ermitteln; Format kann Float (32 Bit) oder PCM (16 Bit) sein.
        float peak = 0f;
        var fmt = _capture!.WaveFormat;

        if (fmt.Encoding == WaveFormatEncoding.IeeeFloat)
        {
            for (int i = 0; i + 3 < e.BytesRecorded; i += 4)
            {
                float sample = BitConverter.ToSingle(e.Buffer, i);
                float abs = Math.Abs(sample);
                if (abs > peak) peak = abs;
            }
        }
        else // 16-bit PCM
        {
            for (int i = 0; i + 1 < e.BytesRecorded; i += 2)
            {
                short sample = BitConverter.ToInt16(e.Buffer, i);
                float abs = Math.Abs(sample / 32768f);
                if (abs > peak) peak = abs;
            }
        }

        LevelChanged?.Invoke(Math.Min(1f, peak));
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        _writer?.Dispose();
        _writer = null;

        if (_capture is not null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
            _capture.Dispose();
            _capture = null;
        }

        _device?.Dispose();
        _device = null;

        IsRecording = false;
        LevelChanged?.Invoke(0f);

        // Wartende StopAsync-Aufrufer freigeben: Datei ist jetzt vollstaendig geschrieben.
        _stopCompletion?.TrySetResult(true);
        _stopCompletion = null;
    }

    public void Stop()
    {
        if (!IsRecording) return;
        _capture?.StopRecording(); // loest RecordingStopped aus (Aufraeumen dort)
    }

    public Task StopAsync()
    {
        if (!IsRecording) return Task.CompletedTask;

        _stopCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _capture?.StopRecording();
        return _stopCompletion.Task;
    }

    public void Dispose()
    {
        try { Stop(); } catch { /* ignorieren */ }
    }
}
