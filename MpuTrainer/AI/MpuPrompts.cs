using MpuTrainer.Models;

namespace MpuTrainer.AI;

/// <summary>
/// Zentrale Sammlung der KI-Prompts. Alle Prompts arbeiten konsequent im
/// fachlichen Kontext der MPU-Vorbereitung; Musterantworten in Ich-Form.
/// </summary>
public static class MpuPrompts
{
    /// <summary>Fachlicher Systemkontext fuer alle Anfragen.</summary>
    public const string System =
        "Du bist ein erfahrener Verkehrspsychologe und unterstuetzt die Vorbereitung " +
        "auf die Medizinisch-Psychologische Untersuchung (MPU) in Deutschland. " +
        "Es geht nicht um auswendig gelernte Standardfloskeln, sondern um eine " +
        "psychologisch nachvollziehbare, ehrliche Reflexion der eigenen Vorgeschichte, " +
        "des Fehlverhaltens, der Ursachen, der Veraenderung und der Rueckfallvermeidung. " +
        "Formuliere fachlich korrekt, aber fuer Klientinnen und Klienten verstaendlich.";

    /// <summary>
    /// Prompt zur Fragegenerierung. Erwartet als Antwort ausschliesslich ein
    /// JSON-Array von Objekten mit den Feldern frage, kategorie, schwierigkeit, musterantwort.
    /// </summary>
    public static string BuildQuestionPrompt(string leitfaden, int count, QuestionCategory focus, string language,
        IReadOnlyList<string>? avoid = null)
    {
        var focusText = focus == QuestionCategory.Allgemein
            ? "Decke die im Leitfaden erkennbaren Themen ausgewogen ab."
            : $"WICHTIG: Erzeuge die Fragen AUSSCHLIESSLICH zum Themenschwerpunkt \"{Describe(focus)}\". " +
              $"Jede einzelne Frage muss sich konkret auf diesen Schwerpunkt beziehen, und das Feld " +
              $"\"kategorie\" ist fuer alle Fragen passend zu diesem Schwerpunkt zu setzen. " +
              $"Andere Aspekte nur, wenn sie diesen Schwerpunkt unmittelbar stuetzen.";

        // Bereits vorhandene Fragen auflisten, damit ein weiterer Stapel nur NEUE Fragen liefert.
        var avoidText = string.Empty;
        if (avoid is { Count: > 0 })
        {
            var items = new List<string>();
            foreach (var a in avoid)
            {
                if (string.IsNullOrWhiteSpace(a)) continue;
                var t = a.Trim();
                items.Add("- " + (t.Length > 140 ? t[..140] : t));
                if (items.Count >= 40) break;
            }
            if (items.Count > 0)
                avoidText =
                    "\n\nDie folgenden Fragen EXISTIEREN BEREITS. Erzeuge ausschliesslich NEUE, " +
                    "inhaltlich deutlich andere Fragen und wiederhole oder paraphrasiere diese NICHT:\n" +
                    string.Join("\n", items) + "\n";
        }

        var categories = string.Join(", ",
            "Alkohol", "Drogen", "Verkehrsdelikte", "Straftaten", "Konsumvorgeschichte",
            "Motive und Hintergruende", "Veraenderung", "Rueckfallvermeidung",
            "Abstinenz", "Einsicht und Verantwortung", "Allgemein");

        var lang = string.IsNullOrWhiteSpace(language) ? "Deutsch" : language.Trim();

        return
            $"Erzeuge genau {count} MPU-Trainingsfragen auf Basis des folgenden Beratungs-Leitfadens.\n" +
            $"{focusText}{avoidText}\n\n" +
            $"WICHTIG: Formuliere die Fragen (Feld \"frage\") UND die Musterantworten (Feld \"musterantwort\") " +
            $"ausschliesslich in folgender Sprache: {lang}. Die Werte fuer \"kategorie\" und " +
            $"\"schwierigkeit\" bleiben unveraendert in der unten vorgegebenen Form.\n\n" +
            "Anforderungen an die Fragen:\n" +
            "- offene, reflexionsfoerdernde Fragen (keine Ja/Nein-Fragen)\n" +
            "- realistisch und an einer echten MPU orientiert\n" +
            "- inhaltlich vom Leitfaden gedeckt\n\n" +
            "Gib pro Frage eine kurze Musterantwort in der ICH-FORM an, die natuerlich und " +
            "individuell klingt (nicht auswendig gelernt).\n\n" +
            "Antworte AUSSCHLIESSLICH mit einem gueltigen JSON-Array, ohne weiteren Text, " +
            "ohne Markdown und ohne Code-Zaeune. Schema je Element:\n" +
            "{\n" +
            "  \"frage\": \"...\",\n" +
            $"  \"kategorie\": \"eine aus: {categories}\",\n" +
            "  \"schwierigkeit\": \"Leicht | Mittel | Schwer\",\n" +
            "  \"musterantwort\": \"... (Ich-Form)\"\n" +
            "}\n\n" +
            "LEITFADEN:\n\"\"\"\n" + Shorten(leitfaden, 200000) + "\n\"\"\"";
    }

