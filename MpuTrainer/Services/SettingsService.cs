using System.IO;
using System.Text.Json;
using MpuTrainer.Models;

namespace MpuTrainer.Services;

public interface ISettingsService
{
    AppSettings Current { get; }
    void Load();
    void Save();
}

/// <summary>
/// Laedt und speichert die Anwendungseinstellungen als JSON-Datei im
/// AppData-Ordner. Der API-Key ist hier bewusst NICHT enthalten.
/// </summary>
public class SettingsService : ISettingsService
{
    private readonly string _path =
        Path.Combine(App.DataDirectory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public AppSettings Current { get; private set; } = new();

    public void Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded is not null)
                    Current = loaded;
            }
        }
        catch
        {
            // Bei beschaedigter Datei mit Standardwerten weiterarbeiten.
            Current = new AppSettings();
        }

        if (string.IsNullOrWhiteSpace(Current.ProjectsDirectory))
        {
            Current.ProjectsDirectory = Path.Combine(App.DataDirectory, "Projekte");
            Directory.CreateDirectory(Current.ProjectsDirectory);
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var json = JsonSerializer.Serialize(Current, JsonOptions);
            File.WriteAllText(_path, json);
        }
        catch
        {
            // Schreibfehler still ignorieren; UI meldet ggf. separat.
        }
    }
}
