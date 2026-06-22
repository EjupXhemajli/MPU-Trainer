using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MpuTrainer.AI;
using MpuTrainer.Audio;
using MpuTrainer.Models;
using MpuTrainer.Services;

namespace MpuTrainer.ViewModels;

/// <summary>
/// Einstellungen: Audiogeraete, Sprachausgabe (Windows lokal oder Premium ueber
/// OpenAI / ElevenLabs mit eigenem API-Key), KI-Anbindung fuer die Fragegenerierung,
/// Speicherort und Verbindungstest.
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly ISecretStore _secrets;
    private readonly IAudioDeviceService _devices;
    private readonly ITtsService _tts;
    private readonly IAudioPlayer _player;
    private readonly IAiClientFactory _aiFactory;
    private readonly IDialogService _dialog;
    private readonly IThemeService _theme;

    private readonly List<TtsVoice> _elevenVoices = new();
    private string _selectedBackgroundKey = "neutral";

    public ObservableCollection<AudioDevice> Microphones { get; } = new();
    public ObservableCollection<AudioDevice> Speakers { get; } = new();
    public ObservableCollection<string> Voices { get; } = new();
    public ObservableCollection<BackgroundOption> Backgrounds { get; } = new();

    public IReadOnlyList<AiProvider> Providers { get; } =
        (AiProvider[])Enum.GetValues(typeof(AiProvider));

    public IReadOnlyList<TtsProvider> TtsProviders { get; } =
        (TtsProvider[])Enum.GetValues(typeof(TtsProvider));

    // Audio / Sprachausgabe
    [ObservableProperty] private AudioDevice? _selectedMicrophone;
    [ObservableProperty] private AudioDevice? _selectedSpeaker;
    [ObservableProperty] private string? _selectedVoice;
    [ObservableProperty] private double _volume = 0.8;

    [ObservableProperty] private TtsProvider _selectedTtsProvider = TtsProvider.Windows;
    [ObservableProperty] private string _premiumApiKey = string.Empty;
    [ObservableProperty] private string _openAiModel = "tts-1-hd";
    [ObservableProperty] private string _ttsStatus = string.Empty;

    // KI (Fragegenerierung)
    [ObservableProperty] private AiProvider _provider = AiProvider.Anthropic;
    [ObservableProperty] private string _apiKey = string.Empty;
    [ObservableProperty] private string _model = "claude-sonnet-4-6";
    [ObservableProperty] private string _baseUrl = string.Empty;
    [ObservableProperty] private double _temperature = 0.7;
    [ObservableProperty] private int _maxTokens = 2000;
    [ObservableProperty] private string _projectsDirectory = string.Empty;

    [ObservableProperty] private string _testStatus = string.Empty;
    [ObservableProperty] private bool _isBusy;

    /// <summary>True, wenn ElevenLabs gewaehlt ist (steuert Sichtbarkeit von "Stimmen laden").</summary>
    public bool IsElevenLabs => SelectedTtsProvider == TtsProvider.ElevenLabs;

    public SettingsViewModel(ISettingsService settings, ISecretStore secrets,
        IAudioDeviceService devices, ITtsService tts, IAudioPlayer player,
        IAiClientFactory aiFactory, IDialogService dialog, IThemeService theme)
    {
        _settings = settings;
        _secrets = secrets;
        _devices = devices;
        _tts = tts;
        _player = player;
        _aiFactory = aiFactory;
        _dialog = dialog;
        _theme = theme;

        LoadFromSettings();
        RefreshDevices();
        ApplyDeviceSelection();
        LoadVoicesForProvider();
        BuildBackgrounds();
    }

    private void LoadFromSettings()
    {
        var s = _settings.Current;
        Volume = s.Volume;

        OpenAiModel = string.IsNullOrWhiteSpace(s.OpenAiTtsModel) ? "tts-1-hd" : s.OpenAiTtsModel;
        PremiumApiKey = _secrets.Load("tts") ?? string.Empty;
        SelectedTtsProvider = s.TtsProvider;

        _selectedBackgroundKey = string.IsNullOrWhiteSpace(s.BackgroundTheme) ? "neutral" : s.BackgroundTheme;

        Provider = s.Provider;
        Model = s.Model;
        BaseUrl = s.BaseUrl;
        Temperature = s.Temperature;
        MaxTokens = s.MaxTokens;
        ProjectsDirectory = s.ProjectsDirectory;
        ApiKey = _secrets.LoadApiKey() ?? string.Empty;
    }

    partial void OnSelectedTtsProviderChanged(TtsProvider value)
    {
        OnPropertyChanged(nameof(IsElevenLabs));
        LoadVoicesForProvider();
    }

    [RelayCommand]
    private void RefreshDevices()
    {
        Microphones.Clear();
        foreach (var d in _devices.GetMicrophones()) Microphones.Add(d);

        Speakers.Clear();
        foreach (var d in _devices.GetSpeakers()) Speakers.Add(d);

        ApplyDeviceSelection();
    }

    /// <summary>Fuellt die Stimmenliste passend zum gewaehlten Sprachausgabe-Anbieter.</summary>
    private void LoadVoicesForProvider()
    {
        Voices.Clear();
        var s = _settings.Current;

        switch (SelectedTtsProvider)
        {
            case TtsProvider.OpenAI:
                foreach (var v in _tts.OpenAiVoices) Voices.Add(v);
                SelectedVoice = Voices.Contains(s.OpenAiTtsVoice) ? s.OpenAiTtsVoice : Voices.FirstOrDefault();
                break;

            case TtsProvider.ElevenLabs:
                foreach (var v in _elevenVoices) Voices.Add(v.Name);
                var preset = _elevenVoices.FirstOrDefault(x => x.Id == s.ElevenLabsVoiceId)?.Name;
                if (preset is null && !string.IsNullOrEmpty(s.ElevenLabsVoiceName))
                {
                    preset = s.ElevenLabsVoiceName;
                    if (!Voices.Contains(preset)) Voices.Add(preset);
                }
                SelectedVoice = preset ?? Voices.FirstOrDefault();
                break;

            default: // Windows
                foreach (var v in _tts.GetWindowsVoices()) Voices.Add(v);
                SelectedVoice = (s.VoiceName is not null && Voices.Contains(s.VoiceName))
                    ? s.VoiceName : Voices.FirstOrDefault();
                break;
        }
    }

    private void ApplyDeviceSelection()
    {
        var s = _settings.Current;

        SelectedMicrophone =
            Microphones.FirstOrDefault(d => d.Id == s.MicrophoneId)
            ?? Microphones.FirstOrDefault(d => d.IsDefault)
            ?? Microphones.FirstOrDefault();

        SelectedSpeaker =
            Speakers.FirstOrDefault(d => d.Id == s.SpeakerId)
            ?? Speakers.FirstOrDefault(d => d.IsDefault)
            ?? Speakers.FirstOrDefault();
    }

    /// <summary>Baut die Kachelliste der Hintergruende und markiert den aktuell gewaehlten.</summary>
    private void BuildBackgrounds()
    {
        Backgrounds.Clear();
        foreach (var t in _theme.Themes)
            Backgrounds.Add(new BackgroundOption(t.Key, t.Name, t.Brush, t.Key == _selectedBackgroundKey));
    }

    [RelayCommand]
    private void SelectBackground(BackgroundOption? option)
    {
        if (option is null) return;

        _selectedBackgroundKey = option.Key;
        foreach (var b in Backgrounds)
            b.IsSelected = b.Key == option.Key;

        // Sofortige Vorschau im Hauptfenster.
        _theme.Apply(option.Key);
    }

    [RelayCommand]
    private async Task LoadElevenVoicesAsync()
    {
        if (string.IsNullOrWhiteSpace(PremiumApiKey))
        {
            TtsStatus = "Bitte zuerst den ElevenLabs-API-Key eingeben.";
            return;
        }

        try
        {
            IsBusy = true;
            TtsStatus = "Stimmen werden geladen ...";
            var voices = await _tts.GetElevenLabsVoicesAsync(PremiumApiKey.Trim());
            _elevenVoices.Clear();
            _elevenVoices.AddRange(voices);
            LoadVoicesForProvider();
            TtsStatus = _elevenVoices.Count > 0
                ? $"{_elevenVoices.Count} Stimmen geladen."
                : "Keine Stimmen im Konto gefunden.";
        }
        catch (Exception ex)
        {
            TtsStatus = $"Fehler: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SpeakSampleAsync()
    {
        string? wav = null;
        try
        {
            IsBusy = true;
            wav = Path.Combine(Path.GetTempPath(), $"mpu_tts_{Guid.NewGuid():N}.wav");
            await _tts.SpeakToWaveFileAsync(
                "Dies ist eine Beispielausgabe der ausgewaehlten Stimme.", wav, BuildTtsOptions());
            await _player.PlayAsync(wav, SelectedSpeaker?.Id, Volume);
        }
        catch (Exception ex)
        {
            _dialog.Error($"Sprachausgabe nicht moeglich: {ex.Message}");
        }
        finally
        {
            if (wav is not null)
            {
                try { File.Delete(wav); } catch { /* ignorieren */ }
            }
            IsBusy = false;
        }
    }

    private TtsOptions BuildTtsOptions()
    {
        var s = _settings.Current;
        var elevenId = SelectedTtsProvider == TtsProvider.ElevenLabs
            ? (_elevenVoices.FirstOrDefault(v => v.Name == SelectedVoice)?.Id ?? s.ElevenLabsVoiceId)
            : s.ElevenLabsVoiceId;

        return new TtsOptions(
            SelectedTtsProvider,
            SelectedTtsProvider == TtsProvider.Windows ? SelectedVoice : null,
            (int)Math.Round(Volume * 100),
            PremiumApiKey?.Trim(),
            SelectedTtsProvider == TtsProvider.OpenAI ? SelectedVoice : s.OpenAiTtsVoice,
            OpenAiModel?.Trim(),
            elevenId,
            string.IsNullOrWhiteSpace(s.ElevenLabsModel) ? "eleven_multilingual_v2" : s.ElevenLabsModel);
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            TestStatus = "Bitte zuerst einen API-Key eingeben.";
            return;
        }

        try
        {
            IsBusy = true;
            TestStatus = "Verbindung wird getestet ...";
            var probe = BuildSettingsFromFields();
            var client = _aiFactory.Create(probe, ApiKey);
            var (ok, message) = await client.TestConnectionAsync();
            TestStatus = ok ? $"OK - {message}" : $"Fehlgeschlagen - {message}";
        }
        catch (Exception ex)
        {
            TestStatus = $"Fehlgeschlagen - {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Save()
    {
        var s = _settings.Current;
        s.MicrophoneId = SelectedMicrophone?.Id;
        s.SpeakerId = SelectedSpeaker?.Id;
        s.Volume = Volume;
        s.BackgroundTheme = _selectedBackgroundKey;

        // Sprachausgabe
        s.TtsProvider = SelectedTtsProvider;
        s.OpenAiTtsModel = string.IsNullOrWhiteSpace(OpenAiModel) ? "tts-1-hd" : OpenAiModel.Trim();

        if (SelectedTtsProvider == TtsProvider.Windows)
        {
            s.VoiceName = SelectedVoice;
        }
        else if (SelectedTtsProvider == TtsProvider.OpenAI)
        {
            if (!string.IsNullOrWhiteSpace(SelectedVoice)) s.OpenAiTtsVoice = SelectedVoice!;
        }
        else if (SelectedTtsProvider == TtsProvider.ElevenLabs)
        {
            var v = _elevenVoices.FirstOrDefault(x => x.Name == SelectedVoice);
            if (v is not null)
            {
                s.ElevenLabsVoiceId = v.Id;
                s.ElevenLabsVoiceName = v.Name;
            }
        }

        // KI (Fragegenerierung)
        s.Provider = Provider;
        s.Model = Model?.Trim() ?? string.Empty;
        s.BaseUrl = BaseUrl?.Trim() ?? string.Empty;
        s.Temperature = Temperature;
        s.MaxTokens = MaxTokens;

        if (!string.IsNullOrWhiteSpace(ProjectsDirectory))
        {
            try { Directory.CreateDirectory(ProjectsDirectory); } catch { /* ignorieren */ }
            s.ProjectsDirectory = ProjectsDirectory.Trim();
        }

        _settings.Save();

        // Schluessel verschluesselt ablegen (oder loeschen, wenn leer).
        _secrets.SaveApiKey(ApiKey?.Trim() ?? string.Empty);
        _secrets.Save("tts", PremiumApiKey?.Trim());

        _dialog.Info("Einstellungen gespeichert.");
    }

    private AppSettings BuildSettingsFromFields() => new()
    {
        Provider = Provider,
        Model = Model?.Trim() ?? string.Empty,
        BaseUrl = BaseUrl?.Trim() ?? string.Empty,
        Temperature = Temperature,
        MaxTokens = MaxTokens
    };
}
