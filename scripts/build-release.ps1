[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $workspace 'artifacts'

dotnet test (Join-Path $workspace 'tests\CsvPeek.Tests\CsvPeek.Tests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet publish (Join-Path $workspace 'src\CsvPeek.App\CsvPeek.App.csproj') -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -p:PublishSingleFile=false -o (Join-Path $artifacts 'publish\win-x64')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build (Join-Path $workspace 'installer\CsvPeek.Installer.wixproj') -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Publicacion: $artifacts\publish\win-x64"
Write-Host "Instalador: $workspace\installer\bin\Release\CSV-Peek-Setup-x64.msi"
