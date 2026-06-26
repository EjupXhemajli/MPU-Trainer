using System.Collections.Generic;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MpuTrainer.AI;
using MpuTrainer.Data;
using MpuTrainer.DocumentProcessing;
using MpuTrainer.Models;
using MpuTrainer.Services;

namespace MpuTrainer.ViewModels;

/// <summary>
/// Dokument hochladen, Text extrahieren, Vorschau anzeigen und daraus die
/// vom Psychologen festgelegte Anzahl an Trainingsfragen generieren lassen.
/// </summary>
public partial class DocumentUploadViewModel : ViewModelBase
{
    private readonly IDocumentExtractionService _extraction;
    private readonly IQuestionGenerationService _generation;
    private readonly IProjectStore _store;
    private readonly IAppSession _session;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialog;

    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    private string _previewText = string.Empty;

    [ObservableProperty] private string _selectedFilePath = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _scannedWarning;

    // Generierungs-Einstellungen des Projekts (nach Erstellung aenderbar).
    [ObservableProperty] private string _selectedLanguage = "Deutsch";
    [ObservableProperty] private QuestionCategory _focusCategory = QuestionCategory.Allgemein;
    [ObservableProperty] private int _questionCount = 10;

    public IReadOnlyList<string> LanguageOptions => Languages.All;
    public IReadOnlyList<QuestionCategory> FocusOptions { get; } =
        (QuestionCategory[])Enum.GetValues(typeof(QuestionCategory));

    /// <summary>Verhindert das Speichern waehrend des erstmaligen Ladens der Werte.</summary>
    private bool _loading;

    public DocumentUploadViewModel(IDocumentExtractionService extraction,
        IQuestionGenerationService generation, IProjectStore store,
        IAppSession session, INavigationService navigation, IDialogService dialog)
    {
        _extraction = extraction;
        _generation = generation;
        _store = store;
        _session = session;
        _navigation = navigation;
        _dialog = dialog;

        var project = _session.CurrentProject;
        if (project is not null)
        {
            _loading = true;
            PreviewText = project.LeitfadenText;
            SelectedFilePath = project.SourceDocumentPath ?? string.Empty;
            SelectedLanguage = string.IsNullOrWhiteSpace(project.Language) ? "Deutsch" : project.Language;
            FocusCategory = project.FocusCategory;
            QuestionCount = project.QuestionCount < 1 ? 10 : project.QuestionCount;
            _loading = false;
        }
    }

    partial void OnSelectedLanguageChanged(string value) => PersistGenerationSettings();
    partial void OnFocusCategoryChanged(QuestionCategory value) => PersistGenerationSettings();
    partial void OnQuestionCountChanged(int value) => PersistGenerationSettings();

    /// <summary>Schreibt Sprache, Schwerpunkt und Anzahl in das Projekt zurueck.</summary>
    private void PersistGenerationSettings()
    {
        if (_loading) return;
        var p = _session.CurrentProject;
        if (p is null) return;

        p.Language = string.IsNullOrWhiteSpace(SelectedLanguage) ? "Deutsch" : SelectedLanguage;
        p.FocusCategory = FocusCategory;
        p.QuestionCount = QuestionCount < 1 ? 1 : (QuestionCount > 80 ? 80 : QuestionCount);

        _ = SaveProjectAsync(p);
    }

    private async Task SaveProjectAsync(ClientProject p)
    {
        try { await _store.UpdateAsync(p); }
        catch { /* nicht kritisch fuer die Bedienung */ }
    }

