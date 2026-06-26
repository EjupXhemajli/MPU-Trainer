using Microsoft.Win32;
using System.Windows;

namespace MpuTrainer.Services;

public interface IDialogService
{
    string? OpenFile(string filter, string title);
    string[] OpenFiles(string filter, string title);
    string? SaveFile(string filter, string title, string defaultFileName);
    void Info(string message, string title = "Hinweis");
    void Error(string message, string title = "Fehler");
    bool Confirm(string message, string title = "Bitte bestaetigen");
}

/// <summary>Kapselt Datei- und Meldungsdialoge, damit ViewModels testbar bleiben.</summary>
public class DialogService : IDialogService
{
    public string? OpenFile(string filter, string title)
    {
        var dlg = new OpenFileDialog
        {
            Filter = filter,
            Title = title,
            CheckFileExists = true
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public string[] OpenFiles(string filter, string title)
    {
        var dlg = new OpenFileDialog
        {
            Filter = filter,
            Title = title,
            CheckFileExists = true,
            Multiselect = true
        };
        return dlg.ShowDialog() == true ? dlg.FileNames : System.Array.Empty<string>();
    }

    public string? SaveFile(string filter, string title, string defaultFileName)
    {
        var dlg = new SaveFileDialog
        {
            Filter = filter,
            Title = title,
            FileName = defaultFileName,
            OverwritePrompt = true,
            AddExtension = true
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public void Info(string message, string title = "Hinweis") =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void Error(string message, string title = "Fehler") =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public bool Confirm(string message, string title = "Bitte bestaetigen") =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
            == MessageBoxResult.Yes;
}
