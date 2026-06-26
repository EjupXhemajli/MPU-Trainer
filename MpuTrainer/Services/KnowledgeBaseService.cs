using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using MpuTrainer.DocumentProcessing;

namespace MpuTrainer.Services;

/// <summary>Art eines Wissensbasis-Eintrags.</summary>
public enum KnowledgeKind
{
    /// <summary>Fachliche Anweisung/Methodik (z. B. DIAGNOSTIKER-Skill) – steuert das Denken der KI.</summary>
    Skill,
    /// <summary>Nachschlagewerk/Datenbank/Unterlage (z. B. BK-5-Kriterien, Akten).</summary>
    Dokument
}

/// <summary>Ein Eintrag der Wissensbasis (Verweis auf die extrahierte Textdatei).</summary>
public class KnowledgeItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FileName { get; set; } = string.Empty;
    public KnowledgeKind Kind { get; set; } = KnowledgeKind.Dokument;
    public int CharCount { get; set; }
    public bool Enabled { get; set; } = true;
    public string TextFile { get; set; } = string.Empty;
}

public interface IKnowledgeBaseService
{
    /// <summary>Alle gespeicherten Eintraege (Skills und Dokumente).</summary>
    List<KnowledgeItem> List();

    /// <summary>Fuegt eine Datei hinzu: extrahiert den Text und speichert ihn. Wirft mit Klartext-Fehler.</summary>
    KnowledgeItem Add(string filePath, KnowledgeKind kind);

    /// <summary>Entfernt einen Eintrag samt gespeichertem Text.</summary>
    void Remove(string id);

    /// <summary>Aktiviert/deaktiviert einen Eintrag (deaktivierte fliessen nicht in die KI ein).</summary>
    void SetEnabled(string id, bool enabled);

    /// <summary>
    /// Baut den fachlichen Kontext fuer die KI: zuerst die Skills (Methodik), dann die Dokumente,
    /// jeweils mit Ueberschrift, auf hoechstens maxChars Zeichen begrenzt. Leer, wenn nichts aktiv ist.
    /// </summary>
    string BuildContext(int maxChars);

    /// <summary>Summe der Zeichen aller aktiven Eintraege (fuer die Anzeige).</summary>
    int TotalActiveChars();
}

/// <summary>
/// Verwaltet eine lokale Wissensbasis aus Skills (fachliche Anweisungen) und Dokumenten
/// (Nachschlagewerke, Datenbanken). Die Dateien werden beim Hinzufuegen in Text umgewandelt und im
/// AppData-Ordner abgelegt; die KI-Auswertung bindet die aktiven Inhalte als fachlichen Rahmen ein.
/// </summary>
public class KnowledgeBaseService : IKnowledgeBaseService
{
    private readonly IDocumentExtractionService _extraction;
    private readonly string _dir;
    private readonly string _manifestPath;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public KnowledgeBaseService(IDocumentExtractionService extraction)
    {
        _extraction = extraction;
        _dir = Path.Combine(App.DataDirectory, "Wissensbasis");
        _manifestPath = Path.Combine(_dir, "manifest.json");
        try { Directory.CreateDirectory(_dir); } catch { /* beim Hinzufuegen erneut versucht */ }
    }

    public List<KnowledgeItem> List()
    {
        try
        {
            if (!File.Exists(_manifestPath)) return new List<KnowledgeItem>();
            var json = File.ReadAllText(_manifestPath);
            return JsonSerializer.Deserialize<List<KnowledgeItem>>(json) ?? new List<KnowledgeItem>();
        }
        catch
        {
            return new List<KnowledgeItem>();
        }
    }

    public KnowledgeItem Add(string filePath, KnowledgeKind kind)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Datei nicht gefunden.", filePath);

        Directory.CreateDirectory(_dir);

        var text = ExtractText(filePath);
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException(
                "Aus dieser Datei liess sich kein Text lesen (evtl. ein gescanntes PDF oder ein leeres Dokument).");

        var item = new KnowledgeItem
        {
            FileName = Path.GetFileName(filePath),
            Kind = kind,
            CharCount = text.Length,
            Enabled = true
        };
        item.TextFile = item.Id + ".txt";

        File.WriteAllText(Path.Combine(_dir, item.TextFile), text);

        var items = List();
        items.Add(item);
        Save(items);
        return item;
    }

    public void Remove(string id)
    {
        var items = List();
        var item = items.FirstOrDefault(i => i.Id == id);
        if (item is null) return;

        try
        {
            var p = Path.Combine(_dir, item.TextFile);
            if (File.Exists(p)) File.Delete(p);
        }
        catch { /* Datei evtl. schon weg */ }

        items.RemoveAll(i => i.Id == id);
        Save(items);
    }

    public void SetEnabled(string id, bool enabled)
    {
        var items = List();
        var item = items.FirstOrDefault(i => i.Id == id);
        if (item is null) return;
        item.Enabled = enabled;
        Save(items);
    }

    public int TotalActiveChars()
    {
        return List().Where(i => i.Enabled).Sum(i => i.CharCount);
    }

    public string BuildContext(int maxChars)
    {
        var items = List().Where(i => i.Enabled).ToList();
        if (items.Count == 0) return string.Empty;

        // Skills (Methodik) zuerst, dann Dokumente.
        var ordered = items.Where(i => i.Kind == KnowledgeKind.Skill)
            .Concat(items.Where(i => i.Kind == KnowledgeKind.Dokument));

        var sb = new StringBuilder();
        foreach (var item in ordered)
        {
            if (sb.Length >= maxChars) break;

            string content;
            try { content = File.ReadAllText(Path.Combine(_dir, item.TextFile)); }
            catch { continue; }
            if (string.IsNullOrWhiteSpace(content)) continue;

            var header = item.Kind == KnowledgeKind.Skill
                ? $"\n===== SKILL / ANWEISUNG: {item.FileName} =====\n"
                : $"\n===== DOKUMENT / WISSEN: {item.FileName} =====\n";

            int remaining = maxChars - sb.Length;
            if (header.Length + content.Length > remaining)
            {
                // Nur so viel anhaengen, wie noch ins Budget passt.
                sb.Append(header);
                int room = maxChars - sb.Length;
                if (room > 0)
                {
                    sb.Append(content.AsSpan(0, Math.Min(room, content.Length)));
                    sb.Append("\n[... gekuerzt ...]\n");
                }
                break;
            }

            sb.Append(header);
            sb.Append(content);
            sb.Append('\n');
        }

        return sb.ToString().Trim();
    }

    // ---- intern -------------------------------------------------------

    private string ExtractText(string filePath)
    {
        try
        {
            return _extraction.Extract(filePath).Text;
        }
        catch (NotSupportedException)
        {
            // Unbekannte Endung -> als reinen Text versuchen ("... und so weiter").
            try { return File.ReadAllText(filePath); }
            catch { return string.Empty; }
        }
    }

    private void Save(List<KnowledgeItem> items)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_manifestPath, JsonSerializer.Serialize(items, JsonOpts));
    }
}
