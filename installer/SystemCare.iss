; Inno Setup script for SystemCare â€” builds a single SystemCare-Setup.exe installer.
; Compile with: "%LocalAppData%\Programs\Inno Setup 6\ISCC.exe" installer\SystemCare.iss
; The published single-file app (dist\SystemCare.exe) must exist first (dotnet publish).

#define MyAppName "SystemCare"
#define MyAppVersion "2.18.0"
#define MyAppPublisher "SystemCare"
#define MyAppExeName "SystemCare.exe"

[Setup]
; A stable AppId so future versions upgrade in place rather than installing side-by-side.
AppId={{B8E7C3A2-9F41-4D6E-A5C1-7E2F8B3D4A60}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
; Agreement shown on the wizard's License page; user must accept it to continue installing.
LicenseFile=..\EULA.txt
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=..\dist
OutputBaseFilename=SystemCare-Setup
SetupIconFile=..\src\SystemCare\Assets\app.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; The app needs administrator rights, so the installer requires them too.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoProductName={#MyAppName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\dist\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