    [RelayCommand]
    private async Task ChooseFileAsync()
    {
        var project = _session.CurrentProject;
        if (project is null)
        {
            _dialog.Error("Bitte zuerst ein Projekt anlegen oder oeffnen.");
            _navigation.NavigateTo<DashboardViewModel>();
            return;
        }

        var path = _dialog.OpenFile(
            "Dokumente (*.docx;*.pdf;*.txt;*.md;*.csv)|*.docx;*.pdf;*.txt;*.md;*.csv|" +
            "Word (*.docx)|*.docx|PDF (*.pdf)|*.pdf|Text (*.txt;*.md;*.csv)|*.txt;*.md;*.csv|" +
            "Alle Dateien (*.*)|*.*",
            "Dokument zum Leitfaden hinzufuegen");
        if (path is null) return;

        try
        {
            IsBusy = true;
            StatusMessage = "Text wird extrahiert ...";
            ScannedWarning = false;

            var result = _extraction.Extract(path);

            if (result.IsLikelyScanned || string.IsNullOrWhiteSpace(result.Text))
            {
                ScannedWarning = true;
                StatusMessage =
                    "Es konnte kaum Text erkannt werden. Das PDF ist vermutlich gescannt " +
                    "(Bild ohne Textebene). Bitte eine Datei mit Textebene verwenden.";
                return;
            }

            // An den bestehenden Leitfaden anhaengen - so lassen sich mehrere Dokumente
            // pro Projekt sammeln. Keine Laengenbegrenzung.
            var name = Path.GetFileName(path);
            project.LeitfadenText = string.IsNullOrWhiteSpace(project.LeitfadenText)
                ? result.Text
                : project.LeitfadenText.TrimEnd() + $"\n\n===== Dokument: {name} =====\n\n" + result.Text;

            project.SourceDocumentPath = path;
            PreviewText = project.LeitfadenText;
            SelectedFilePath = name;
            await _store.UpdateAsync(project);

            StatusMessage = $"\"{name}\" hinzugefuegt ({result.CharacterCount} Zeichen). " +
                            $"Leitfaden gesamt: {project.LeitfadenText.Length} Zeichen.";
        }
        catch (Exception ex)
        {
            _dialog.Error($"Datei konnte nicht gelesen werden: {ex.Message}");
            StatusMessage = "Fehler beim Lesen der Datei.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ClearLeitfadenAsync()
    {
        var project = _session.CurrentProject;
        if (project is null) return;

        if (!_dialog.Confirm(
            "Den gesamten Leitfaden-Text dieses Projekts leeren? Die hinzugefuegten Dokumente " +
            "werden dadurch entfernt (die Originaldateien bleiben unberuehrt).",
            "Leitfaden leeren"))
            return;

        project.LeitfadenText = string.Empty;
        project.SourceDocumentPath = null;
        PreviewText = string.Empty;
        SelectedFilePath = string.Empty;
        StatusMessage = "Leitfaden geleert.";
        await _store.UpdateAsync(project);
    }

    private bool CanGenerate() => !string.IsNullOrWhiteSpace(PreviewText) && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateAsync()
    {
        var project = _session.CurrentProject;
        if (project is null) return;

        try
        {
            IsBusy = true;
            StatusMessage = $"KI generiert {project.QuestionCount} Fragen ...";

            var generated = await _generation.GenerateQuestionsAsync(
                project.LeitfadenText, project.QuestionCount, project.FocusCategory, project.Language);

            if (generated.Count == 0)
            {
                _dialog.Error("Es konnten keine Fragen erzeugt werden. " +
                              "Bitte API-Einstellungen und Leitfaden pruefen.");
                StatusMessage = "Keine Fragen erhalten.";
                return;
            }

            var questions = new List<TrainingQuestion>();
            int order = 1;
            foreach (var g in generated)
            {
                questions.Add(new TrainingQuestion
                {
                    Order = order++,
                    Text = g.Text,
                    Category = g.Category,
                    Difficulty = g.Difficulty,
                    ModelAnswer = g.ModelAnswer,
                    Status = QuestionStatus.Offen
                });
            }

            await _store.ReplaceQuestionsAsync(project.Id, questions);

            // Projekt mit gespeicherten Fragen neu laden.
            var reloaded = await _store.GetAsync(project.Id);
            if (reloaded is not null) _session.CurrentProject = reloaded;

            StatusMessage = $"{questions.Count} Fragen erstellt.";
            _navigation.NavigateTo<QuestionsViewModel>();
        }
        catch (Exception ex)
        {
            _dialog.Error($"Fehler bei der Fragegenerierung: {ex.Message}");
            StatusMessage = "Fehler bei der Generierung.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
