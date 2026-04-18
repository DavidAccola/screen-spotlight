#define MyAppName "Screen Spotlight"
#define MyAppVersion "1.0.2"
#define MyAppPublisher "David Accola"
#define MyAppURL "https://github.com/DavidAccola/screen-spotlight"
#define MyAppExeName "Screen Spotlight.exe"
#define PublishDir "SpotlightOverlay\bin\Release\net8.0-windows\win-x64\publish"

[Setup]
AppId={{A3F2B1C4-7E8D-4F9A-B2C3-D4E5F6A7B8C9}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={localappdata}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=installer-output
OutputBaseFilename=Screen Spotlight Setup v{#MyAppVersion}
SetupIconFile=SpotlightOverlay\assets\spotlightIcon.ico
VersionInfoVersion={#MyAppVersion}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
CloseApplicationsFilter=*.exe
RestartApplications=no
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"
Name: "startupentry"; Description: "Start Screen Spotlight when Windows starts"; GroupDescription: "Windows startup:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\Screen Spotlight.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\D3DCompiler_47_cor3.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\PenImc_cor3.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\PresentationNative_cor3.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\vcruntime140_cor3.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\wpfgfx_cor3.dll"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startupentry

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Screen Spotlight"; Flags: nowait postinstall skipifsilent
