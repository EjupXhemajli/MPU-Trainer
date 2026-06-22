using System.Collections.Generic;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MpuTrainer.AI;
using MpuTrainer.Audio;
using MpuTrainer.Data;
using MpuTrainer.Models;
using MpuTrainer.Services;

namespace MpuTrainer.ViewModels;

/// <summary>
/// Trainingsmodus: Frage vorlesen, Antwort aufnehmen, eigene Antwort und
/// Musterantwort anhoeren, zur naechsten Frage wechseln. Am Ende wird die
/// gesamte Unterhaltung als MP3 zusammengefuegt.
/// </summary>
public partial class TrainingViewModel : ViewModelBase
{
    private readonly IAppSession _session;
    private readonly IProjectStore _store;
    private readonly ITtsService _tts;
    private readonly IAudioRecorder _recorder;
    private readonly IAudioPlayer _player;
    private readonly ISessionAudioBuilder _sessionBuilder;
    private readonly IQuestionGenerationService _generation;
    private readonly ISettingsService _settings;
    private readonly IDialogService _dialog;
    private readonly ITranscriptionService _transcription;
    private readonly IAnswerEvaluationService _evaluation;
    private readonly IWordExportService _wordExport;
    private readonly ISecretStore _secrets;

    private readonly List<TrainingQuestion> _questions = new();

