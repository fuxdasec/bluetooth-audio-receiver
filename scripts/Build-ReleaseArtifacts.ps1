[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [string]$InformationalVersion = $Version,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'src\BluetoothAudioReceiver.App\BluetoothAudioReceiver.App.csproj'
$artifactDirectory = Join-Path $repositoryRoot 'artifacts\release'
$publishDirectory = Join-Path $repositoryRoot 'artifacts\publish-win-x64'
$executablePath = Join-Path $artifactDirectory 'BluetoothAudioReceiver.exe'
$checksumPath = Join-Path $artifactDirectory 'SHA256SUMS.txt'
$fileVersion = "$Version.0"

$versionParts = $Version.Split('.') | ForEach-Object { [int]$_ }
if ($versionParts | Where-Object { $_ -gt 65535 }) {
    throw 'Each version component must be between 0 and 65535.'
}

foreach ($directory in @($artifactDirectory, $publishDirectory)) {
    if (Test-Path $directory) {
        Remove-Item -LiteralPath $directory -Recurse -Force
    }
    New-Item -ItemType Directory -Path $directory | Out-Null
}

dotnet publish $projectPath `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    --output $publishDirectory `
    -p:Version=$Version `
    -p:AssemblyVersion=$fileVersion `
    -p:FileVersion=$fileVersion `
    -p:InformationalVersion=$InformationalVersion `
    -p:IncludeSourceRevisionInInformationalVersion=false `
    -p:DebugSymbols=false `
    -p:DebugType=None
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$publishedFiles = @(Get-ChildItem -LiteralPath $publishDirectory -File -Recurse)
if ($publishedFiles.Count -ne 1 -or $publishedFiles[0].Name -ne 'BluetoothAudioReceiver.App.exe') {
    $names = ($publishedFiles | ForEach-Object FullName) -join ', '
    throw "Expected exactly one published executable, found: $names"
}

$publishedFileVersion = $publishedFiles[0].VersionInfo.FileVersion
if ($publishedFileVersion -ne $fileVersion) {
    throw "Unexpected executable version: '$publishedFileVersion'; expected '$fileVersion'."
}

Move-Item -LiteralPath $publishedFiles[0].FullName -Destination $executablePath
$hash = (Get-FileHash -LiteralPath $executablePath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $checksumPath -Value "$hash  BluetoothAudioReceiver.exe" -Encoding utf8NoBOM

Write-Host "Executable: $executablePath"
Write-Host "Checksum:   $checksumPath"
