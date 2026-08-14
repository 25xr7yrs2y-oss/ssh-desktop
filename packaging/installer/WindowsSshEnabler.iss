#define ProductName "Windows SSH Enabler"
#define ProductExe "WindowsSshEnabler.exe"

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

#ifndef AppStageDir
  #error AppStageDir must point to the validated application staging directory.
#endif

#ifndef InstallerOutputDir
  #error InstallerOutputDir must point to the installer artifact directory.
#endif

#ifndef PublisherName
  #define PublisherName "Publisher Name (configure before release)"
#endif

[Setup]
; Stable application identity. Do not change this AppId between upgrades.
AppId={{B1D84CE8-E5D1-4B27-89E8-A72F1A0A6365}
AppName={#ProductName}
AppVersion={#AppVersion}
AppVerName={#ProductName} {#AppVersion}
AppPublisher={#PublisherName}
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#PublisherName}
VersionInfoDescription={#ProductName} installer
VersionInfoProductName={#ProductName}
DefaultDirName={autopf}\Windows SSH Enabler
DefaultGroupName=Windows SSH Enabler
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
OutputDir={#InstallerOutputDir}
OutputBaseFilename=WindowsSshEnabler-Setup-x64
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
Uninstallable=yes
UninstallDisplayName={#ProductName}
UninstallDisplayIcon={app}\{#ProductExe}
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
ChangesAssociations=no
ChangesEnvironment=no
AllowNoIcons=no
MinVersion=10.0
UsePreviousAppDir=yes
UsePreviousGroup=yes

#ifdef AppIconFile
SetupIconFile={#AppIconFile}
#endif

[Files]
; Deliberately list the sole payload instead of recursively copying a directory.
Source: "{#AppStageDir}\{#ProductExe}"; DestDir: "{app}"

[Icons]
Name: "{group}\Windows SSH Enabler"; Filename: "{app}\{#ProductExe}"; WorkingDir: "{app}"
; A Desktop shortcut is intentionally installed by default for the requested UX.
Name: "{autodesktop}\Windows SSH Enabler"; Filename: "{app}\{#ProductExe}"; WorkingDir: "{app}"

; There are intentionally no [Run], [Registry], [Tasks], [Code], [UninstallRun],
; or [UninstallDelete] sections. Setup and uninstall only manage this exact file,
; their standard uninstall metadata, and the two shortcuts above.
