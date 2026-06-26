using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace MpuTrainer;

/// <summary>
/// Hauptfenster mit Seitenleiste. Der DataContext (MainViewModel) wird in
/// App.xaml.cs gesetzt; der ContentControl zeigt jeweils das aktive ViewModel.
/// Standardmaessig wird das eingebettete BfK-Logo angezeigt.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        TryLoadCustomLogo();
    }

    /// <summary>
    /// Ersetzt das eingebettete Logo durch ein eigenes, wenn im Datenordner
    /// (%AppData%\MpuTrainer) eine Datei "logo.png" liegt.
    /// </summary>
    private void TryLoadCustomLogo()
    {
        try
        {
            var path = Path.Combine(App.DataDirectory, "logo.png");
            if (!File.Exists(path)) return;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();

            LogoImage.Source = bmp;
        }
        catch
        {
            // Bei Problemen mit der Bilddatei bleibt das eingebettete BfK-Logo stehen.
        }
    }
}
