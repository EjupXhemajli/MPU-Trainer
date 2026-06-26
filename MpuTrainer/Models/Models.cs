using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;

namespace MpuTrainer.Models;

// ============================================================
//  Aufzaehlungen mit deutschen Anzeigetexten ([Description])
// ============================================================

/// <summary>Themenkategorie einer Trainingsfrage.</summary>
public enum QuestionCategory
{
    [Description("Allgemein")] Allgemein,
    [Description("Alkoholdelikt")] Alkohol,
    [Description("Drogenkonsum")] Drogen,
    [Description("Verkehrsdelikte")] Verkehrsdelikte,
    [Description("Straftaten")] Straftaten,
    [Description("Konsumvorgeschichte")] Konsumvorgeschichte,
    [Description("Motive und Hintergruende")] MotiveUndHintergruende,
    [Description("Veraenderung")] Veraenderung,
    [Description("Rueckfallvermeidung")] Rueckfallvermeidung,
    [Description("Abstinenz")] Abstinenz,
    [Description("Einsicht und Verantwortung")] EinsichtUndVerantwortung
}

/// <summary>Bearbeitungsstatus einer Frage im Training.</summary>
public enum QuestionStatus
{
    [Description("Offen")] Offen,
    [Description("Geuebt")] Geuebt,
    [Description("Abgeschlossen")] Abgeschlossen
}

/// <summary>Unterstuetzte KI-Anbieter.</summary>
public enum AiProvider
{
    [Description("Anthropic Claude")] Anthropic,
    [Description("OpenAI-kompatibel")] OpenAiCompatible
}

/// <summary>Anbieter fuer die Sprachausgabe (Text-to-Speech).</summary>
public enum TtsProvider
{
    [Description("Windows (lokal, kostenlos)")] Windows,
    [Description("OpenAI (Premium)")] OpenAI,
    [Description("ElevenLabs (Premium, beste Qualitaet)")] ElevenLabs
}

/// <summary>Schwierigkeitsgrad einer Frage.</summary>
public enum DifficultyLevel
{
    [Description("Leicht")] Leicht,
    [Description("Mittel")] Mittel,
    [Description("Schwer")] Schwer
}

// ============================================================
//  Persistente Entitaeten (EF Core / SQLite)
// ============================================================

/// <summary>Stammdaten des Klienten (als Owned Entity im Projekt gespeichert).</summary>
public class Client
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public DateTime? BirthDate { get; set; }

    /// <summary>Anzeigename, nicht in der Datenbank gespeichert.</summary>
    [NotMapped]
    public string FullName =>
        string.Join(" ", new[] { FirstName, LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

/// <summary>Ein Trainingsprojekt fuer genau einen Klienten.</summary>
public class ClientProject
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Klientendaten (Vorname, Familienname, Geburtsdatum).</summary>
    public Client Client { get; set; } = new();

    /// <summary>Vom Psychologen festgelegte Anzahl zu generierender Fragen.</summary>
    public int QuestionCount { get; set; } = 25;

    /// <summary>Optionaler thematischer Schwerpunkt der Fragegenerierung.</summary>
    public QuestionCategory FocusCategory { get; set; } = QuestionCategory.Allgemein;

    /// <summary>Sprache, in der Fragen und Musterantworten erzeugt und vorgelesen werden.</summary>
    public string Language { get; set; } = "Deutsch";

    /// <summary>Aus dem Dokument extrahierter Leitfaden-Text.</summary>
    public string LeitfadenText { get; set; } = string.Empty;

    public string? SourceDocumentPath { get; set; }

    /// <summary>Pfad der zusammengefuegten Sitzungs-MP3 (gesamte Unterhaltung).</summary>
    public string? SessionMp3Path { get; set; }

    public string Notes { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public List<TrainingQuestion> Questions { get; set; } = new();
}

/// <summary>Einzelne Trainingsfrage inklusive Musterantwort und Aufnahmepfad.</summary>
public class TrainingQuestion
{
    public int Id { get; set; }
    public int ProjectId { get; set; }

    public int Order { get; set; }
    public string Text { get; set; } = string.Empty;
    public QuestionCategory Category { get; set; } = QuestionCategory.Allgemein;
    public DifficultyLevel Difficulty { get; set; } = DifficultyLevel.Mittel;
    public QuestionStatus Status { get; set; } = QuestionStatus.Offen;

    /// <summary>Musterantwort in Ich-Form (kann nachtraeglich generiert werden).</summary>
    public string? ModelAnswer { get; set; }

    /// <summary>Pfad zur Audioaufnahme der Klientenantwort.</summary>
    public string? RecordingPath { get; set; }

    /// <summary>Pfad zur Aufnahme der Klientenantwort als MP3 (zum Anhoeren/Teilen).</summary>
    public string? RecordingMp3Path { get; set; }

    /// <summary>Transkript der aufgenommenen Klientenantwort (per Spracherkennung).</summary>
    public string? Transcript { get; set; }

    /// <summary>Fachliche Auswertung der Antwort (Fazit, Schwaechen, Defizite, Verbesserungen) als Text.</summary>
    public string? Evaluation { get; set; }
}

// ============================================================
//  Hilfs-/Transportobjekte (nicht persistent)
// ============================================================

/// <summary>Von der KI geliefertes Roh-Fragenobjekt vor dem Speichern.</summary>
public class GeneratedQuestion
{
    public string Text { get; set; } = string.Empty;
    public QuestionCategory Category { get; set; } = QuestionCategory.Allgemein;
    public DifficultyLevel Difficulty { get; set; } = DifficultyLevel.Mittel;
    public string? ModelAnswer { get; set; }
}

/// <summary>Ergebnis der Textextraktion aus einem Dokument.</summary>
public class DocumentExtractionResult
{
    public string Text { get; set; } = string.Empty;
    public bool IsLikelyScanned { get; set; }
    public string Format { get; set; } = string.Empty;
    public int CharacterCount => Text?.Length ?? 0;
}

/// <summary>Ein erkanntes Audiogeraet (Mikrofon oder Lautsprecher).</summary>
public class AudioDevice
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }

    public override string ToString() => IsDefault ? $"{Name} (Standard)" : Name;
}

