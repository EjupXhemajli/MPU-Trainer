using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace MpuTrainer.Services;

/// <summary>Ein auswaehlbares Hintergrunddesign (Name + fertiger Pinsel + passende Schriftfarben).</summary>
public sealed class BackgroundTheme
{
    public string Key { get; }
    public string Name { get; }
    public Brush Brush { get; }

    /// <summary>Schriftfarbe fuer Texte direkt auf dem Hintergrund (Titel/Status).</summary>
    public Brush OnBgText { get; }

    /// <summary>Gedaempfte Schriftfarbe fuer Untertitel/Status auf dem Hintergrund.</summary>
    public Brush OnBgSubtle { get; }

    /// <summary>True bei dunklem Hintergrund (helle Schrift).</summary>
    public bool IsDark { get; }

    public BackgroundTheme(string key, string name, Brush brush, Brush onBgText, Brush onBgSubtle, bool isDark)
    {
        Key = key;
        Name = name;
        Brush = brush;
        OnBgText = onBgText;
        OnBgSubtle = onBgSubtle;
        IsDark = isDark;
    }
}

public interface IThemeService
{
    /// <summary>Alle verfuegbaren Hintergruende (10 helle + 10 dunkle Designs).</summary>
    IReadOnlyList<BackgroundTheme> Themes { get; }

    /// <summary>Schluessel des aktuell gewaehlten Hintergrunds.</summary>
    string CurrentKey { get; }

    /// <summary>Pinsel des aktuell gewaehlten Hintergrunds.</summary>
    Brush CurrentBackground { get; }

    /// <summary>Schriftfarbe fuer Texte auf dem Hintergrund (passend zum Design).</summary>
    Brush CurrentOnBgText { get; }

    /// <summary>Gedaempfte Schriftfarbe fuer Texte auf dem Hintergrund.</summary>
    Brush CurrentOnBgSubtle { get; }

    /// <summary>Wird ausgeloest, wenn sich der Hintergrund aendert (UI aktualisiert sich daraufhin).</summary>
    event Action? Changed;

    /// <summary>Setzt den aktiven Hintergrund anhand des Schluessels (mit sicherem Fallback).</summary>
    void Apply(string? key);
}

/// <summary>
/// Stellt 20 zum BfK-Logo (Anthrazit/Orange) passende Hintergruende bereit: 10 helle und
/// 10 dunkle. Bei dunklen Designs wird die Schrift auf dem Hintergrund hell; weisse Karten heben
/// sich davon ab und bleiben mit dunkler Schrift gut lesbar.
/// Die Pinsel sind eingefroren (Freeze) und damit threadsicher wiederverwendbar.
/// </summary>
public class ThemeService : IThemeService
{
    private readonly List<BackgroundTheme> _themes;

    // Schriftfarben fuer Texte auf dem Hintergrund.
    private static readonly Brush DarkText = Solid("#2B2E35");
    private static readonly Brush DarkSubtle = Solid("#6B7280");
    private static readonly Brush LightText = Solid("#F4F6F8");
    private static readonly Brush LightSubtle = Solid("#B9BFC9");

    public IReadOnlyList<BackgroundTheme> Themes => _themes;
    public string CurrentKey { get; private set; } = "neutral";
    public Brush CurrentBackground { get; private set; }
    public Brush CurrentOnBgText { get; private set; } = DarkText;
    public Brush CurrentOnBgSubtle { get; private set; } = DarkSubtle;
    public event Action? Changed;

    public ThemeService()
    {
        _themes = new List<BackgroundTheme>
        {
            // ---- Helle Designs (dunkle Schrift, weisse Karten) ----
            Light("neutral",   "Neutral",                 Solid("#E4E7EC")),
            Light("creme",     "Warmes Creme",            Solid("#F0E9DD")),
            Light("grau",      "Sanfter Grauverlauf",     VGrad("#E6E9EF", "#D7DCE4")),
            Light("anthrazit", "Helles Anthrazitgrau",    VGrad("#D3D8E0", "#C2C9D4")),
            Light("blaugrau",  "Blau-Grau seriös",        VGrad("#DCE4EE", "#CBD7E6")),
            Light("weiss",     "Hell minimalistisch",     Solid("#F6F7F9")),
            Light("orangegrau","Soft Orange/Grau",        DiagGrad("#F3E3D5", "#E2E5EA")),
            Light("karten",    "Moderne Kartenoptik",     Solid("#DEE3EA")),
            Light("beratung",  "Ruhiger Beratungsmodus",  Solid("#E8E3D8")),
            Light("fokus",     "Fokusmodus Training",     VGrad("#DCE7E2", "#CCD9D3")),

            // ---- Dunkle Designs (helle Schrift, weisse Karten heben sich ab) ----
            Dark("nachtblau",   "Nachtblau",               Solid("#3A4F68")),
            Dark("anthrazitd",  "Dunkles Anthrazit",       Solid("#454B57")),
            Dark("schiefer",    "Schiefergrau",            VGrad("#4A535F", "#3C434E")),
            Dark("graphit",     "Graphit",                 Solid("#4A505A")),
            Dark("mitternacht", "Mitternacht",             VGrad("#364154", "#46546B")),
            Dark("tanne",       "Dunkles Tannengrün",      Solid("#3C5049")),
            Dark("bordeaux",    "Dunkles Bordeaux",        Solid("#4F3A45")),
            Dark("espresso",    "Espresso",                Solid("#4D423A")),
            Dark("petrol",      "Dunkles Petrol",          VGrad("#325862", "#284A53")),
            Dark("indigo",      "Indigo",                  DiagGrad("#423A5E", "#322A4F")),
        };

        CurrentBackground = _themes[0].Brush;
    }

    private static BackgroundTheme Light(string key, string name, Brush brush) =>
        new(key, name, brush, DarkText, DarkSubtle, false);

    private static BackgroundTheme Dark(string key, string name, Brush brush) =>
        new(key, name, brush, LightText, LightSubtle, true);

    public void Apply(string? key)
    {
        var theme = _themes.Find(t => t.Key == key) ?? _themes[0];
        CurrentKey = theme.Key;
        CurrentBackground = theme.Brush;
        CurrentOnBgText = theme.OnBgText;
        CurrentOnBgSubtle = theme.OnBgSubtle;
        Changed?.Invoke();
    }

    // ---- Pinsel-Hilfsfunktionen ----

    private static SolidColorBrush Solid(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    private static LinearGradientBrush VGrad(string from, string to) =>
        Grad(from, to, new Point(0, 0), new Point(0, 1));

    private static LinearGradientBrush DiagGrad(string from, string to) =>
        Grad(from, to, new Point(0, 0), new Point(1, 1));

    private static LinearGradientBrush Grad(string from, string to, Point start, Point end)
    {
        var g = new LinearGradientBrush { StartPoint = start, EndPoint = end };
        g.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(from), 0));
        g.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(to), 1));
        g.Freeze();
        return g;
    }
}
