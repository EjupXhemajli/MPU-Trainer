using System.IO;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using MpuTrainer.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace MpuTrainer.DocumentProcessing;

public interface IDocumentExtractionService
{
    /// <summary>Extrahiert Text aus einer .docx- oder .pdf-Datei.</summary>
    DocumentExtractionResult Extract(string filePath);
}

/// <summary>
/// Liest Text aus Word- und PDF-Dateien. Erkennt heuristisch, ob ein PDF
/// vermutlich gescannt ist (kaum extrahierbarer Text) und meldet dies.
/// </summary>
public class DocumentExtractionService : IDocumentExtractionService
{
    public DocumentExtractionResult Extract(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Datei nicht gefunden.", filePath);

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".docx" => ExtractDocx(filePath),
            ".pdf" => ExtractPdf(filePath),
            ".txt" or ".text" or ".md" or ".markdown" or ".csv" => ExtractPlainText(filePath),
            _ => throw new NotSupportedException(
                "Nicht unterstuetztes Format. Moeglich sind Word (.docx), PDF (.pdf) " +
                "und Textdateien (.txt, .md, .csv).")
        };
    }

    // ---- Textdatei -----------------------------------------------------

    private static DocumentExtractionResult ExtractPlainText(string path)
    {
        var text = File.ReadAllText(path).Trim();
        return new DocumentExtractionResult
        {
            Text = text,
            Format = "TEXT",
            IsLikelyScanned = false
        };
    }

    // ---- DOCX ----------------------------------------------------------

    private static DocumentExtractionResult ExtractDocx(string path)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        var sb = new StringBuilder();

        if (body is not null)
        {
            // Absaetze und Tabellen in Dokumentreihenfolge durchlaufen.
            foreach (var element in body.Elements())
            {
                switch (element)
                {
                    case Paragraph p:
                        var line = p.InnerText;
                        if (!string.IsNullOrWhiteSpace(line))
                            sb.AppendLine(line);
                        break;

                    case Table table:
                        foreach (var row in table.Elements<TableRow>())
                        {
                            var cells = row.Elements<TableCell>()
                                           .Select(c => c.InnerText.Trim());
                            sb.AppendLine(string.Join(" | ", cells));
                        }
                        sb.AppendLine();
                        break;
                }
            }
        }

        return new DocumentExtractionResult
        {
            Text = sb.ToString().Trim(),
            Format = "DOCX",
            IsLikelyScanned = false
        };
    }

    // ---- PDF -----------------------------------------------------------

    private static DocumentExtractionResult ExtractPdf(string path)
    {
        var sb = new StringBuilder();
        int pageCount;

        using (var pdf = PdfDocument.Open(path))
        {
            pageCount = pdf.NumberOfPages;
            foreach (var page in pdf.GetPages())
            {
                string pageText;
                try
                {
                    // Inhaltsbasierte Extraktion liefert sauberere Wortabstaende.
                    pageText = ContentOrderTextExtractor.GetText(page);
                }
                catch
                {
                    pageText = page.Text;
                }

                if (!string.IsNullOrWhiteSpace(pageText))
                    sb.AppendLine(pageText.Trim());
            }
        }

        var text = sb.ToString().Trim();

        // Heuristik: sehr wenig Text pro Seite deutet auf ein gescanntes PDF hin.
        bool likelyScanned = pageCount > 0 && text.Length < pageCount * 20;

        return new DocumentExtractionResult
        {
            Text = text,
            Format = "PDF",
            IsLikelyScanned = likelyScanned
        };
    }
}
