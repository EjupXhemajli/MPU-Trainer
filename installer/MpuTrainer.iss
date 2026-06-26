; ============================================================
;  Inno Setup Script – MPU-Trainer
;  Erzeugt: installer\Output\MPU-Trainer-Setup.exe
;  Installation pro Benutzer (kein Administrator erforderlich)
; ============================================================

#define MyAppName "MPU-Trainer"
#define MyAppVersion "0.18.0"
#define MyAppPublisher "BfK - Beratungsstelle fuer Kraftfahreignung GmbH"
#define MyAppExeName "MpuTrainer.exe"

[Setup]
AppId={{B7A3F2C1-9E44-4D2A-8B61-2C7E9A4F1D33}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\MPU-Trainer
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=Output
OutputBaseFilename=MPU-Trainer-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce

[Files]
; Alle Dateien aus dem Veröffentlichungs-Ordner uebernehmen (in der Regel nur die EXE).
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