    /// <summary>
    /// Prompt fuer eine einzelne Musterantwort in Ich-Form. Erwartet reinen Text.
    /// </summary>
    public static string BuildModelAnswerPrompt(string leitfaden, string question, string language)
    {
        var lang = string.IsNullOrWhiteSpace(language) ? "Deutsch" : language.Trim();
        return
            "Formuliere eine Musterantwort auf die folgende MPU-Frage.\n" +
            "Die Antwort soll in der ICH-FORM stehen, ehrlich, konkret und psychologisch " +
            "nachvollziehbar sein und wie die Antwort einer real vorbereiteten Person klingen " +
            "(nicht auswendig gelernt, keine Floskeln). Laenge: einige Saetze.\n\n" +
            $"WICHTIG: Schreibe die Antwort ausschliesslich in folgender Sprache: {lang}.\n\n" +
            "Antworte nur mit dem Antworttext, ohne Einleitung und ohne Anfuehrungszeichen.\n\n" +
            "FRAGE:\n" + question + "\n\n" +
            "KONTEXT AUS DEM LEITFADEN:\n\"\"\"\n" + Shorten(leitfaden, 8000) + "\n\"\"\"";
    }

    /// <summary>Systemkontext fuer die Auswertung einer Klientenantwort.</summary>
    public const string EvaluationSystem =
        "Du bist ein erfahrener Verkehrspsychologe und MPU-Gutachter mit Schwerpunkt MPU-Vorbereitung. " +
        "Du bewertest die Antwort eines Klienten NICHT nach sprachlicher Aehnlichkeit zu einer Musterantwort, " +
        "sondern nach psychologischer Qualitaet: Problembewusstsein, Einsichtsfaehigkeit, " +
        "Verantwortungsuebernahme, Glaubwuerdigkeit, Authentizitaet, Selbstreflexion, emotionale Verarbeitung, " +
        "Veraenderungsmotivation, Stabilitaet der Veraenderung, konkrete Verhaltensaenderungen im Alltag sowie " +
        "Risiko- und Rueckfallbewusstsein und Rueckfallpraevention. Du achtest auf innere Logik, Widersprueche, " +
        "Bagatellisierung, Schuldverschiebung, fehlende Tiefe und fehlende konkrete Beispiele und benennst " +
        "Schutz- und Risikofaktoren. Du formulierst fachlich, klar und nachvollziehbar und bewertest " +
        "ausschliesslich das, was der Klient tatsaechlich gesagt hat (das Transkript); du erfindest nichts hinzu. " +
        "Du gibst KEINE amtliche MPU-Entscheidung ab, sondern eine vorbereitende psychologische Einschaetzung.";

