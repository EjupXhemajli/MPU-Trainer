using System.Collections.Generic;
using System.IO;
using NAudio.Lame;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MpuTrainer.Audio;

public interface ISessionAudioBuilder
{
    /// <summary>
    /// Fuegt mehrere WAV-Segmente (Frage, Klientenantwort, Musterantwort ...) in
    /// der angegebenen Reihenfolge zu einer einzelnen MP3-Datei zusammen.
    /// </summary>
    Task BuildAsync(IReadOnlyList<string> wavSegments, string outputMp3Path);
}

/// <summary>
/// Setzt die komplette Trainingsunterhaltung zu einer MP3 zusammen. Alle Segmente
/// werden auf ein einheitliches Format (44,1 kHz, 16 Bit, Mono) gebracht, damit
/// TTS-Ausgaben und Mikrofonaufnahmen unterschiedlicher Formate zusammenpassen.
/// </summary>
public class SessionAudioBuilder : ISessionAudioBuilder
{
    private static readonly WaveFormat TargetFormat = new(44100, 16, 1);
    private const int SilenceMs = 600; // kurze Pause zwischen Segmenten

    public Task BuildAsync(IReadOnlyList<string> wavSegments, string outputMp3Path)
    {
        return Task.Run(() =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputMp3Path)!);

            using var mp3 = new LameMP3FileWriter(outputMp3Path, TargetFormat, LAMEPreset.STANDARD);

            var buffer = new byte[TargetFormat.AverageBytesPerSecond];
            bool first = true;

            foreach (var segment in wavSegments)
            {
                if (string.IsNullOrWhiteSpace(segment) || !File.Exists(segment))
                    continue;

                if (!first)
                    WriteSilence(mp3);
                first = false;

                using var reader = new AudioFileReader(segment);
                ISampleProvider mono = ToMono(reader);

                // Bei abweichender Abtastrate resampeln.
                if (mono.WaveFormat.SampleRate != TargetFormat.SampleRate)
                    mono = new WdlResamplingSampleProvider(mono, TargetFormat.SampleRate);

                var wave16 = new SampleToWaveProvider16(mono);

                int read;
                while ((read = wave16.Read(buffer, 0, buffer.Length)) > 0)
                    mp3.Write(buffer, 0, read);
            }
        });
    }

    /// <summary>Mischt beliebige Kanalzahlen auf Mono herunter (Kanal 0).</summary>
    private static ISampleProvider ToMono(ISampleProvider source)
    {
        if (source.WaveFormat.Channels == 1)
            return source;

        if (source.WaveFormat.Channels == 2)
            return new StereoToMonoSampleProvider(source) { LeftVolume = 0.5f, RightVolume = 0.5f };

        // Mehr als zwei Kanaele: ersten Kanal auf Mono abbilden.
        var mux = new MultiplexingSampleProvider(new[] { source }, 1);
        mux.ConnectInputToOutput(0, 0);
        return mux;
    }

    private static void WriteSilence(Stream mp3)
    {
        int bytes = TargetFormat.AverageBytesPerSecond * SilenceMs / 1000;
        var silence = new byte[bytes];
        mp3.Write(silence, 0, silence.Length);
    }
}
