using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using MpuTrainer.Models;
using MpuTrainer.Services;

namespace MpuTrainer.AI;

/// <summary>Strukturierte Auswertung einer Klientenantwort im Vergleich zur Musterantwort.</summary>
public class AnswerEvaluation
{
    /// <summary>Stimmt die Antwort im Kern mit der Musterantwort ueberein? (Urteil + Begruendung)</summary>
    public string Kernuebereinstimmung { get; set; } = string.Empty;

    /// <summary>Was war falsch oder weicht von der Musterantwort ab?</summary>
    public List<string> Abweichungen { get; set; } = new();

    /// <summary>Was hat der Klient im Kern noch nicht verstanden?</summary>
    public List<string> NichtVerstanden { get; set; } = new();

    /// <summary>Konkrete Verbesserungsvorschlaege.</summary>
    public List<string> Verbesserungen { get; set; } = new();

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Kernuebereinstimmung) &&
        Abweichungen.Count == 0 && NichtVerstanden.Count == 0 && Verbesserungen.Count == 0;
}

public interface IAnswerEvaluationService
{
    /// <summary>Vergleicht das Transkript einer Klientenantwort mit der Musterantwort.</summary>
    Task<AnswerEvaluation> EvaluateAsync(
        ClientProject project, TrainingQuestion question, string transcript, CancellationToken ct = default);

    /// <summary>Korrigiert ein automatisches Transkript sprachlich (Rechtschreibung, Grammatik, Sinn).</summary>
    Task<string> CorrectTranscriptAsync(string transcript, string language, CancellationToken ct = default);
}

/// <summary>
/// Vergleicht die (zuvor transkribierte) Antwort eines Klienten mit der Musterantwort: trifft sie
/// die Kernaussagen, was weicht ab, was wurde im Kern nicht verstanden, und wie laesst sie sich
/// verbessern. Nutzt den eingestellten KI-Anbieter. Faellt notfalls auf den Rohtext der KI zurueck,
/// damit immer eine sichtbare Rueckmeldung entsteht.
/// </summary>
public class AnswerEvaluationService : IAnswerEvaluationService
{
    private readonly IAiClientFactory _factory;
    private readonly ISettingsService _settings;

    public AnswerEvaluationService(IAiClientFactory factory, ISettingsService settings)
    {
        _factory = factory;
        _settings = settings;
    }

    public async Task<AnswerEvaluation> EvaluateAsync(
        ClientProject project, TrainingQuestion question, string transcript, CancellationToken ct = default)
    {
        var client = _factory.Create();
        var temp = Math.Min(_settings.Current.Temperature, 0.4);

        var prompt = MpuPrompts.BuildEvaluationPrompt(
            project.LeitfadenText, question.Text, question.ModelAnswer ?? string.Empty,
            transcript, project.Language);

        var raw = await client.CompleteAsync(MpuPrompts.EvaluationSystem, prompt, temp, 1500, ct);

        var result = Parse(raw);

        // Fallback: konnte nichts strukturiert gelesen werden, den Rohtext zeigen,
        // damit nie "keine Auswertung" erscheint.
        if (result.IsEmpty && !string.IsNullOrWhiteSpace(raw))
            result.Kernuebereinstimmung = Clean(raw);

        return result;
    }

    public async Task<string> CorrectTranscriptAsync(
        string transcript, string language, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(transcript)) return transcript;

        try
        {
            var client = _factory.Create();
            var prompt = MpuPrompts.BuildTranscriptCorrectionPrompt(transcript, language);
            int budget = Math.Min(4000, Math.Max(800, transcript.Length));
            var raw = await client.CompleteAsync(
                MpuPrompts.TranscriptCorrectionSystem, prompt, 0.2, budget, ct);

            var cleaned = raw?.Trim();
            if (!string.IsNullOrEmpty(cleaned)) cleaned = cleaned.Trim('`').Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? transcript : cleaned;
        }
        catch
        {
            // Bei Fehlern das Originaltranskript beibehalten.
            return transcript;
        }
    }

    // ---- JSON-Parsing (tolerant) --------------------------------------

    private static AnswerEvaluation Parse(string raw)
    {
        var result = new AnswerEvaluation();
        if (string.IsNullOrWhiteSpace(raw)) return result;

        var json = ExtractJsonObject(raw);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return result;

            result.Kernuebereinstimmung = GetString(root,
                "kernuebereinstimmung", "kernübereinstimmung", "uebereinstimmung", "übereinstimmung", "fazit", "summary");
            result.Abweichungen = GetStringList(root,
                "abweichungen", "falsch", "fehler", "deviations");
            result.NichtVerstanden = GetStringList(root,
                "nicht_verstanden", "nichtverstanden", "nicht verstanden", "defizite", "misunderstandings");
            result.Verbesserungen = GetStringList(root,
                "verbesserungen", "verbesserungsvorschlaege", "verbesserungsvorschläge", "improvements");
        }
        catch
        {
            // unparsebar -> oben greift der Rohtext-Fallback
        }

        return result;
    }

    private static string ExtractJsonObject(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "{}";

        var s = raw.Trim();
        if (s.StartsWith("```"))
        {
            int nl = s.IndexOf('\n');
            if (nl >= 0) s = s[(nl + 1)..];
            int fence = s.LastIndexOf("```", StringComparison.Ordinal);
            if (fence >= 0) s = s[..fence];
            s = s.Trim();
        }

        int start = s.IndexOf('{');
        int end = s.LastIndexOf('}');
        if (start >= 0 && end > start)
            return s.Substring(start, end - start + 1);

        return "{}";
    }

    private static string GetString(JsonElement el, params string[] names)
    {
        foreach (var name in names)
            foreach (var prop in el.EnumerateObject())
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase) &&
                    prop.Value.ValueKind == JsonValueKind.String)
                    return prop.Value.GetString()?.Trim() ?? string.Empty;
        return string.Empty;
    }

    private static List<string> GetStringList(JsonElement el, params string[] names)
    {
        var list = new List<string>();
        foreach (var name in names)
        {
            foreach (var prop in el.EnumerateObject())
            {
                if (!string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase)) continue;

                if (prop.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in prop.Value.EnumerateArray())
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            var v = item.GetString()?.Trim();
                            if (!string.IsNullOrWhiteSpace(v)) list.Add(v);
                        }
                }
                else if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    var v = prop.Value.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(v)) list.Add(v);
                }

                if (list.Count > 0) return list;
            }
        }
        return list;
    }

    private static string Clean(string s) => s.Trim().Trim('`').Trim();
}