    [ObservableProperty] private TrainingQuestion? _currentQuestion;
    [ObservableProperty] private string _progressText = string.Empty;
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private string _modelAnswerText = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private double _level;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayQuestionCommand))]
    [NotifyCanExecuteChangedFor(nameof(PlayMyAnswerCommand))]
    [NotifyCanExecuteChangedFor(nameof(PlayModelAnswerCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextQuestionCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviousQuestionCommand))]
    [NotifyCanExecuteChangedFor(nameof(BuildSessionCommand))]
    [NotifyCanExecuteChangedFor(nameof(EvaluateAnswerCommand))]
    private bool _isRecording;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayQuestionCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleRecordingCommand))]
    [NotifyCanExecuteChangedFor(nameof(PlayMyAnswerCommand))]
    [NotifyCanExecuteChangedFor(nameof(PlayModelAnswerCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextQuestionCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviousQuestionCommand))]
    [NotifyCanExecuteChangedFor(nameof(BuildSessionCommand))]
    [NotifyCanExecuteChangedFor(nameof(EvaluateAnswerCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayMyAnswerCommand))]
    [NotifyCanExecuteChangedFor(nameof(EvaluateAnswerCommand))]
    private bool _hasRecording;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayQuestionCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleRecordingCommand))]
    [NotifyCanExecuteChangedFor(nameof(PlayMyAnswerCommand))]
    [NotifyCanExecuteChangedFor(nameof(PlayModelAnswerCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextQuestionCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviousQuestionCommand))]
    [NotifyCanExecuteChangedFor(nameof(BuildSessionCommand))]
    [NotifyCanExecuteChangedFor(nameof(EvaluateAnswerCommand))]
    private bool _isEvaluating;

    /// <summary>Transkript der zuletzt ausgewerteten Antwort (zur Anzeige).</summary>
    [ObservableProperty] private string _transcript = string.Empty;

    /// <summary>Formatierter Auswertungstext (Fazit, Schwaechen, Defizite, Verbesserungen).</summary>
    [ObservableProperty] private string _evaluationText = string.Empty;

    /// <summary>Steuert die Sichtbarkeit des Auswertungsbereichs.</summary>
    [ObservableProperty] private bool _hasEvaluation;

    /// <summary>Pfad der Antwort-MP3 (zum Oeffnen), falls erzeugt.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenAnswerMp3Command))]
    private string _answerMp3Path = string.Empty;

    /// <summary>Steuert die Sichtbarkeit des MP3-Buttons.</summary>
    [ObservableProperty] private bool _hasAnswerMp3;

    private int _index;

    public TrainingViewModel(IAppSession session, IProjectStore store, ITtsService tts,
        IAudioRecorder recorder, IAudioPlayer player, ISessionAudioBuilder sessionBuilder,
        IQuestionGenerationService generation, ISettingsService settings, IDialogService dialog,
        ITranscriptionService transcription, IAnswerEvaluationService evaluation,
        IWordExportService wordExport, ISecretStore secrets)
    {
        _session = session;
        _store = store;
        _tts = tts;
        _recorder = recorder;
        _player = player;
        _sessionBuilder = sessionBuilder;
        _generation = generation;
        _settings = settings;
        _dialog = dialog;
        _transcription = transcription;
        _evaluation = evaluation;
        _wordExport = wordExport;
        _secrets = secrets;

        _recorder.LevelChanged += OnLevelChanged;

        Load();
    }

    private void Load()
    {
        _questions.Clear();
        var project = _session.CurrentProject;
        if (project is not null)
            _questions.AddRange(project.Questions.OrderBy(q => q.Order));

        _index = 0;
        ShowCurrent();
    }

    private void ShowCurrent()
    {
        if (_questions.Count == 0)
        {
            CurrentQuestion = null;
            ProgressText = "Keine Fragen vorhanden";
            ProgressValue = 0;
            ModelAnswerText = string.Empty;
            HasRecording = false;
            Transcript = string.Empty;
            EvaluationText = string.Empty;
            HasEvaluation = false;
            AnswerMp3Path = string.Empty;
            HasAnswerMp3 = false;
            return;
        }

        _index = Math.Clamp(_index, 0, _questions.Count - 1);
        CurrentQuestion = _questions[_index];
        ProgressText = $"Frage {_index + 1} von {_questions.Count}";
        ProgressValue = (double)(_index + 1) / _questions.Count;
        ModelAnswerText = CurrentQuestion.ModelAnswer ?? string.Empty;
        HasRecording = !string.IsNullOrWhiteSpace(CurrentQuestion.RecordingPath)
                       && File.Exists(CurrentQuestion.RecordingPath);

        // Gespeicherte Auswertung der aktuellen Frage anzeigen (falls vorhanden).
        Transcript = CurrentQuestion.Transcript ?? string.Empty;
        EvaluationText = CurrentQuestion.Evaluation ?? string.Empty;
        HasEvaluation = !string.IsNullOrWhiteSpace(EvaluationText);

        AnswerMp3Path = CurrentQuestion.RecordingMp3Path ?? string.Empty;
        HasAnswerMp3 = !string.IsNullOrWhiteSpace(AnswerMp3Path) && File.Exists(AnswerMp3Path);

        StatusMessage = string.Empty;
    }

    private void OnLevelChanged(float level)
    {
        // Ereignis kommt aus einem Audiothread -> auf den UI-Thread marshallen.
        Application.Current?.Dispatcher.Invoke(() => Level = level);
    }

    // ---- Verfuegbarkeit der Aktionen (verhindert Audiokonflikte) ------

    private bool CanInteract() => !IsBusy && !IsRecording && !IsEvaluating;
    private bool CanPlayQuestion() => !IsBusy && !IsRecording && !IsEvaluating;
    private bool CanPlayModelAnswer() => !IsBusy && !IsRecording && !IsEvaluating;
    private bool CanToggleRecording() => !IsBusy && !IsEvaluating;
    private bool CanPlayMyAnswer() => HasRecording && !IsBusy && !IsRecording && !IsEvaluating;
    private bool CanEvaluateAnswer() => HasRecording && !IsBusy && !IsRecording && !IsEvaluating;

    // ---- Audioausgabe der Frage ---------------------------------------

    [RelayCommand(CanExecute = nameof(CanPlayQuestion))]
    private async Task PlayQuestionAsync()
    {
        if (CurrentQuestion is null) return;
        try
        {
            IsBusy = true;
            StatusMessage = "Frage wird vorgelesen ...";
            var wav = await EnsureQuestionAudioAsync(CurrentQuestion);
            await PlayAsync(wav);
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            _dialog.Error($"Wiedergabe nicht moeglich: {ex.Message}");
        }
        finally { IsBusy = false; }
    }

    // ---- Aufnahme der Klientenantwort ---------------------------------

    [RelayCommand(CanExecute = nameof(CanToggleRecording))]
    private async Task ToggleRecordingAsync()
    {
        if (CurrentQuestion is null) return;

        if (_recorder.IsRecording)
        {
            // Erst zurueckkehren, wenn die WAV-Datei vollstaendig geschrieben ist.
            await _recorder.StopAsync();
            IsRecording = false;
            StatusMessage = "Aufnahme gespeichert.";

            // Pfad merken und persistieren.
            CurrentQuestion.RecordingPath = _pendingRecordingPath;
            HasRecording = true;
            await _store.UpdateQuestionAsync(CurrentQuestion);
            return;
        }

        try
        {
            // Laufende Wiedergabe stoppen, bevor aufgenommen wird.
            _player.Stop();

            var dir = Path.Combine(ProjectDir(), "aufnahmen");
            _pendingRecordingPath = Path.Combine(dir, $"antwort_{CurrentQuestion.Order:D3}.wav");

            _recorder.Start(_settings.Current.MicrophoneId, _pendingRecordingPath);
            IsRecording = true;
            StatusMessage = "Aufnahme laeuft ... erneut klicken zum Stoppen.";
        }
        catch (Exception ex)
        {
            IsRecording = false;
            _dialog.Error($"Aufnahme nicht moeglich: {ex.Message}\n\n" +
                          "Bitte pruefen Sie unter Windows-Einstellungen > Datenschutz > " +
                          "Mikrofon, ob der Zugriff erlaubt ist.");
        }
    }

    private string _pendingRecordingPath = string.Empty;

    [RelayCommand(CanExecute = nameof(CanPlayMyAnswer))]
    private async Task PlayMyAnswerAsync()
    {
        if (CurrentQuestion?.RecordingPath is null) return;
        try
        {
            IsBusy = true;
            await PlayAsync(CurrentQuestion.RecordingPath);
        }
        catch (Exception ex)
        {
            _dialog.Error($"Wiedergabe nicht moeglich: {ex.Message}");
        }
        finally { IsBusy = false; }
    }

    // ---- Auswertung der Antwort ---------------------------------------

    [RelayCommand(CanExecute = nameof(CanEvaluateAnswer))]
    private async Task EvaluateAnswerAsync()
    {
        var project = _session.CurrentProject;
        var question = CurrentQuestion;
        if (project is null || question is null) return;

        var path = question.RecordingPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            _dialog.Error("Es ist keine Aufnahme vorhanden, die ausgewertet werden kann.");
            return;
        }

        var key = ResolveOpenAiKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            _dialog.Error(
                "Fuer die Auswertung der gesprochenen Antwort wird ein OpenAI-Key benoetigt " +
                "(fuer die Spracherkennung). Bitte in den Einstellungen entweder als " +
                "Premium-Sprach-Key (Anbieter OpenAI) oder als KI-Anbieter \"OpenAI-kompatibel\" hinterlegen.");
            return;
        }

        try
        {
            _player.Stop();
            IsEvaluating = true;
            HasEvaluation = false;

            // 1) Transkription der Aufnahme.
            StatusMessage = "Antwort wird in Text umgewandelt ...";
            var transcript = await _transcription.TranscribeAsync(path, key);

            if (string.IsNullOrWhiteSpace(transcript))
            {
                _dialog.Error("Die Aufnahme konnte nicht in Text umgewandelt werden (zu leise oder zu kurz?).");
                StatusMessage = "Auswertung abgebrochen.";
                return;
            }

            // 1b) Transkript sprachlich korrigieren (Rechtschreibung, Grammatik, Sinn) –
            //     besonders hilfreich bei fremdsprachigen Antworten.
            StatusMessage = "Transkript wird korrigiert ...";
            transcript = await _evaluation.CorrectTranscriptAsync(transcript, project.Language);

            Transcript = transcript;

            // 2) Aufnahme zusaetzlich als MP3 ausgeben.
            try
            {
                StatusMessage = "Aufnahme wird als MP3 gespeichert ...";
                var mp3 = Path.Combine(ProjectDir(), "aufnahmen", $"antwort_{question.Order:D3}.mp3");
                await _sessionBuilder.BuildAsync(new[] { path }, mp3);
                question.RecordingMp3Path = mp3;
                AnswerMp3Path = mp3;
                HasAnswerMp3 = true;
            }
            catch { /* MP3 ist optional - Auswertung laeuft trotzdem weiter */ }

            // 3) Musterantwort sicherstellen (sie ist der Massstab fuer den Vergleich).
            if (string.IsNullOrWhiteSpace(question.ModelAnswer))
            {
                StatusMessage = "Musterantwort wird erstellt ...";
                question.ModelAnswer = await _generation.GenerateModelAnswerAsync(
                    project.LeitfadenText, question.Text, project.Language);
                ModelAnswerText = question.ModelAnswer;
            }

            // 4) Vergleich/Auswertung.
            StatusMessage = "Antwort wird mit der Musterantwort verglichen ...";
            var evaluation = await _evaluation.EvaluateAsync(project, question, transcript);

            EvaluationText = FormatEvaluation(evaluation);
            HasEvaluation = true;

            // Transkript, Auswertung und MP3-Pfad dauerhaft speichern.
            question.Transcript = transcript;
            question.Evaluation = EvaluationText;
            await _store.UpdateQuestionAsync(question);

            StatusMessage = "Auswertung abgeschlossen.";
        }
        catch (Exception ex)
        {
            _dialog.Error($"Auswertung nicht moeglich: {ex.Message}");
            StatusMessage = "Fehler bei der Auswertung.";
        }
        finally
        {
            IsEvaluating = false;
        }
    }

    private bool CanOpenAnswerMp3() =>
        !string.IsNullOrWhiteSpace(AnswerMp3Path) && File.Exists(AnswerMp3Path);

    [RelayCommand(CanExecute = nameof(CanOpenAnswerMp3))]
    private void OpenAnswerMp3()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = AnswerMp3Path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _dialog.Error($"MP3 konnte nicht geoeffnet werden: {ex.Message}");
        }
    }

    /// <summary>Ermittelt einen OpenAI-Key fuer die Spracherkennung aus den hinterlegten Geheimnissen.</summary>
    private string? ResolveOpenAiKey()
    {
        var s = _settings.Current;

        // 1) Premium-Sprach-Key, wenn OpenAI als Sprachausgabe-Anbieter gewaehlt ist (= OpenAI-Key).
        if (s.TtsProvider == TtsProvider.OpenAI)
        {
            var k = _secrets.Load("tts");
            if (!string.IsNullOrWhiteSpace(k)) return k;
        }

        // 2) KI-Anbieter "OpenAI-kompatibel" -> der hinterlegte KI-Key ist ein OpenAI-Key.
        if (s.Provider == AiProvider.OpenAiCompatible)
        {
            var k = _secrets.LoadApiKey();
            if (!string.IsNullOrWhiteSpace(k)) return k;
        }

        return null;
    }

    /// <summary>Formt die strukturierte Auswertung in einen gut lesbaren Text um.</summary>
    private static string FormatEvaluation(AnswerEvaluation e)
    {
        var sb = new System.Text.StringBuilder();

        if (!string.IsNullOrWhiteSpace(e.Kernuebereinstimmung))
            sb.AppendLine("Übereinstimmung mit der Musterantwort:")
              .AppendLine(e.Kernuebereinstimmung.Trim())
              .AppendLine();

        AppendSection(sb, "Was war falsch oder abweichend", e.Abweichungen);
        AppendSection(sb, "Was im Kern noch nicht verstanden wurde", e.NichtVerstanden);
        AppendSection(sb, "Verbesserungsvorschläge", e.Verbesserungen);

        var text = sb.ToString().Trim();
        return string.IsNullOrWhiteSpace(text)
            ? "Es konnte keine Auswertung erstellt werden."
            : text;
    }

    private static void AppendSection(System.Text.StringBuilder sb, string title, List<string> items)
    {
        if (items is null || items.Count == 0) return;
        sb.AppendLine($"{title}:");
        foreach (var item in items)
            sb.AppendLine($"  •  {item}");
        sb.AppendLine();
    }

    // ---- Musterantwort ------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanPlayModelAnswer))]
    private async Task PlayModelAnswerAsync()
    {
        if (CurrentQuestion is null) return;
        try
        {
            IsBusy = true;

            if (string.IsNullOrWhiteSpace(CurrentQuestion.ModelAnswer))
            {
                StatusMessage = "Musterantwort wird erstellt ...";
                var project = _session.CurrentProject;
                var answer = await _generation.GenerateModelAnswerAsync(
                    project?.LeitfadenText ?? string.Empty, CurrentQuestion.Text,
                    project?.Language ?? "Deutsch");

                CurrentQuestion.ModelAnswer = answer;
                ModelAnswerText = answer;
                await _store.UpdateQuestionAsync(CurrentQuestion);
            }

            StatusMessage = "Musterantwort wird vorgelesen ...";
            var wav = await EnsureModelAudioAsync(CurrentQuestion);
            await PlayAsync(wav);
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            _dialog.Error($"Musterantwort nicht moeglich: {ex.Message}");
        }
        finally { IsBusy = false; }
    }

    // ---- Navigation zwischen Fragen -----------------------------------

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private async Task NextQuestionAsync()
    {
        _player.Stop();

        if (CurrentQuestion is { Status: QuestionStatus.Offen })
        {
            CurrentQuestion.Status = QuestionStatus.Geuebt;
            await _store.UpdateQuestionAsync(CurrentQuestion);
        }

        if (_index >= _questions.Count - 1)
        {
            StatusMessage = "Letzte Frage erreicht.";
            if (_dialog.Confirm(
                "Das war die letzte Frage. Gesamte Unterhaltung jetzt als MP3 speichern?",
                "Sitzung abschliessen"))
            {
                await BuildSessionAsync();
            }
            return;
        }

        _index++;
        ShowCurrent();
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private void PreviousQuestion()
    {
        _player.Stop();
        if (_index <= 0) return;
        _index--;
        ShowCurrent();
    }

    // ---- Gesamte Unterhaltung als MP3 ---------------------------------

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private async Task BuildSessionAsync()
    {
        var project = _session.CurrentProject;
        if (project is null || _questions.Count == 0) return;

        try
        {
            IsBusy = true;
            StatusMessage = "Unterhaltung wird als MP3 zusammengestellt ...";

            var segments = new List<string>();
            foreach (var q in _questions)
            {
                // Frage
                segments.Add(await EnsureQuestionAudioAsync(q));

                // Klientenantwort (falls vorhanden)
                if (!string.IsNullOrWhiteSpace(q.RecordingPath) && File.Exists(q.RecordingPath))
                    segments.Add(q.RecordingPath);

                // Musterantwort (bei Bedarf erzeugen)
                if (string.IsNullOrWhiteSpace(q.ModelAnswer))
                {
                    q.ModelAnswer = await _generation.GenerateModelAnswerAsync(
                        project.LeitfadenText, q.Text, project.Language);
                    await _store.UpdateQuestionAsync(q);
                }
                segments.Add(await EnsureModelAudioAsync(q));
            }

            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
            var mp3Path = Path.Combine(ProjectDir(), $"sitzung_{stamp}.mp3");

            await _sessionBuilder.BuildAsync(segments, mp3Path);

            project.SessionMp3Path = mp3Path;
            await _store.UpdateAsync(project);

            StatusMessage = $"MP3 gespeichert: {mp3Path}";
            _dialog.Info($"Die gesamte Unterhaltung wurde gespeichert:\n\n{mp3Path}");
        }
        catch (Exception ex)
        {
            _dialog.Error($"MP3 konnte nicht erstellt werden: {ex.Message}");
        }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private async Task SaveSessionWordAsync()
    {
        var project = _session.CurrentProject;
        if (project is null || _questions.Count == 0) return;

        var safeName = MakeFileSafe(project.Name);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
        var target = _dialog.SaveFile(
            "Word-Dokument (*.docx)|*.docx",
            "Sitzung als Word speichern",
            $"MPU-Sitzung_{safeName}_{stamp}.docx");
        if (target is null) return;

        try
        {
            IsBusy = true;
            StatusMessage = "Sitzung wird als Word erstellt ...";

            // Fehlende Musterantworten vorab erzeugen, damit das Dokument vollstaendig ist.
            foreach (var q in _questions)
            {
                if (string.IsNullOrWhiteSpace(q.ModelAnswer))
                {
                    q.ModelAnswer = await _generation.GenerateModelAnswerAsync(
                        project.LeitfadenText, q.Text, project.Language);
                    await _store.UpdateQuestionAsync(q);
                }
            }

            await _wordExport.ExportSessionAsync(project, _questions, target);

            StatusMessage = $"Word gespeichert: {target}";
            _dialog.Info($"Die Sitzung wurde als Word gespeichert:\n\n{target}");
        }
        catch (Exception ex)
        {
            _dialog.Error($"Word-Dokument konnte nicht erstellt werden: {ex.Message}");
        }
        finally { IsBusy = false; }
    }

    private static string MakeFileSafe(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "Projekt" : name.Trim();
    }

    // ---- Hilfsfunktionen ----------------------------------------------

    private async Task PlayAsync(string path)
    {
        await _player.PlayAsync(path, _settings.Current.SpeakerId, _settings.Current.Volume);
    }

    private async Task<string> EnsureQuestionAudioAsync(TrainingQuestion q)
    {
        var path = Path.Combine(ProjectDir(), "tts", $"frage_{q.Order:D3}_{TtsSignature()}.wav");
        if (!File.Exists(path))
            await _tts.SpeakToWaveFileAsync(q.Text, path);
        return path;
    }

    private async Task<string> EnsureModelAudioAsync(TrainingQuestion q)
    {
        var path = Path.Combine(ProjectDir(), "tts", $"muster_{q.Order:D3}_{TtsSignature()}.wav");
        var text = q.ModelAnswer ?? string.Empty;

        // Immer neu erzeugen, falls Text vorhanden, aber Datei fehlt.
        if (!File.Exists(path) && !string.IsNullOrWhiteSpace(text))
            await _tts.SpeakToWaveFileAsync(text, path);

        return path;
    }

    /// <summary>
    /// Kurzes Kennzeichen der aktuellen Sprachausgabe-Einstellung (Anbieter + Stimme + Modell).
    /// Wird in den Dateinamen der vorgelesenen Audiodateien aufgenommen, damit beim Wechsel
    /// der Stimme die Audios automatisch neu erzeugt werden (keine alte Stimme aus dem Cache).
    /// </summary>
    private string TtsSignature()
    {
        var s = _settings.Current;
        var raw = s.TtsProvider switch
        {
            TtsProvider.OpenAI => $"oai|{s.OpenAiTtsModel}|{s.OpenAiTtsVoice}",
            TtsProvider.ElevenLabs => $"el|{s.ElevenLabsModel}|{s.ElevenLabsVoiceId}",
            _ => $"win|{s.VoiceName}"
        };
        var hash = System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
    }

    private string ProjectDir()
    {
        var project = _session.CurrentProject!;
        var root = string.IsNullOrWhiteSpace(_settings.Current.ProjectsDirectory)
            ? Path.Combine(App.DataDirectory, "Projekte")
            : _settings.Current.ProjectsDirectory;

        var dir = Path.Combine(root, $"projekt_{project.Id:D4}");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
