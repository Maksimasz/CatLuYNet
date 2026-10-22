param([string]$Compiler = 'C:\Program Files\Inno Setup 7\ISCC.exe')

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$root = Join-Path $repo ('.test-temp/installer-tests-' + [Guid]::NewGuid().ToString('N'))
$source = Join-Path $root 'source'
$target = Join-Path $root 'installed'
New-Item -ItemType Directory -Force -Path $source, $target | Out-Null

function Assert($condition, $message) {
    if (!$condition) { throw $message }
}

foreach ($name in @('same.txt', 'changed.txt', 'missing.txt')) {
    [IO.File]::WriteAllText((Join-Path $source $name), "new $name")
}
[IO.File]::WriteAllText((Join-Path $target 'same.txt'), 'new same.txt')
[IO.File]::WriteAllText((Join-Path $target 'changed.txt'), 'old content')
[IO.File]::WriteAllText((Join-Path $target 'stations.json'), 'personal data')
$oldDate = [DateTime]::new(2000, 1, 1, 0, 0, 0, [DateTimeKind]::Utc)
[IO.File]::SetLastWriteTimeUtc((Join-Path $target 'same.txt'), $oldDate)

# Exercise the production Pascal hash check in an isolated, non-admin installer.
# It has no uninstaller, registry entries, shortcuts, player or user-data actions.
$production = [IO.File]::ReadAllText((Join-Path $repo 'installer/CatLuYNet.iss'))
$hashCheck = $production.Substring($production.IndexOf('function ShouldInstallFile('))
$hashCheck = $hashCheck.Substring(0, $hashCheck.IndexOf('function ClosePlayer:'))
$script = @"
[Setup]
AppName=CatLu installer hash test
AppVersion=1.0
DefaultDirName=$target
OutputDir=$root
OutputBaseFilename=hash-test
PrivilegesRequired=lowest
Uninstallable=no
CreateUninstallRegKey=no
CloseApplications=no
DisableDirPage=yes
DisableProgramGroupPage=yes
[Files]
"@
foreach ($file in Get-ChildItem -LiteralPath $source -File) {
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    $script += "`r`nSource: `"$($file.FullName)`"; DestDir: `"{app}`"; Flags: ignoreversion; Check: ShouldInstallFile('$hash')"
}
$script += @"

[Code]
function IsUpdate: Boolean;
begin
  Result := ExpandConstant('{param:MODE|update}') <> 'full';
end;
$hashCheck
"@
$scriptPath = Join-Path $root 'hash-test.iss'
[IO.File]::WriteAllText($scriptPath, $script, [Text.UTF8Encoding]::new($true))
& $Compiler /Q $scriptPath
Assert ($LASTEXITCODE -eq 0) 'Test installer did not compile.'
$setup = Join-Path $root 'hash-test.exe'
foreach ($mode in @('update', 'full')) {
    $process = Start-Process -FilePath $setup -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/MODE=$mode", ('/LOG="' + (Join-Path $root "$mode.log") + '"')) -WindowStyle Hidden -Wait -PassThru
    Assert ($process.ExitCode -eq 0) "Installer failed in $mode mode."
    foreach ($name in @('same.txt', 'changed.txt', 'missing.txt')) {
        Assert ((Get-FileHash (Join-Path $source $name)).Hash -eq (Get-FileHash (Join-Path $target $name)).Hash) "Wrong file contents: $name"
    }
    Assert ([IO.File]::ReadAllText((Join-Path $target 'stations.json')) -eq 'personal data') 'Personal data changed.'
    $unchanged = [IO.File]::GetLastWriteTimeUtc((Join-Path $target 'same.txt')) -eq $oldDate
    Assert ($unchanged -eq ($mode -eq 'update')) "Wrong unchanged-file behavior in $mode mode."
}
& powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $repo 'installer/Close-Player.ps1') -AppDirectory $target
Assert ($LASTEXITCODE -eq 0) 'Player-close helper failed for a directory without a running player.'
Write-Output "PASS: unchanged files skipped, changed/missing files installed, full mode rewrites files, personal data preserved. Logs: $root"
