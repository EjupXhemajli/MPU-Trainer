using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MpuTrainer.AI;
using MpuTrainer.Data;
using MpuTrainer.Models;
using MpuTrainer.Services;

namespace MpuTrainer.ViewModels;

/// <summary>
/// Fragenuebersicht: generierte Fragen anzeigen, bearbeiten, loeschen,
/// ergaenzen oder neu generieren. Auswahllisten fuer Kategorie, Schwierigkeit
/// und Status werden den Spalten der Tabelle bereitgestellt.
/// </summary>
public partial class QuestionsViewModel : ViewModelBase
{
    private readonly IProjectStore _store;
    private readonly IAppSession _session;
    private readonly INavigationService _navigation;
    private readonly IQuestionGenerationService _generation;
    private readonly IDialogService _dialog;
    private readonly IWordExportService _wordExport;

    public ObservableCollection<TrainingQuestion> Questions { get; } = new();

    public IReadOnlyList<QuestionCategory> Categories { get; } =
        (QuestionCategory[])Enum.GetValues(typeof(QuestionCategory));
    public IReadOnlyList<DifficultyLevel> Difficulties { get; } =
        (DifficultyLevel[])Enum.GetValues(typeof(DifficultyLevel));
    public IReadOnlyList<QuestionStatus> Statuses { get; } =
        (QuestionStatus[])Enum.GetValues(typeof(QuestionStatus));

    [ObservableProperty] private string _projectTitle = string.Empty;
    [ObservableProperty] private bool _isBusy;

    public QuestionsViewModel(IProjectStore store, IAppSession session,
        INavigationService navigation, IQuestionGenerationService generation,
        IDialogService dialog, IWordExportService wordExport)
    {
        _store = store;
        _session = session;
        _navigation = navigation;
        _generation = generation;
        _dialog = dialog;
        _wordExport = wordExport;

        LoadFromSession();
    }

    private void LoadFromSession()
    {
        Questions.Clear();
        var project = _session.CurrentProject;
        if (project is null)
        {
            ProjectTitle = "Kein Projekt geoeffnet";
            return;
        }

        ProjectTitle = $"{project.Name} - {project.Client.FullName}";
        foreach (var q in project.Questions.OrderBy(q => q.Order))
            Questions.Add(q);
    }

    [RelayCommand]
    private void AddQuestion()
    {
        var nextOrder = Questions.Count == 0 ? 1 : Questions.Max(q => q.Order) + 1;
        Questions.Add(new TrainingQuestion
        {
            Order = nextOrder,
            Text = "Neue Frage",
            Category = QuestionCategory.Allgemein,
            Difficulty = DifficultyLevel.Mittel,
            Status = QuestionStatus.Offen
        });
    }

    [RelayCommand]
    private void DeleteQuestion(TrainingQuestion? question)
    {
        if (question is null) return;
        Questions.Remove(question);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var project = _session.CurrentProject;
        if (project is null) return;

        try
        {
            IsBusy = true;

            // Reihenfolge neu vergeben und speichern.
            int order = 1;
            foreach (var q in Questions) q.Order = order++;

            await _store.ReplaceQuestionsAsync(project.Id, Questions.ToList());

            var reloaded = await _store.GetAsync(project.Id);
            if (reloaded is not null)
            {
                _session.CurrentProject = reloaded;
                LoadFromSession();
            }

            _dialog.Info("Fragen gespeichert.");
        }
        catch (Exception ex)
        {
            _dialog.Error($"Speichern fehlgeschlagen: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ExportWordAsync()
    {
        var project = _session.CurrentProject;
        if (project is null || Questions.Count == 0)
        {
            _dialog.Info("Es sind keine Fragen vorhanden, die gespeichert werden koennen.");
            return;
        }

        var safe = MakeFileSafe(project.Name);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
        var target = _dialog.SaveFile(
            "Word-Dokument (*.docx)|*.docx",
            "Fragen und Musterantworten als Word speichern",
            $"MPU-Fragen_{safe}_{stamp}.docx");
        if (target is null) return;

        try
        {
            IsBusy = true;
            await _wordExport.ExportQuestionsAsync(project, Questions.ToList(), target);
            _dialog.Info($"Die Fragen wurden als Word gespeichert:\n\n{target}");
        }
        catch (Exception ex)
        {
            _dialog.Error($"Word-Dokument konnte nicht erstellt werden: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string MakeFileSafe(string name)
    {
        foreach (var c in System.IO.Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "Projekt" : name.Trim();
    }

    [RelayCommand]
    private async Task RegenerateAllAsync()
    {
        var project = _session.CurrentProject;
        if (project is null) return;

        if (string.IsNullOrWhiteSpace(project.LeitfadenText))
        {
            _dialog.Error("Kein Leitfaden-Text vorhanden. Bitte zuerst ein Dokument hochladen.");
            return;
        }

        if (!_dialog.Confirm(
            $"Alle Fragen durch {project.QuestionCount} neu generierte ersetzen?",
            "Neu generieren"))
            return;

        try
        {
            IsBusy = true;
            var generated = await _generation.GenerateQuestionsAsync(
                project.LeitfadenText, project.QuestionCount, project.FocusCategory, project.Language);

            if (generated.Count == 0)
            {
                _dialog.Error("Es konnten keine Fragen erzeugt werden.");
                return;
            }

            var list = new List<TrainingQuestion>();
            int order = 1;
            foreach (var g in generated)
            {
                list.Add(new TrainingQuestion
                {
                    Order = order++,
                    Text = g.Text,
                    Category = g.Category,
                    Difficulty = g.Difficulty,
                    ModelAnswer = g.ModelAnswer,
                    Status = QuestionStatus.Offen
                });
            }

            await _store.ReplaceQuestionsAsync(project.Id, list);
            var reloaded = await _store.GetAsync(project.Id);
            if (reloaded is not null)
            {
                _session.CurrentProject = reloaded;
                LoadFromSession();
            }
        }
        catch (Exception ex)
        {
            _dialog.Error($"Fehler: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void StartTraining()
    {
        if (Questions.Count == 0)
        {
            _dialog.Error("Es sind keine Fragen vorhanden.");
            return;
        }
        _navigation.NavigateTo<TrainingViewModel>();
    }
}
