#define MyAppName "Filtro de Código de Barras PDV"
#define MyAppVersion "1.4.0"
#define MyAppPublisher "PDV Local"
#define MyAppExeName "PdvBarcodeFilter.exe"
#define MySimulatorExeName "PdvQrScannerSimulator.exe"

[Setup]
AppId={{6B3DA25E-164A-4C0B-BCA0-EBAA83E8FA1C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
VersionInfoVersion=1.4.0.0
VersionInfoProductVersion=1.4.0.0
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\PdvBarcodeFilter
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=installer
OutputBaseFilename=PdvBarcodeFilter-Setup-{#MyAppVersion}-x64
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\{#MyAppExeName}
AppMutex=Local\PdvBarcodeFilter-8C642EF4-982D-43CF-A44E-70A64FD1256B
SetupLogging=yes

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "startup"; Description: "Iniciar automaticamente com privilégios elevados"; GroupDescription: "Inicialização:"; Flags: checkedonce
Name: "desktopicon"; Description: "Criar atalho na área de trabalho"; GroupDescription: "Atalhos:"; Flags: unchecked

[Files]
Source: "dist-1.4.0\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "dist-1.4.0\filtersettings.example.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "simulator-dist-1.4.0\{#MySimulatorExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autoprograms}\Simulador de leitor QR — Filtro PDV"; Filename: "{app}\{#MySimulatorExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--install-startup"; StatusMsg: "Configurando inicialização elevada..."; Flags: runhidden waituntilterminated; Tasks: startup
Filename: "{app}\{#MyAppExeName}"; Description: "Iniciar {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--remove-startup"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveElevatedStartup"
