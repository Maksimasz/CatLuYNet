param(
    [string]$PublishDirectory,
    [string]$Compiler = 'C:\Program Files\Inno Setup 7\ISCC.exe',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'Output')
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$version = ([xml](Get-Content -LiteralPath (Join-Path $repo 'YouTubeRadio.csproj'))).Project.PropertyGroup.Version
if (!$PublishDirectory) { $PublishDirectory = Join-Path $repo "publish/win-x64-$version" }
$publish = (Resolve-Path -LiteralPath $PublishDirectory).Path
if (!(Test-Path -LiteralPath (Join-Path $publish 'YouTubeRadio.exe'))) { throw 'Publish the application before building the installer.' }
if (!(Test-Path -LiteralPath $Compiler)) { throw "Inno Setup 7 compiler not found: $Compiler" }
$fileList = Join-Path ([IO.Path]::GetTempPath()) ('catlu-installer-' + [Guid]::NewGuid().ToString('N') + '.iss')
try {
    $entries = foreach ($file in (Get-ChildItem -LiteralPath $publish -Recurse -File | Sort-Object FullName)) {
        $relative = $file.FullName.Substring($publish.Length).TrimStart('\')
        if ($file.Name -in @('stations.json', 'settings.json') -or $file.Extension -in @('.bak', '.mp3')) {
            throw "Personal data must not be packaged: $relative"
        }
        $directory = Split-Path $relative -Parent
        $destination = if ($directory) { '{app}\' + $directory } else { '{app}' }
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        # Escape literal braces in source and destination paths for Inno constants.
        $source = $file.FullName.Replace('{', '{{')
        $destination = '{app}' + $destination.Substring(5).Replace('{', '{{')
        'Source: "{0}"; DestDir: "{1}"; Flags: ignoreversion; Check: ShouldInstallFile(''{2}'')' -f $source, $destination, $hash
    }
    [IO.File]::WriteAllLines($fileList, [string[]]$entries, [Text.UTF8Encoding]::new($true))
    & $Compiler "/O$OutputDirectory" "/DAppVersion=$version" "/DFileList=$fileList" (Join-Path $PSScriptRoot 'CatLuYNet.iss')
    if ($LASTEXITCODE -ne 0) { throw "Installer compiler failed with exit code $LASTEXITCODE" }
} finally {
    if (Test-Path -LiteralPath $fileList) { Remove-Item -LiteralPath $fileList }
}