// ============================================================
//  Anwendungseinstellungen (als JSON gespeichert)
// ============================================================

/// <summary>Persistente Benutzereinstellungen (ohne API-Key; dieser liegt verschluesselt separat).</summary>
public class AppSettings
{
    // Audio
    public string? MicrophoneId { get; set; }
    public string? SpeakerId { get; set; }
    public double Volume { get; set; } = 0.8;
    public string? VoiceName { get; set; }

    // Sprachausgabe (Text-to-Speech)
    public TtsProvider TtsProvider { get; set; } = TtsProvider.Windows;
    public string OpenAiTtsModel { get; set; } = "tts-1-hd";
    public string OpenAiTtsVoice { get; set; } = "alloy";
    public string ElevenLabsModel { get; set; } = "eleven_multilingual_v2";
    public string ElevenLabsVoiceId { get; set; } = string.Empty;
    public string ElevenLabsVoiceName { get; set; } = string.Empty;

    // KI
    public AiProvider Provider { get; set; } = AiProvider.Anthropic;
    public string Model { get; set; } = "claude-sonnet-4-6";
    public string BaseUrl { get; set; } = string.Empty;
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 2000;

    // Allgemein
    public string Language { get; set; } = "de-DE";
    public string ProjectsDirectory { get; set; } = string.Empty;

    /// <summary>Schluessel des gewaehlten Hintergrunddesigns (siehe ThemeService).</summary>
    public string BackgroundTheme { get; set; } = "neutral";

    /// <summary>Id des zuletzt geoeffneten Projekts (zum Wiederherstellen nach Neustart).</summary>
    public int? LastProjectId { get; set; }

    /// <summary>Begruessung ("Willkommen bei der BfK") beim Start abspielen.</summary>
    public bool PlayWelcomeOnStartup { get; set; } = true;
}

// ============================================================
//  Sprachenliste fuer die multilinguale Fragegenerierung
// ============================================================

/// <summary>Auswaehlbare Sprachen (deutsche Bezeichnung) fuer Fragen und Sprachausgabe.</summary>
public static class Languages
{
    public static readonly IReadOnlyList<string> All = new[]
    {
        "Deutsch", "Englisch", "Albanisch", "Griechisch", "Serbisch", "Kroatisch",
        "Bosnisch", "Slowenisch", "Mazedonisch", "Russisch", "Ukrainisch", "Polnisch",
        "Tschechisch", "Slowakisch", "Italienisch", "Spanisch", "Portugiesisch",
        "Franzoesisch", "Niederlaendisch", "Tuerkisch", "Arabisch", "Kurdisch",
        "Persisch (Farsi)", "Rumaenisch", "Bulgarisch", "Ungarisch", "Litauisch",
        "Lettisch", "Estnisch", "Finnisch", "Schwedisch", "Daenisch", "Norwegisch",
        "Chinesisch", "Japanisch", "Vietnamesisch", "Hindi", "Urdu", "Thailaendisch",
        "Georgisch", "Armenisch"
    };
}
