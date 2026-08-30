[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot

Push-Location $repositoryRoot
try {
    dotnet restore BluetoothAudioReceiver.sln
    dotnet format BluetoothAudioReceiver.sln --verify-no-changes --no-restore
    dotnet build BluetoothAudioReceiver.sln --configuration $Configuration --no-restore
    dotnet test tests/BluetoothAudioReceiver.Core.Tests/BluetoothAudioReceiver.Core.Tests.csproj `
        --configuration $Configuration --no-build
}
finally {
    Pop-Location
}
