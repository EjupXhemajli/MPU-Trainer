using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using MpuTrainer.Models;

namespace MpuTrainer.Services;

public interface IWordExportService
{
    /// <summary>Speichert die gesamte Sitzung (Fragen, Musterantworten, Antworten, Auswertungen) als Word.</summary>
    Task ExportSessionAsync(ClientProject project, IReadOnlyList<TrainingQuestion> questions, string path);

    /// <summary>Speichert die Fragen mit Musterantworten als Word (Fragenuebersicht).</summary>
    Task ExportQuestionsAsync(ClientProject project, IReadOnlyList<TrainingQuestion> questions, string path);
}

/// <summary>
/// Erstellt Word-Dokumente (.docx) im BfK-Stil (Calibri, Dunkelblau/Mittelblau) ueber das
/// OpenXML-SDK. Wird sowohl fuer den Sitzungsexport als auch fuer die Fragenuebersicht genutzt.
/// </summary>
public class WordExportService : IWordExportService
{
    private const string DarkBlue = "1F3864";
    private const string MidBlue = "2E75B6";
    private const string Grey = "666666";
    private const string LightGrey = "999999";
    private const string Font = "Calibri";

    public Task ExportSessionAsync(ClientProject project, IReadOnlyList<TrainingQuestion> questions, string path)
        => Task.Run(() => BuildSession(project, questions, path));

    public Task ExportQuestionsAsync(ClientProject project, IReadOnlyList<TrainingQuestion> questions, string path)
        => Task.Run(() => BuildQuestions(project, questions, path));

    // ---- Aufbau der Dokumente -----------------------------------------

    private void BuildSession(ClientProject project, IReadOnlyList<TrainingQuestion> questions, string path)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var body = NewBody(doc);

        body.AppendChild(Heading("MPU-Trainingssitzung", DarkBlue, 38, true));
        body.AppendChild(Meta(project));
        body.AppendChild(Par(string.Empty));

        int i = 1;
        foreach (var q in questions)
        {
            body.AppendChild(Heading($"Frage {i} \u2014 {CategoryName(q.Category)}", MidBlue, 28, true));
            body.AppendChild(Par(q.Text));

            body.AppendChild(Label("Musterantwort:"));
            body.AppendChild(Par(string.IsNullOrWhiteSpace(q.ModelAnswer) ? "(keine Musterantwort hinterlegt)" : q.ModelAnswer!));

            if (!string.IsNullOrWhiteSpace(q.Transcript))
            {
                body.AppendChild(Label("Antwort des Klienten:"));
                body.AppendChild(Par(q.Transcript!));
            }

            if (!string.IsNullOrWhiteSpace(q.Evaluation))
            {
                body.AppendChild(Label("Auswertung:"));
                body.AppendChild(Par(q.Evaluation!));
            }

            body.AppendChild(Par(string.Empty));
            i++;
        }

        body.AppendChild(Footer());
        doc.MainDocumentPart!.Document.Save();
    }

    private void BuildQuestions(ClientProject project, IReadOnlyList<TrainingQuestion> questions, string path)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var body = NewBody(doc);

        body.AppendChild(Heading("MPU-Fragen und Musterantworten", DarkBlue, 38, true));
        body.AppendChild(Meta(project));
        body.AppendChild(Par(string.Empty));

        int i = 1;
        foreach (var q in questions)
        {
            body.AppendChild(Heading(
                $"{i}. {CategoryName(q.Category)} \u00B7 {DifficultyName(q.Difficulty)}", MidBlue, 26, true));
            body.AppendChild(Par(q.Text));

            body.AppendChild(Label("Musterantwort:"));
            body.AppendChild(Par(string.IsNullOrWhiteSpace(q.ModelAnswer) ? "(keine Musterantwort hinterlegt)" : q.ModelAnswer!));

            body.AppendChild(Par(string.Empty));
            i++;
        }

        body.AppendChild(Footer());
        doc.MainDocumentPart!.Document.Save();
    }

    // ---- Bausteine -----------------------------------------------------

    private static Body NewBody(WordprocessingDocument doc)
    {
        var main = doc.AddMainDocumentPart();
        main.Document = new Document();
        return main.Document.AppendChild(new Body());
    }

    private static Paragraph Meta(ClientProject p)
    {
        var client = p.Client?.FullName ?? string.Empty;
        var date = DateTime.Now.ToString("dd.MM.yyyy");
        var lang = string.IsNullOrWhiteSpace(p.Language) ? "Deutsch" : p.Language;
        return Par($"Projekt: {p.Name}    |    Klient: {client}    |    Sprache: {lang}    |    Datum: {date}",
                    bold: false, color: Grey, size: 18);
    }

    private static Paragraph Footer() =>
        Par("Erstellt mit dem MPU-Trainer \u2013 BfK GmbH", bold: false, color: LightGrey, size: 16);

    private static Paragraph Label(string text) => Par(text, bold: true, color: MidBlue, size: 22);

    private static Paragraph Heading(string text, string color, int sizeHalfPt, bool bold)
    {
        var p = new Paragraph();
        var pp = new ParagraphProperties();
        pp.Append(new SpacingBetweenLines { Before = "180", After = "60" });
        p.Append(pp);
        p.Append(MakeRun(text, bold, color, sizeHalfPt));
        return p;
    }

    private static Paragraph Par(string? text, bool bold = false, string? color = null, int size = 22)
    {
        var p = new Paragraph();
        var pp = new ParagraphProperties();
        pp.Append(new SpacingBetweenLines { After = "80", Line = "276", LineRule = LineSpacingRuleValues.Auto });
        p.Append(pp);
        p.Append(MakeRun(text ?? string.Empty, bold, color, size));
        return p;
    }

    /// <summary>Erzeugt einen Run; Zeilenumbrueche (\n) werden in echte Umbrueche uebersetzt.</summary>
    private static Run MakeRun(string text, bool bold, string? color, int sizeHalfPt)
    {
        var run = new Run();
        var rp = new RunProperties();
        rp.Append(new RunFonts { Ascii = Font, HighAnsi = Font, ComplexScript = Font });
        if (bold) rp.Append(new Bold());
        if (!string.IsNullOrEmpty(color)) rp.Append(new Color { Val = color });
        rp.Append(new FontSize { Val = sizeHalfPt.ToString() });
        run.Append(rp);

        var lines = text.Replace("\r", string.Empty).Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) run.Append(new Break());
            run.Append(new Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve });
        }
        return run;
    }

    // ---- Enum-Beschriftungen ------------------------------------------

    private static string CategoryName(QuestionCategory c) => Describe(c);
    private static string DifficultyName(DifficultyLevel d) => Describe(d);

    private static string Describe(Enum value)
    {
        var field = value.GetType().GetField(value.ToString());
        var attr = field?.GetCustomAttribute<DescriptionAttribute>();
        return attr?.Description ?? value.ToString();
    }
}
