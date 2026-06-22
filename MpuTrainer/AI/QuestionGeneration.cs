using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using MpuTrainer.Models;
using MpuTrainer.Services;

namespace MpuTrainer.AI;

public interface IQuestionGenerationService
{
    Task<List<GeneratedQuestion>> GenerateQuestionsAsync(
        string leitfaden, int count, QuestionCategory focus, string language, CancellationToken ct = default);

    Task<string> GenerateModelAnswerAsync(
        string leitfaden, string question, string language, CancellationToken ct = default);
}

/// <summary>
/// Kapselt die KI-gestuetzte Erzeugung von Fragen und Musterantworten und
/// parst die JSON-Antwort robust in typisierte Objekte.
/// </summary>
public class QuestionGenerationService : IQuestionGenerationService
{
    private readonly IAiClientFactory _factory;
    private readonly ISettingsService _settings;

    public QuestionGenerationService(IAiClientFactory factory, ISettingsService settings)
    {
        _factory = factory;
        _settings = settings;
    }

    public async Task<List<GeneratedQuestion>> GenerateQuestionsAsync(
        string leitfaden, int count, QuestionCategory focus, string language, CancellationToken ct = default)
    {
        var client = _factory.Create();
        var temp = _settings.Current.Temperature;

        // Tokenbudget grosszuegiger an die Fragenanzahl anpassen (Frage + Ich-Form-Musterantwort
        // brauchen mehr Platz als frueher angenommen). Nach oben gedeckelt, damit auch Anbieter
        // mit kleinerem Ausgabelimit nicht mit einer Fehlermeldung abbrechen.
        int maxTokens = Math.Min(8000, Math.Max(_settings.Current.MaxTokens, count * 320 + 800));

        var prompt = MpuPrompts.BuildQuestionPrompt(leitfaden, count, focus, language);
        var raw = await client.CompleteAsync(MpuPrompts.System, prompt, temp, maxTokens, ct);

        var parsed = ParseQuestions(raw);
        if (parsed.Count == 0)
        {
            // Klare, konkrete Ursache statt einer pauschalen Meldung.
            if (string.IsNullOrWhiteSpace(raw))
                throw new InvalidOperationException(
                    "Die KI hat eine leere Antwort geliefert. Bitte pruefen: Modellname, " +
                    "Anbieter/Endpoint und API-Key unter Einstellungen > KI-Anbindung.");

            throw new InvalidOperationException(
                "Die KI-Antwort liess sich nicht als Frageliste lesen. Anfang der Antwort: " +
                Snippet(raw));
        }

        return parsed;
    }

    private static string Snippet(string s)
    {
        var t = s.Trim().Replace("\r", " ").Replace("\n", " ");
        return t.Length > 200 ? t[..200] + " ..." : t;
    }

    public async Task<string> GenerateModelAnswerAsync(
        string leitfaden, string question, string language, CancellationToken ct = default)
    {
        var client = _factory.Create();
        var temp = _settings.Current.Temperature;

        var prompt = MpuPrompts.BuildModelAnswerPrompt(leitfaden, question, language);
        var answer = await client.CompleteAsync(MpuPrompts.System, prompt, temp, 600, ct);
        return answer.Trim();
    }

    // ---- JSON-Parsing (robust gegen Prosa, Wrapper-Objekte und Abschnitt) ----

    private static List<GeneratedQuestion> ParseQuestions(string raw)
    {
        var list = new List<GeneratedQuestion>();
        if (string.IsNullOrWhiteSpace(raw)) return list;

        var s = StripFences(raw);

        // 1) Sauberes JSON-Array (ggf. von Prosa umgeben).
        var array = Slice(s, '[', ']');
        if (array is not null) AddFromArray(array, list);
        if (list.Count > 0) return list;

        // 2) Objekt mit eingebettetem Array, z. B. {"fragen": [ ... ]}.
        var obj = Slice(s, '{', '}');
        if (obj is not null) AddFromWrapperObject(obj, list);
        if (list.Count > 0) return list;

        // 3) Rettung: einzelne {...}-Objekte einsammeln. Faengt auch abgeschnittene
        //    Antworten ab (das letzte, unvollstaendige Objekt wird einfach uebersprungen).
        AddFromSalvagedObjects(s, list);
        return list;
    }

    /// <summary>Entfernt umschliessende Code-Zaeune (```), falls vorhanden.</summary>
    private static string StripFences(string raw)
    {
        var s = raw.Trim();
        if (s.StartsWith("```"))
        {
            int nl = s.IndexOf('\n');
            if (nl >= 0) s = s[(nl + 1)..];
            int fence = s.LastIndexOf("```", StringComparison.Ordinal);
            if (fence >= 0) s = s[..fence];
            s = s.Trim();
        }
        return s;
    }