    /// <summary>
    /// Baut den Systemkontext fuer die Auswertung und bindet – falls vorhanden – die fachliche
    /// Wissensbasis (Skills/Methodik wie DIAGNOSTIKER und Dokumente wie die BK-5-Kriterien) als
    /// massgeblichen Rahmen ein, damit die Beurteilung psychologisch fundiert erfolgt.
    /// </summary>
    public static string BuildEvaluationSystem(string? knowledge)
    {
        if (string.IsNullOrWhiteSpace(knowledge))
            return EvaluationSystem;

        return EvaluationSystem +
            "\n\nDir steht zusaetzlich die folgende FACHLICHE WISSENSBASIS zur Verfuegung. Sie enthaelt " +
            "SKILLS (verbindliche Arbeitsanweisungen und Methodik, z. B. verkehrspsychologische " +
            "Fallanalyse) und DOKUMENTE (Nachschlagewerke wie die Beurteilungskriterien BK 5). Wende sie " +
            "als massgeblichen fachlichen Rahmen an: Beurteile die Antwort wie ein erfahrener " +
            "Verkehrspsychologe und Gutachter nach diesen Vorgaben – also nicht nur als Abgleich mit der " +
            "Musterantwort, sondern auch hinsichtlich einschlaegiger BK-5-Hypothesen und -Kriterien, " +
            "Indikatoren und Kontraindikatoren, Verantwortungsuebernahme, Aufarbeitungstiefe, " +
            "Ursachenverstaendnis und Rueckfallprophylaxe. Stuetze dich auf den Wortlaut der Dokumente; " +
            "erfinde keine Kriterien, Nummern oder Seitenzahlen. Widerspricht die Wissensbasis einer " +
            "allgemeinen Einschaetzung, hat die Wissensbasis Vorrang.\n\n" +
            "===== BEGINN WISSENSBASIS =====\n" + knowledge + "\n===== ENDE WISSENSBASIS =====";
    }

    /// <summary>
    /// Prompt zur Auswertung einer Klientenantwort im Vergleich zur Musterantwort. Erwartet als
    /// Antwort ausschliesslich ein JSON-Objekt mit kernuebereinstimmung, abweichungen,
    /// nicht_verstanden und verbesserungen.
    /// </summary>
    public static string BuildEvaluationPrompt(
        string leitfaden, string question, string modelAnswer, string transcript, string language)
    {
        var lang = string.IsNullOrWhiteSpace(language) ? "Deutsch" : language.Trim();

        return
            "Beurteile die folgende Antwort eines Klienten aus einer MPU-Vorbereitung psychologisch – " +
            "wie ein erfahrener Verkehrspsychologe und MPU-Gutachter.\n\n" +
            "WICHTIG: Es geht NICHT um sprachliche Uebereinstimmung mit der Musterantwort. Die Musterantwort " +
            "ist nur ein inhaltlicher Orientierungspunkt, kein Wortlaut-Massstab. Bewerte die psychologische " +
            "QUALITAET der Antwort: Problembewusstsein, Einsicht, Verantwortungsuebernahme, Glaubwuerdigkeit " +
            "und Authentizitaet, Selbstreflexion, emotionale Verarbeitung, Veraenderungsmotivation und " +
            "-stabilitaet, konkrete Verhaltensaenderungen im Alltag sowie Risikobewusstsein und " +
            "Rueckfallpraevention. Achte auf innere Logik, Widersprueche, Bagatellisierung, Schuldverschiebung, " +
            "fehlende Tiefe und fehlende konkrete Beispiele.\n\n" +
            "Liefere eine fachliche, gut begruendete Rueckmeldung mit genau diesen Bestandteilen:\n" +
            "1. STAERKEN der Antwort (psychologisch konkret, nicht nur formal)\n" +
            "2. SCHWAECHEN der Antwort\n" +
            "3. PSYCHOLOGISCHE EINSCHAETZUNG als zusammenhaengender Fliesstext im gutachterlichen Ton " +
            "(was zeigt die Antwort, was fehlt, wie tief ist die Aufarbeitung)\n" +
            "4. GUTACHTERLICHE RISIKOBEWERTUNG als Fliesstext (Rueckfallrisiko, Schutzfaktoren, Risikofaktoren, " +
            "Stabilitaet der Veraenderung)\n" +
            "5. KONKRETE VERBESSERUNGSVORSCHLAEGE (umsetzbar, inhaltlich)\n" +
            "6. MOEGLICHE NACHFRAGEN, die ein MPU-Gutachter zu dieser Antwort stellen wuerde\n" +
            "7. GESAMTURTEIL: genau einer der Werte \"ueberzeugend\", \"teilweise ueberzeugend\" oder " +
            "\"nicht ueberzeugend\"\n\n" +
            "Wenn die Antwort sehr kurz, leer oder am Thema vorbei ist, sage das ehrlich und werte entsprechend.\n\n" +
            $"WICHTIG: Formuliere die gesamte Rueckmeldung ausschliesslich in folgender Sprache: {lang}.\n\n" +
            "Antworte AUSSCHLIESSLICH mit einem gueltigen JSON-Objekt, ohne weiteren Text, ohne Markdown " +
            "und ohne Code-Zaeune. Schema:\n" +
            "{\n" +
            "  \"staerken\": [\"...\"],\n" +
            "  \"schwaechen\": [\"...\"],\n" +
            "  \"psychologische_einschaetzung\": \"... (Fliesstext)\",\n" +
            "  \"risikobewertung\": \"... (Fliesstext)\",\n" +
            "  \"verbesserungen\": [\"...\"],\n" +
            "  \"moegliche_nachfragen\": [\"...\"],\n" +
            "  \"gesamturteil\": \"ueberzeugend | teilweise ueberzeugend | nicht ueberzeugend\"\n" +
            "}\n\n" +
            "FRAGE:\n" + question + "\n\n" +
            "MUSTERANTWORT (nur inhaltlicher Orientierungspunkt, kein Wortlaut-Massstab):\n\"\"\"\n" +
            (string.IsNullOrWhiteSpace(modelAnswer) ? "(keine Musterantwort hinterlegt)" : modelAnswer.Trim()) +
            "\n\"\"\"\n\n" +
            "ANTWORT DES KLIENTEN (Transkript der Aufnahme):\n\"\"\"\n" +
            (string.IsNullOrWhiteSpace(transcript) ? "(keine erkennbare Antwort)" : transcript.Trim()) +
            "\n\"\"\"\n\n" +
            "KONTEXT AUS DEM LEITFADEN:\n\"\"\"\n" + Shorten(leitfaden, 6000) + "\n\"\"\"";
    }

