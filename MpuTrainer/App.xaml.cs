using System.IO;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MpuTrainer.AI;
using MpuTrainer.Audio;
using MpuTrainer.Data;
using MpuTrainer.DocumentProcessing;
using MpuTrainer.Services;
using MpuTrainer.ViewModels;

namespace MpuTrainer;

/// <summary>
/// Einstiegspunkt der Anwendung. Hier wird der Dependency-Injection-Container
/// aufgebaut, die lokale Datenbank initialisiert und das Hauptfenster erzeugt.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _services;

    /// <summary>Lokales Datenverzeichnis im AppData-Ordner des Benutzers.</summary>
    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MpuTrainer");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Globale Fehlerbehandlung: unerwartete Fehler als Meldung anzeigen,
        // statt die Anwendung kommentarlos zu schliessen.
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                "Es ist ein unerwarteter Fehler aufgetreten:\n\n" + args.Exception.Message,
                "MPU-Trainer", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                MessageBox.Show("Schwerer Fehler:\n\n" + ex.Message,
                    "MPU-Trainer", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        Directory.CreateDirectory(DataDirectory);

        var sc = new ServiceCollection();
        ConfigureServices(sc);
        _services = sc.BuildServiceProvider();

        // Datenbank anlegen, falls noch nicht vorhanden.
        using (var scope = _services.CreateScope())
        {
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            using var db = factory.CreateDbContext();
            db.Database.EnsureCreated();

            // Schema-Ergaenzung fuer bereits bestehende Datenbanken: neue Spalte "Language".
            // Bei neuen Datenbanken existiert die Spalte schon -> Fehler wird ignoriert.
            try
            {
                db.Database.ExecuteSqlRaw(
                    "ALTER TABLE Projects ADD COLUMN Language TEXT NOT NULL DEFAULT 'Deutsch'");
            }
            catch { /* Spalte bereits vorhanden */ }

            // Neue Spalten fuer die Antwort-Auswertung (Transkript + Bewertungstext).
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Questions ADD COLUMN Transcript TEXT"); }
            catch { /* Spalte bereits vorhanden */ }

            try { db.Database.ExecuteSqlRaw("ALTER TABLE Questions ADD COLUMN Evaluation TEXT"); }
            catch { /* Spalte bereits vorhanden */ }

            try { db.Database.ExecuteSqlRaw("ALTER TABLE Questions ADD COLUMN RecordingMp3Path TEXT"); }
            catch { /* Spalte bereits vorhanden */ }
        }

        // Einstellungen frueh laden, damit gespeicherte Geraete/Praeferenzen verfuegbar sind.
        var settings = _services.GetRequiredService<ISettingsService>();
        settings.Load();

        var window = _services.GetRequiredService<MainWindow>();
        window.DataContext = _services.GetRequiredService<MainViewModel>();

        // Gewaehltes Hintergrunddesign anwenden und bei Aenderung live aktualisieren.
        var theme = _services.GetRequiredService<IThemeService>();
        theme.Apply(settings.Current.BackgroundTheme);
        ApplyThemeVisuals(window, theme);
        theme.Changed += () => window.Dispatcher.Invoke(() => ApplyThemeVisuals(window, theme));

        window.Show();

        // Zuletzt geoeffnetes Projekt nach dem Anzeigen im Hintergrund laden (blockiert den Start nicht).
        _ = RestoreLastProjectAsync();

        // Begruessung (Melodie + Stimme) im Hintergrund abspielen; Fehler sind unkritisch.
        var welcome = _services.GetService<IWelcomeService>();
        if (welcome is not null)
            _ = Task.Run(welcome.PlayAsync);
    }

    /// <summary>
    /// Setzt Hintergrund und die Schriftfarben fuer Texte auf dem Hintergrund passend zum Design.
    /// Bei dunklen Designs wird die Schrift hell, damit Titel und Status lesbar bleiben.
    /// </summary>
    private static void ApplyThemeVisuals(Window window, IThemeService theme)
    {
        window.Background = theme.CurrentBackground;
        if (Current is not null)
        {
            Current.Resources["OnBgTextBrush"] = theme.CurrentOnBgText;
            Current.Resources["OnBgSubtleBrush"] = theme.CurrentOnBgSubtle;
        }
    }

    /// <summary>
    /// Laedt das zuletzt geoeffnete Projekt nach dem Start nach. Bewusst nicht blockierend;
    /// ein Fehler ist unkritisch (reiner Komfort) und fuehrt nur dazu, dass kein Projekt
    /// vorausgewaehlt ist.
    /// </summary>
    private async Task RestoreLastProjectAsync()
    {
        if (_services is null) return;
        try
        {
            var settings = _services.GetRequiredService<ISettingsService>();
            if (settings.Current.LastProjectId is not int lastId) return;

            var store = _services.GetRequiredService<IProjectStore>();
            var last = await store.GetAsync(lastId);
            if (last is null) return;

            var session = _services.GetRequiredService<IAppSession>();
            Dispatcher.Invoke(() => session.CurrentProject = last);
        }
        catch
        {
            // unkritisch: zuletzt geoeffnetes Projekt wird einfach nicht wiederhergestellt.
        }
    }

    private void ConfigureServices(IServiceCollection services)
    {
        var dbPath = Path.Combine(DataDirectory, "mputrainer.db");

        // DbContextFactory statt Singleton-DbContext, um Captive-Dependency-Probleme
        // zu vermeiden (Singletons duerfen keinen scoped DbContext halten).
        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));

        // Infrastruktur-Services (zustandslos bzw. prozessweit -> Singleton).
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ISecretStore, DpapiSecretStore>();
        services.AddSingleton<IProjectStore, ProjectStore>();
        services.AddSingleton<IAppSession, AppSession>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IThemeService, ThemeService>();

        services.AddSingleton<IAudioDeviceService, AudioDeviceService>();
        services.AddSingleton<ITtsService, TtsService>();
        services.AddSingleton<IAudioPlayer, AudioPlayer>();
        services.AddSingleton<ISessionAudioBuilder, SessionAudioBuilder>();
        services.AddTransient<IAudioRecorder, AudioRecorder>();

        services.AddSingleton<IDocumentExtractionService, DocumentExtractionService>();
        services.AddSingleton<IKnowledgeBaseService, KnowledgeBaseService>();
        services.AddSingleton<IAiClientFactory, AiClientFactory>();
        services.AddSingleton<IQuestionGenerationService, QuestionGenerationService>();
        services.AddSingleton<ITranscriptionService, OpenAiTranscriptionService>();
        services.AddSingleton<IAnswerEvaluationService, AnswerEvaluationService>();
        services.AddSingleton<IWordExportService, WordExportService>();
        services.AddSingleton<IWelcomeService, WelcomeService>();

        // ViewModels.
        services.AddSingleton<MainViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<DocumentUploadViewModel>();
        services.AddTransient<QuestionsViewModel>();
        services.AddTransient<TrainingViewModel>();
        services.AddTransient<SettingsViewModel>();

        services.AddSingleton<MainWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }
}
