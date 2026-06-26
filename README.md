# MPU-Trainer

Windows-Desktop-Anwendung zur MPU-Vorbereitung für die **BfK – Beratungsstelle für Kraftfahreignung**.
Der Psychologe lädt einen Leitfaden hoch, die KI erzeugt daraus offene MPU-Übungsfragen, der Klient
beantwortet sie mündlich, und die Anwendung erstellt eine natürliche Musterantwort in Ich-Form als
Audio. Die gesamte Sitzung lässt sich als MP3 sichern.

> **Stack:** .NET 8 · WPF · MVVM (CommunityToolkit.Mvvm) · SQLite (EF Core) · NAudio · System.Speech

---

## Funktionsumfang

**Dashboard**
- Erfassung der Klientendaten: Vorname, Familienname, Geburtsdatum
- Projektname, thematischer Schwerpunkt und **frei wählbare Anzahl zu generierender Fragen** (5–80)
- Übersicht und Wiederaufnahme der letzten Projekte

**Leitfaden hochladen**
- Word- (.docx) und PDF-Dokumente
- Automatische Textextraktion mit Vorschau
- Hinweis bei vermutlich gescannten PDFs ohne auswählbaren Text

**Fragenübersicht**
- KI-generierte Fragen in einer editierbaren Tabelle
- Kategorie, Schwierigkeit, Status und Musterantwort pro Frage anpassbar
- Fragen einzeln ergänzen/löschen oder komplett neu generieren

**Trainingsmodus**
- Frage per Sprachausgabe (TTS) vorlesen
- Antwort des Klienten aufnehmen (Mikrofon mit Pegelanzeige)
- Eigene Antwort erneut anhören
- KI-Musterantwort in Ich-Form als Text und Audio
- Vor- und Zurücknavigation durch alle Fragen
- **Export der gesamten Unterhaltung (Frage → Antwort → Musterantwort) als eine MP3-Datei**

**Einstellungen**
- Automatische Erkennung von Mikrofon und Lautsprecher (umschaltbar)
- Auswahl der TTS-Stimme und Lautstärke, mit Hörprobe
- KI-Anbindung: Anbieter (Anthropic / OpenAI-kompatibel), Modell, Base-URL, Temperatur, Max-Tokens
- Verbindungstest
- API-Key wird **verschlüsselt über die Windows-DPAPI** gespeichert (nicht im Klartext)

---

## Installationsdatei über GitHub erstellen (ohne Visual Studio)

Du brauchst kein Visual Studio. GitHub baut die Anwendung automatisch und erstellt eine fertige
**Setup-Datei**. So gehst du vor:

1. **Repository hochladen** (mit GitHub Desktop): den Ordner `MpuTrainer` als neues Repository
   veröffentlichen (Push auf `main`).
2. GitHub startet den Build automatisch. Alternativ im Reiter **Actions** den Workflow
   „Build MPU-Trainer" auswählen und auf **Run workflow** klicken.
3. Wenn der Lauf mit einem grünen Haken fertig ist, den Lauf öffnen und unten unter **Artifacts**
   die Datei **MPU-Trainer-Setup** herunterladen (kommt als ZIP).
4. ZIP entpacken → **MPU-Trainer-Setup.exe** ausführen → der Assistent installiert die App
   (kein Administrator nötig, Verknüpfung auf dem Desktop).

Es entstehen zwei Artefakte:
- **MPU-Trainer-Setup** – die Installationsdatei (empfohlen).
- **MPU-Trainer-portable** – nur `MpuTrainer.exe`, läuft direkt per Doppelklick ohne Installation.

Beides ist **eigenständig** – auf dem Zielrechner muss kein .NET installiert sein.

> **Saubere Download-Links per Release:** Wer in GitHub Desktop ein Tag wie `v1.0.0` setzt und pusht,
> bekommt automatisch ein **Release** mit Setup- und EXE-Datei als direkten Download (ohne ZIP).

---

## Systemvoraussetzungen

