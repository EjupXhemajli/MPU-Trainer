using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MpuTrainer.Models;

namespace MpuTrainer.Services;

public interface IGutachterCaseStore
{
    /// <summary>Alle gespeicherten Faelle (Vorgaenge), neueste zuerst.</summary>
    List<GutachterCase> List();

    /// <summary>Laedt einen Fall anhand der Id (oder null, falls nicht vorhanden).</summary>
    GutachterCase? Load(string id);

    /// <summary>Speichert einen Fall (legt ihn bei Bedarf an) und aktualisiert das Aenderungsdatum.</summary>
    void Save(GutachterCase c);

    /// <summary>Loescht einen Fall samt zugehoerigem Ordner.</summary>
    void Delete(string id);

    /// <summary>Ordner des Falls (fuer spaetere Audiodateien, Exporte usw.).</summary>
    string CaseDirectory(string id);
}

/// <summary>
/// Speichert Gutachter-Faelle lokal: je Fall ein eigener Ordner unter
/// %AppData%\MpuTrainer\Gutachten\&lt;Id&gt; mit einer fall.json. So bleibt jeder Vorgang getrennt
/// und kann spaeter wieder geoeffnet werden. Alle Operationen sind fehlertolerant.
/// </summary>
public class GutachterCaseStore : IGutachterCaseStore
{
    private readonly string _root;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public GutachterCaseStore()
    {
        _root = Path.Combine(App.DataDirectory, "Gutachten");
        try { Directory.CreateDirectory(_root); } catch { /* beim Speichern erneut versucht */ }
    }

    public string CaseDirectory(string id) => Path.Combine(_root, id);

    private string CaseFile(string id) => Path.Combine(CaseDirectory(id), "fall.json");

    public List<GutachterCase> List()
    {
        var result = new List<GutachterCase>();
        try
        {
            if (!Directory.Exists(_root)) return result;

            foreach (var dir in Directory.GetDirectories(_root))
            {
                var file = Path.Combine(dir, "fall.json");
                if (!File.Exists(file)) continue;
                try
                {
                    var c = JsonSerializer.Deserialize<GutachterCase>(File.ReadAllText(file));
                    if (c is not null) result.Add(c);
                }
                catch { /* beschaedigte Datei ueberspringen */ }
            }
        }
        catch { /* Ordner nicht lesbar -> leere Liste */ }

        return result.OrderByDescending(c => c.UpdatedAt).ToList();
    }

    public GutachterCase? Load(string id)
    {
        try
        {
            var file = CaseFile(id);
            if (!File.Exists(file)) return null;
            return JsonSerializer.Deserialize<GutachterCase>(File.ReadAllText(file));
        }
        catch
        {
            return null;
        }
    }

    public void Save(GutachterCase c)
    {
        if (c is null) throw new ArgumentNullException(nameof(c));
        if (string.IsNullOrWhiteSpace(c.Id)) c.Id = Guid.NewGuid().ToString("N");

        c.UpdatedAt = DateTime.Now;

        var dir = CaseDirectory(c.Id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(CaseFile(c.Id), JsonSerializer.Serialize(c, JsonOpts));
    }

    public void Delete(string id)
    {
        try
        {
            var dir = CaseDirectory(id);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch { /* bereits geloescht oder gesperrt */ }
    }
}
