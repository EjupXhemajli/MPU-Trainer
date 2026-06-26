using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MpuTrainer.Services;

namespace MpuTrainer.ViewModels;

/// <summary>
/// Steuert die Navigation und haelt das aktuell angezeigte ViewModel. Reagiert
/// auf Projektwechsel, um den Projektnamen in der Seitenleiste zu aktualisieren.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly INavigationService _navigation;
    private readonly IAppSession _session;

    [ObservableProperty]
    private ViewModelBase? _currentViewModel;

    [ObservableProperty]
    private string _currentProjectName = "Kein Projekt geoeffnet";

    /// <summary>Kennzeichnet die aktuell aktive Seite zur Hervorhebung in der Navigation.</summary>
    [ObservableProperty]
    private string _activePage = "Dashboard";

    partial void OnCurrentViewModelChanged(ViewModelBase? value)
    {
        ActivePage = value switch
        {
            DashboardViewModel => "Dashboard",
            DocumentUploadViewModel => "Upload",
            QuestionsViewModel => "Questions",
            TrainingViewModel => "Training",
            GutachterViewModel => "Gutachter",
            SettingsViewModel => "Settings",
            _ => ActivePage
        };
    }

    public MainViewModel(INavigationService navigation, IAppSession session)
    {
        _navigation = navigation;
        _session = session;

        _navigation.CurrentChanged += vm => CurrentViewModel = vm;
        _session.CurrentProjectChanged += UpdateProjectName;

        // Startseite anzeigen.
        _navigation.NavigateTo<DashboardViewModel>();
    }

    private void UpdateProjectName()
    {
        var p = _session.CurrentProject;
        CurrentProjectName = p is null
            ? "Kein Projekt geoeffnet"
            : $"Projekt: {p.Name}\n{p.Client.FullName}";
    }

    [RelayCommand] private void GoDashboard() => _navigation.NavigateTo<DashboardViewModel>();
    [RelayCommand] private void GoUpload() => _navigation.NavigateTo<DocumentUploadViewModel>();
    [RelayCommand] private void GoQuestions() => _navigation.NavigateTo<QuestionsViewModel>();
    [RelayCommand] private void GoTraining() => _navigation.NavigateTo<TrainingViewModel>();
    [RelayCommand] private void GoGutachter() => _navigation.NavigateTo<GutachterViewModel>();
    [RelayCommand] private void GoSettings() => _navigation.NavigateTo<SettingsViewModel>();
}
