using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MpuTrainer.ViewModels;

/// <summary>
/// Ein in den Einstellungen auswaehlbarer Hintergrund (Name, Vorschau-Pinsel und
/// Auswahlmarkierung fuer die Kachelansicht).
/// </summary>
public partial class BackgroundOption : ObservableObject
{
    public string Key { get; }
    public string Name { get; }
    public Brush Brush { get; }

    [ObservableProperty] private bool _isSelected;

    public BackgroundOption(string key, string name, Brush brush, bool isSelected)
    {
        Key = key;
        Name = name;
        Brush = brush;
        IsSelected = isSelected;
    }
}
