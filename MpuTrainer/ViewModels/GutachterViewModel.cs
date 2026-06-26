using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MpuTrainer.Models;
using MpuTrainer.Services;

namespace MpuTrainer.ViewModels;

/// <summary>
/// Startmaske des Gutachter-Modus: Anlegen und Verwalten von MPU-Simulationsfaellen (Vorgaengen).
/// Jeder Fall wird als eigener Vorgang gespeichert und kann spaeter wieder geoeffnet werden. Die
/// weiterfuehrenden Schritte (Auffaelligkeitsliste analysieren, Fragen generieren, Pruefung,
/// Gesamtbewertung, Export) folgen in den naechsten Ausbaustufen.
/// </summary>
public partial class GutachterViewModel : ViewModelBase
{
    private readonly IGutachterCaseStore _store;
    private readonly IDialogService _dialog;

    /// <summary>Gespeicherte Faelle (neueste zuerst).</summary>
    public ObservableCollection<GutachterCase> Cases { get; } = new();

    /// <summary>Auswaehlbare Sprachen fuer Fragen und Auswertung.</summary>
    public IReadOnlyList<string> LanguageOptions => Languages.All;

    /// <summary>Auswaehlbare Fragestellungen (Anlass der MPU).</summary>
    public IReadOnlyList<Fragestellung> FragestellungOptions { get; } =
        ((Fragestellung[])Enum.GetValues(typeof(Fragestellung))).ToList();

    [ObservableProperty] private string _firstName = string.Empty;
    [ObservableProperty] private string _lastName = string.Empty;
    [ObservableProperty] private string _birthDate = string.Empty;
    [ObservableProperty] private string _selectedLanguage = "Deutsch";
    [ObservableProperty] private int _questionCount = 15;
    [ObservableProperty] private Fragestellung _selectedFragestellung = Fragestellung.Alkohol;
    [ObservableProperty] private string _examDate = DateTime.Now.ToString("dd.MM.yyyy");
    [ObservableProperty] private string _notes = string.Empty;

    [ObservableProperty] private GutachterCase? _selectedCase;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public GutachterViewModel(IGutachterCaseStore store, IDialogService dialog)
    {
        _store = store;
        _dialog = dialog;
        ReloadCases();
    }

    private void ReloadCases()
    {
        Cases.Clear();
        foreach (var c in _store.List())
            Cases.Add(c);
    }

    [RelayCommand]
    private void CreateCase()
    {
        if (string.IsNullOrWhiteSpace(FirstName) && string.IsNullOrWhiteSpace(LastName))
        {
            _dialog.Error("Bitte mindestens den Vor- oder Nachnamen des Klienten eingeben.");
            return;
        }

        var c = new GutachterCase
        {
            FirstName = (FirstName ?? string.Empty).Trim(),
            LastName = (LastName ?? string.Empty).Trim(),
            BirthDate = (BirthDate ?? string.Empty).Trim(),
            Language = string.IsNullOrWhiteSpace(SelectedLanguage) ? "Deutsch" : SelectedLanguage,
            QuestionCount = Math.Clamp(QuestionCount, 1, 80),
            Fragestellung = SelectedFragestellung,
            ExamDate = (ExamDate ?? string.Empty).Trim(),
            Notes = (Notes ?? string.Empty).Trim()
        };

        try
        {
            _store.Save(c);
            ReloadCases();
            SelectedCase = Cases.FirstOrDefault(x => x.Id == c.Id);
            StatusMessage = $"Fall „{c.FullName}\" wurde angelegt und gespeichert. " +
                            "Die Prüfung (Auffälligkeitsliste, Fragen, Auswertung) folgt im nächsten Schritt.";

            // Formular fuer den naechsten Fall zuruecksetzen.
            FirstName = string.Empty;
            LastName = string.Empty;
            BirthDate = string.Empty;
            Notes = string.Empty;
            QuestionCount = 15;
        }
        catch (Exception ex)
        {
            _dialog.Error("Der Fall konnte nicht gespeichert werden: " + ex.Message);
        }
    }

    [RelayCommand]
    private void OpenCase(GutachterCase? c)
    {
        if (c is null) return;
        SelectedCase = c;
        StatusMessage = $"Fall „{c.FullName}\" geöffnet. Die Prüfungsdurchführung wird in der " +
                        "nächsten Ausbaustufe ergänzt.";
    }

    [RelayCommand]
    private void DeleteCase(GutachterCase? c)
    {
        if (c is null) return;
        if (!_dialog.Confirm($"Fall „{c.FullName}\" wirklich löschen? Das kann nicht rückgängig gemacht werden.",
                "Fall löschen"))
            return;

        try
        {
            _store.Delete(c.Id);
            ReloadCases();
            if (SelectedCase?.Id == c.Id) SelectedCase = null;
            StatusMessage = "Fall gelöscht.";
        }
        catch (Exception ex)
        {
            _dialog.Error("Löschen fehlgeschlagen: " + ex.Message);
        }
    }
}