- Windows 10 (1809+) oder Windows 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022 (Workload „.NET-Desktopentwicklung") – empfohlen
- Mikrofon und Lautsprecher/Kopfhörer
- Für die Fragengenerierung: ein API-Key des gewählten KI-Anbieters

---

## Selbst bauen (optional, für Entwickler)

Die Anwendung ist Windows-spezifisch (WPF) und muss unter **Windows** gebaut werden.

**Mit Visual Studio 2022 (empfohlen)**
1. `MpuTrainer.sln` öffnen.
2. Visual Studio stellt die NuGet-Pakete beim ersten Build automatisch wieder her.
3. Mit `F5` starten (oder Strg+F5 ohne Debugger).

**Über die Kommandozeile**
```powershell
cd MpuTrainer
dotnet restore
dotnet run --project MpuTrainer
```

> **Hinweis:** Beim ersten Build werden NuGet-Pakete aus dem Internet geladen
> (`api.nuget.org`). Eine Offline-Umgebung benötigt einen vorbefüllten NuGet-Cache.

---

## Erste Schritte

1. **Einstellungen** öffnen → Mikrofon/Lautsprecher prüfen, TTS-Stimme wählen, API-Key und Modell
   eintragen, **Verbindung testen**, **speichern**.
2. **Dashboard** → Klientendaten erfassen, Fragenanzahl wählen, **Projekt erstellen**.
3. **Leitfaden** auswählen → Text wird extrahiert → **Fragen generieren**.
4. **Fragenübersicht** prüfen/anpassen → **Training starten**.
5. Im **Trainingsmodus** Frage anhören, Antwort aufnehmen, Musterantwort anhören,
   am Ende **als MP3 speichern**.

---

## Datenschutz & Speicherorte

Alle personenbezogenen Daten verbleiben lokal auf dem Rechner. Standardpfad:

```
%AppData%\MpuTrainer\
├── mputrainer.db          SQLite-Datenbank (Projekte, Klientendaten, Fragen)
├── settings.json          Geräte- und KI-Einstellungen (ohne API-Key)
├── secret.bin             API-Key, DPAPI-verschlüsselt (nur aktueller Windows-Benutzer)
└── Projekte\
    └── projekt_XXXX\
        ├── tts\           vorgelesene Fragen / Musterantworten (WAV)
        ├── aufnahmen\     Antworten des Klienten (WAV)
        └── sitzung_*.mp3  zusammengefügte Gesamtsitzung
```

Der Speicherort der Projekte lässt sich in den Einstellungen ändern.

**Wichtig:** Bei Nutzung einer externen KI werden Leitfaden-Inhalte und Fragen zur Verarbeitung an den
gewählten Anbieter übertragen. Die Audiodateien verlassen den Rechner nicht – die Sprachausgabe nutzt
die lokale Windows-Sprachsynthese (SAPI5).

---

## Projektstruktur

```
MpuTrainer/
├── App.xaml(.cs)              Einstiegspunkt, Dependency Injection
├── MainWindow.xaml(.cs)       Rahmen mit Seitennavigation
├── Models/                    Datenmodelle und Enums
├── Data/                      EF-Core-DbContext, Projekt-Repository
├── Services/                  Einstellungen, Secret-Store (DPAPI), Navigation, Dialoge, Converter
├── Audio/                     Geräteerkennung, TTS, Aufnahme, Wiedergabe, MP3-Zusammenführung
├── AI/                        KI-Clients (Anthropic / OpenAI-kompatibel), Prompts, Fragengenerierung
├── DocumentProcessing/        Text-Extraktion aus .docx und .pdf
├── ViewModels/                MVVM-ViewModels
└── Views/                     XAML-Ansichten
```

---

## Ausblick (geplante Erweiterungen)

- **OCR** für gescannte PDF-Leitfäden
- Optionale Cloud-Sprachausgabe (natürlichere Stimmen)
- Erweiterte Projektverwaltung (Suche, Archiv, Export)
- Windows-Installer (MSIX/MSI) für die Verteilung ohne Visual Studio

---

*Intern für die BfK GmbH. Nicht zur Weitergabe an Dritte bestimmt.*