    /// <summary>Systemkontext fuer die Korrektur automatischer Transkripte.</summary>
    public const string TranscriptCorrectionSystem =
        "Du korrigierst automatische Spracherkennungen (Transkripte gesprochener Sprache). " +
        "Du verbesserst Rechtschreibung, Grammatik und Zeichensetzung und sorgst dafuer, dass die " +
        "Saetze sprachlich korrekt sind und Sinn ergeben. Du behaeltst den Inhalt und die Aussage " +
        "exakt bei, erfindest nichts hinzu und laesst nichts weg.";

    /// <summary>
    /// Prompt zur Korrektur eines Transkripts. Antwort = ausschliesslich der korrigierte Text.
    /// </summary>
    public static string BuildTranscriptCorrectionPrompt(string transcript, string language)
    {
        var lang = string.IsNullOrWhiteSpace(language) ? "Deutsch" : language.Trim();

        return
            "Der folgende Text stammt aus einer automatischen Spracherkennung und kann Fehler, " +
            "falsch erkannte Woerter und fehlende Satzzeichen enthalten.\n\n" +
            "Aufgabe: Korrigiere Rechtschreibung, Grammatik und Zeichensetzung, ergaenze sinnvolle " +
            "Satzzeichen und sorge dafuer, dass die Saetze fluessig und sinnvoll sind. " +
            "Korrigiere offensichtliche Erkennungsfehler anhand des Zusammenhangs. " +
            "Aendere den Inhalt NICHT, kuerze nicht und erfinde nichts hinzu.\n\n" +
            $"Der Text ist in folgender Sprache; antworte ausschliesslich in dieser Sprache: {lang}.\n\n" +
            "Gib NUR den korrigierten Text zurueck, ohne Einleitung, ohne Anmerkungen, ohne Anfuehrungszeichen.\n\n" +
            "TEXT:\n\"\"\"\n" + transcript.Trim() + "\n\"\"\"";
    }

    private static string Describe(QuestionCategory c) => c switch
    {
        QuestionCategory.Alkohol => "Alkoholdelikt",
        QuestionCategory.Drogen => "Drogenkonsum",
        QuestionCategory.Verkehrsdelikte => "Verkehrsdelikte",
        QuestionCategory.Straftaten => "Straftaten",
        QuestionCategory.Konsumvorgeschichte => "Konsumvorgeschichte",
        QuestionCategory.MotiveUndHintergruende => "Motive und Hintergruende",
        QuestionCategory.Veraenderung => "Veraenderung",
        QuestionCategory.Rueckfallvermeidung => "Rueckfallvermeidung",
        QuestionCategory.Abstinenz => "Abstinenz",
        QuestionCategory.EinsichtUndVerantwortung => "Einsicht und Verantwortungsuebernahme",
        _ => "Allgemein"
    };

    private static string Shorten(string text, int max)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Length <= max ? text : text[..max] + "\n[... gekuerzt ...]";
    }
}
