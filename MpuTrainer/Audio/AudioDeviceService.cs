using System.Collections.Generic;
using NAudio.CoreAudioApi;
using MpuTrainer.Models;

namespace MpuTrainer.Audio;

public interface IAudioDeviceService
{
    List<AudioDevice> GetMicrophones();
    List<AudioDevice> GetSpeakers();

    /// <summary>Liefert das MMDevice zur Id oder das Standard-Aufnahmegeraet.</summary>
    MMDevice? ResolveCaptureDevice(string? id);

    /// <summary>Liefert das MMDevice zur Id oder das Standard-Wiedergabegeraet.</summary>
    MMDevice? ResolveRenderDevice(string? id);
}

/// <summary>
/// Erkennt vorhandene Mikrofone und Lautsprecher ueber die Windows Core Audio API
/// und loest gespeicherte Geraete-Ids in konkrete Geraete auf.
/// </summary>
public class AudioDeviceService : IAudioDeviceService
{
    public List<AudioDevice> GetMicrophones() => Enumerate(DataFlow.Capture);
    public List<AudioDevice> GetSpeakers() => Enumerate(DataFlow.Render);

    private static List<AudioDevice> Enumerate(DataFlow flow)
    {
        var result = new List<AudioDevice>();
        using var enumerator = new MMDeviceEnumerator();

        string? defaultId = null;
        try
        {
            // Eigenschaften sofort lesen, solange das Geraet-Objekt lebt.
            using var def = enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia);
            defaultId = def.ID;
        }
        catch { /* kein Standardgeraet vorhanden */ }

        foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            using (device)
            {
                result.Add(new AudioDevice
                {
                    Id = device.ID,
                    Name = device.FriendlyName,
                    IsDefault = device.ID == defaultId
                });
            }
        }
        return result;
    }

    public MMDevice? ResolveCaptureDevice(string? id) => Resolve(DataFlow.Capture, id);
    public MMDevice? ResolveRenderDevice(string? id) => Resolve(DataFlow.Render, id);

    private static MMDevice? Resolve(DataFlow flow, string? id)
    {
        var enumerator = new MMDeviceEnumerator();
        try
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
                {
                    if (device.ID == id) return device;
                    device.Dispose();
                }
            }
            return enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia);
        }
        catch
        {
            return null;
        }
    }
}