    /// <summary>Schneidet vom ersten Oeffnungs- bis zum letzten Schliesszeichen zu.</summary>
    private static string? Slice(string s, char open, char close)
    {
        int start = s.IndexOf(open);
        int end = s.LastIndexOf(close);
        if (start >= 0 && end > start) return s.Substring(start, end - start + 1);
        return null;
    }

    private static void AddFromArray(string array, List<GeneratedQuestion> list)
    {
        try
        {
            using var doc = JsonDocument.Parse(array);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return;
            foreach (var el in doc.RootElement.EnumerateArray())
                AddIfValid(el, list);
        }
        catch { /* kein sauberes Array -> spaetere Strategien greifen */ }
    }

    private static void AddFromWrapperObject(string obj, List<GeneratedQuestion> list)
    {
        try
        {
            using var doc = JsonDocument.Parse(obj);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Array) continue;
                foreach (var el in prop.Value.EnumerateArray())
                    AddIfValid(el, list);
                if (list.Count > 0) return;
            }
        }
        catch { /* kein Wrapper-Objekt -> Rettung greift */ }
    }

    /// <summary>
    /// Sammelt vollstaendige {...}-Bloecke (Klammertiefe 0) aus beliebigem Text.
    /// Beachtet Strings und Escapes, damit Klammern innerhalb von Texten nicht mitzaehlen.
    /// </summary>
    private static void AddFromSalvagedObjects(string s, List<GeneratedQuestion> list)
    {
        int depth = 0, objStart = -1;
        bool inString = false, escape = false;

        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];

            if (inString)
            {
                if (escape) escape = false;
                else if (c == '\\') escape = true;
                else if (c == '"') inString = false;
                continue;
            }

            if (c == '"') { inString = true; continue; }

            if (c == '{')
            {
                if (depth == 0) objStart = i;
                depth++;
            }
            else if (c == '}' && depth > 0)
            {
                depth--;
                if (depth == 0 && objStart >= 0)
                {
                    var objText = s.Substring(objStart, i - objStart + 1);
                    try
                    {
                        using var doc = JsonDocument.Parse(objText);
                        AddIfValid(doc.RootElement, list);
                    }
                    catch { /* dieses Fragment ignorieren */ }
                    objStart = -1;
                }
            }
        }
    }

    private static void AddIfValid(JsonElement el, List<GeneratedQuestion> list)
    {
        if (el.ValueKind != JsonValueKind.Object) return;

        var text = GetString(el, "frage", "text", "question");
        if (string.IsNullOrWhiteSpace(text)) return;

        list.Add(new GeneratedQuestion
        {
            Text = text.Trim(),
            Category = MapCategory(GetString(el, "kategorie", "category")),
            Difficulty = MapDifficulty(GetString(el, "schwierigkeit", "difficulty")),
            ModelAnswer = GetString(el, "musterantwort", "modelAnswer", "answer")
        });
    }

    private static string GetString(JsonElement el, params string[] names)
    {
        foreach (var name in names)
        {
            foreach (var prop in el.EnumerateObject())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase) &&
                    prop.Value.ValueKind == JsonValueKind.String)
                {
                    return prop.Value.GetString() ?? string.Empty;
                }
            }
        }
        return string.Empty;
    }

    private static QuestionCategory MapCategory(string value)
    {
        var v = value.ToLowerInvariant();
        if (v.Contains("alkohol")) return QuestionCategory.Alkohol;
        if (v.Contains("drogen")) return QuestionCategory.Drogen;
        if (v.Contains("verkehr")) return QuestionCategory.Verkehrsdelikte;
        if (v.Contains("straftat")) return QuestionCategory.Straftaten;
        if (v.Contains("vorgeschichte") || v.Contains("konsumvor")) return QuestionCategory.Konsumvorgeschichte;
        if (v.Contains("motiv") || v.Contains("hintergr")) return QuestionCategory.MotiveUndHintergruende;
        if (v.Contains("veraender") || v.Contains("änder") || v.Contains("aender")) return QuestionCategory.Veraenderung;
        if (v.Contains("rueckfall") || v.Contains("rückfall")) return QuestionCategory.Rueckfallvermeidung;
        if (v.Contains("abstinenz")) return QuestionCategory.Abstinenz;
        if (v.Contains("einsicht") || v.Contains("verantwort")) return QuestionCategory.EinsichtUndVerantwortung;
        return QuestionCategory.Allgemein;
    }

    private static DifficultyLevel MapDifficulty(string value)
    {
        var v = value.ToLowerInvariant();
        if (v.Contains("leicht") || v.Contains("easy")) return DifficultyLevel.Leicht;
        if (v.Contains("schwer") || v.Contains("hard")) return DifficultyLevel.Schwer;
        return DifficultyLevel.Mittel;
    }
}
