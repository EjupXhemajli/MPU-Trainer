using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MpuTrainer.Data;
using MpuTrainer.Models;
using MpuTrainer.Services;

namespace MpuTrainer.ViewModels;

/// <summary>
/// Startseite: neues Projekt mit Klientendaten (Vorname, Familienname,
/// Geburtsdatum) und Fragenanzahl anlegen oder bestehende Projekte oeffnen.
/// </summary>
public partial class DashboardViewModel : ViewModelBase
{
    private readonly IProjectStore _store;
    private readonly IAppSession _session;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialog;
    private readonly ISettingsService _settings;

    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(CreateProjectCommand))]
    private string _firstName = string.Empty;

    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(CreateProjectCommand))]
    private string _lastName = string.Empty;

    [ObservableProperty]
    private DateTime? _birthDate;

    [ObservableProperty]
    private string _projectName = string.Empty;

    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(CreateProjectCommand))]
    private int _questionCount = 25;

    [ObservableProperty]
    private QuestionCategory _focusCategory = QuestionCategory.Allgemein;

    [ObservableProperty]
    private string _selectedLanguage = "Deutsch";

    /// <summary>Steuert die Hauptmaske: false = Moduswahl, true = Trainings-Setup-Formular.</summary>
    [ObservableProperty]
    private bool _showTrainingSetup;

    public IReadOnlyList<QuestionCategory> Categories { get; } =
        (QuestionCategory[])Enum.GetValues(typeof(QuestionCategory));

    public IReadOnlyList<string> LanguageOptions => Languages.All;

    public ObservableCollection<ClientProject> RecentProjects { get; } = new();

    public DashboardViewModel(IProjectStore store, IAppSession session,
        INavigationService navigation, IDialogService dialog, ISettingsService settings)
    {
        _store = store;
        _session = session;
        _navigation = navigation;
        _dialog = dialog;
        _settings = settings;

        _ = LoadRecentAsync();
    }

    private async Task LoadRecentAsync()
    {
        try
        {
            RecentProjects.Clear();
            foreach (var p in await _store.GetRecentAsync(10))
                RecentProjects.Add(p);
        }
        catch (Exception ex)
        {
            _dialog.Error($"Projekte konnten nicht geladen werden: {ex.Message}");
        }
    }

    // ---- Moduswahl auf der Hauptmaske ---------------------------------

    /// <summary>Trainingsmodus gewaehlt: Setup-Formular fuer ein Trainingsprojekt anzeigen.</summary>
    [RelayCommand]
    private void ChooseTraining() => ShowTrainingSetup = true;

    /// <summary>Begutachtung gewaehlt: in den Gutachter-Modus wechseln.</summary>
    [RelayCommand]
    private void ChooseBegutachtung() => _navigation.NavigateTo<GutachterViewModel>();

    /// <summary>Zurueck von Setup zur Moduswahl.</summary>
    [RelayCommand]
    private void BackToChoice() => ShowTrainingSetup = false;

    private bool CanCreate() =>
        !string.IsNullOrWhiteSpace(FirstName) &&
        !string.IsNullOrWhiteSpace(LastName) &&
        QuestionCount is >= 1 and <= 200;

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateProjectAsync()
    {
        var name = string.IsNullOrWhiteSpace(ProjectName)
            ? $"{LastName}, {FirstName}"
            : ProjectName.Trim();

        var project = new ClientProject
        {
            Name = name,
            Client = new Client
            {
                FirstName = FirstName.Trim(),
                LastName = LastName.Trim(),
                BirthDate = BirthDate
            },
            QuestionCount = QuestionCount,
            FocusCategory = FocusCategory,
            Language = string.IsNullOrWhiteSpace(SelectedLanguage) ? "Deutsch" : SelectedLanguage
        };

        var saved = await _store.AddAsync(project);
        _session.CurrentProject = saved;
        RememberLastProject(saved.Id);

        // Direkt zum Dokument-Upload weitergehen.
        _navigation.NavigateTo<DocumentUploadViewModel>();
    }

    [RelayCommand]
    private async Task OpenProjectAsync(ClientProject? project)
    {
        if (project is null) return;

        var full = await _store.GetAsync(project.Id);
        if (full is null)
        {
            _dialog.Error("Projekt konnte nicht geladen werden.");
            return;
        }

        _session.CurrentProject = full;
        RememberLastProject(full.Id);
        _navigation.NavigateTo<QuestionsViewModel>();
    }

    /// <summary>Merkt sich das zuletzt geoeffnete Projekt fuer den naechsten Start.</summary>
    private void RememberLastProject(int id)
    {
        try
        {
            _settings.Current.LastProjectId = id;
            _settings.Save();
        }
        catch { /* nicht kritisch */ }
    }

    [RelayCommand]
    private async Task DeleteProjectAsync(ClientProject? project)
    {
        if (project is null) return;

        if (!_dialog.Confirm(
            $"Projekt \"{project.Name}\" wirklich endgueltig loeschen?\n\n" +
            "Alle Fragen, Aufnahmen, Transkripte und Auswertungen dieses Projekts werden entfernt.",
            "Projekt loeschen"))
            return;

        try
        {
            await _store.DeleteAsync(project.Id);

            // Projektordner (Aufnahmen, Sprachausgaben, MP3s) ebenfalls entfernen.
            try
            {
                var dir = ProjectFolder(project.Id);
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
            catch { /* Dateien nicht kritisch - DB-Eintrag ist bereits weg */ }

            // War das Projekt geoeffnet oder gemerkt? Dann zuruecksetzen.
            if (_session.CurrentProject?.Id == project.Id)
                _session.CurrentProject = null;
            if (_settings.Current.LastProjectId == project.Id)
            {
                _settings.Current.LastProjectId = null;
                _settings.Save();
            }

            await LoadRecentAsync();
        }
        catch (Exception ex)
        {
            _dialog.Error($"Projekt konnte nicht geloescht werden: {ex.Message}");
        }
    }

    private string ProjectFolder(int id)
    {
        var root = string.IsNullOrWhiteSpace(_settings.Current.ProjectsDirectory)
            ? Path.Combine(App.DataDirectory, "Projekte")
            : _settings.Current.ProjectsDirectory;
        return Path.Combine(root, $"projekt_{id:D4}");
    }
}
