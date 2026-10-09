#define AppName "CatLu YNet"
#define AppVersion "1.1.1"
#define AppPublisher "CatLu"
#define AppExeName "YouTubeRadio.exe"

[Setup]
AppId={{B9A3D57E-1E58-4E72-8DA7-7D7F376FD8AE}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\CatLu YNet
DefaultGroupName=CatLu YNet
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=CatLuYNet-1.1.1-Setup
SetupIconFile=..\Assets\CatLuNet2.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayIcon={app}\{#AppExeName}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Дополнительно:"; Flags: unchecked

[Files]
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\CatLu YNet"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\CatLu YNet"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Запустить CatLu YNet"; Flags: nowait postinstall skipifsilent
