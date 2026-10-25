#define MyAppName "EcoMode"
#define MyAppExe  "ecomode.exe"
#ifndef MyAppVer
  #define MyAppVer "1.0.0"
#endif
#ifndef MyAppNumericVer
  #define MyAppNumericVer "1.0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "bin\Release\net9.0-windows\win-x64\publish"
#endif
#define MyPublisher "ToolsLib"
#define MyURL "https://github.com/ToolsLib/EcoMode"

[Setup]
AppId={{A4D0A0D8-7B4C-4CF1-9C5E-AAAA1111BBBB}}
AppName={#MyAppName}
AppVersion={#MyAppVer}
VersionInfoVersion={#MyAppNumericVer}
AppPublisher={#MyPublisher}
AppPublisherURL={#MyURL}
DefaultDirName={pf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableDirPage=no
DisableProgramGroupPage=no
OutputBaseFilename=EcoModeSetup
Compression=lzma
SolidCompression=yes
; Needs admin for HKLM/Policies + powercfg usage
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
;SetupIconFile=app_simplified.ico

[Files]
; Point Source to your publish folder
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop icon"; GroupDescription: "Additional icons:"; Flags: unchecked

[Run]
; Offer to launch after install
Filename: "{app}\{#MyAppExe}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Optional: remove per-user config
Type: filesandordirs; Name: "{userappdata}\EcoMode"
