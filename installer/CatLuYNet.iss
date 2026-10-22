#define AppName "CatLu YNet"
#ifndef AppVersion
  #define AppVersion "1.1.11"
#endif
#ifndef FileList
  #error Build this installer using installer\Build-Installer.ps1
#endif
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
OutputBaseFilename=CatLuYNet-{#AppVersion}-Setup
SetupIconFile=..\Assets\CatLuNet2.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#AppExeName}
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; GroupDescription: "{cm:ExtraTasks}"; Flags: unchecked

[CustomMessages]
russian.DesktopIcon=Создать ярлык на рабочем столе
english.DesktopIcon=Create a desktop shortcut
russian.ExtraTasks=Дополнительно:
english.ExtraTasks=Additional tasks:
russian.ModeTitle=Обновление или полная установка
english.ModeTitle=Update or full installation
russian.ModeDescription=Выберите способ установки CatLu YNet
english.ModeDescription=Choose how to install CatLu YNet
russian.VersionInfo=Установлена версия %1. Версия установщика: %2.%n%nПлейлисты, настройки и музыка сохраняются в обоих режимах. После выбора запущенный плеер будет закрыт.
english.VersionInfo=Installed version: %1. Installer version: %2.%n%nBoth modes preserve playlists, settings and music. The running player will close after you choose a mode.
russian.UpdateMode=Обновить / восстановить — заменить только изменённые и отсутствующие файлы
english.UpdateMode=Update / repair — replace only changed and missing files
russian.FullMode=Полная установка — заменить все файлы программы
english.FullMode=Full installation — replace all application files
russian.NewerInstalled=Уже установлена более новая версия %1. Этот установщик содержит версию %2.
english.NewerInstalled=A newer version (%1) is already installed. This installer contains version %2.
russian.CloseFailed=Не удалось закрыть CatLu YNet. Закройте плеер вручную и повторите попытку. Установка не продолжена.
english.CloseFailed=Could not close CatLu YNet. Close the player manually and try again. Installation has not continued.
russian.StartPlayer=Запустить CatLu YNet
english.StartPlayer=Launch CatLu YNet

[Files]
Source: "Close-Player.ps1"; Flags: dontcopy
#include FileList

[Icons]
Name: "{autoprograms}\CatLu YNet"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\CatLu YNet"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:StartPlayer}"; Flags: nowait postinstall skipifsilent

[Code]
var
  InstalledDir, InstalledVersion: String;
  ModePage: TInputOptionWizardPage;

function ReadInstallation(Root: Integer): Boolean;
var
  Key, CandidateDir: String;
begin
  Key := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{B9A3D57E-1E58-4E72-8DA7-7D7F376FD8AE}_is1';
  Result := RegQueryStringValue(Root, Key, 'InstallLocation', CandidateDir);
  if Result then
    Result := FileExists(AddBackslash(CandidateDir) + '{#AppExeName}');
  if Result then
  begin
    InstalledDir := CandidateDir;
    if not RegQueryStringValue(Root, Key, 'DisplayVersion', InstalledVersion) then
      GetVersionNumbersString(AddBackslash(CandidateDir) + 'YouTubeRadio.dll', InstalledVersion);
  end;
end;

function InitializeSetup: Boolean;
var
  Existing, Incoming: Int64;
begin
  Result := True;
  if not ReadInstallation(HKLM64) then
    if not ReadInstallation(HKLM32) then
      if not ReadInstallation(HKCU64) then ReadInstallation(HKCU32);
  if (InstalledDir <> '') and StrToVersion(InstalledVersion, Existing) and
     StrToVersion('{#AppVersion}', Incoming) then
    if ComparePackedVersion(Existing, Incoming) > 0 then
    begin
      SuppressibleMsgBox(FmtMessage(CustomMessage('NewerInstalled'), [InstalledVersion, '{#AppVersion}']), mbError, MB_OK, IDOK);
      Result := False;
    end;
end;

procedure InitializeWizard;
begin
  if InstalledDir = '' then exit;
  WizardForm.DirEdit.Text := InstalledDir;
  ModePage := CreateInputOptionPage(wpWelcome, CustomMessage('ModeTitle'),
    CustomMessage('ModeDescription'), FmtMessage(CustomMessage('VersionInfo'), [InstalledVersion, '{#AppVersion}']), True, False);
  ModePage.Add(CustomMessage('UpdateMode'));
  ModePage.Add(CustomMessage('FullMode'));
  ModePage.SelectedValueIndex := 0;
end;

function IsUpdate: Boolean;
begin
  Result := InstalledDir <> '';
  if Result then Result := ModePage.SelectedValueIndex = 0;
end;

function ShouldInstallFile(ExpectedHash: String): Boolean;
var
  Target: String;
begin
  Result := True;
  Target := ExpandConstant(CurrentFileName);
  if not IsUpdate or not FileExists(Target) then exit;
  try
    Result := CompareText(GetSHA256OfFile(Target), ExpectedHash) <> 0;
    if not Result then Log('Unchanged file skipped: ' + Target);
  except
    Log('Cannot hash existing file, replacing: ' + Target);
  end;
end;

function ClosePlayer: Boolean;
var
  ExitCode: Integer;
begin
  ExtractTemporaryFile('Close-Player.ps1');
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' +
    ExpandConstant('{tmp}\Close-Player.ps1') + '" -AppDirectory "' +
    RemoveBackslashUnlessRoot(WizardDirValue) + '"', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  if Result then Result := ExitCode = 0;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if InstalledDir <> '' then
    if CurPageID = ModePage.ID then
    begin
      Result := ClosePlayer;
      if not Result then MsgBox(CustomMessage('CloseFailed'), mbError, MB_OK);
    end;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (InstalledDir <> '') and ((PageID = wpSelectDir) or
    (IsUpdate and (PageID = wpSelectTasks)));
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not ClosePlayer then Result := CustomMessage('CloseFailed');
end;

procedure RegisterExtraCloseApplicationsResources;
begin
  RegisterExtraCloseApplicationsResource(ExpandConstant('{app}\{#AppExeName}'));
end;
