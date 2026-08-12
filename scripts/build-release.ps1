[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $workspace 'artifacts'
$propertiesPath = Join-Path $workspace 'Directory.Build.props'
$properties = [xml](Get-Content -LiteralPath $propertiesPath -Raw)
$version = [string]$properties.Project.PropertyGroup.CsvPeekVersion
if ([string]::IsNullOrWhiteSpace($version)) { throw 'Directory.Build.props no define CsvPeekVersion.' }
$publishDir = Join-Path $artifacts 'publish\win-x64'
$installerPath = Join-Path $workspace 'installer\bin\Release\CSV-Peek-Setup-x64.msi'
$checksumPath = "$installerPath.sha256"

dotnet test (Join-Path $workspace 'tests\CsvPeek.Tests\CsvPeek.Tests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet test (Join-Path $workspace 'tests\CsvPeek.App.Tests\CsvPeek.App.Tests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet publish (Join-Path $workspace 'src\CsvPeek.App\CsvPeek.App.csproj') -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -p:PublishSingleFile=false -o $publishDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build (Join-Path $workspace 'installer\CsvPeek.Installer.wixproj') -c Release -p:SkipCsvPeekPublish=true
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$publishedDll = Join-Path $publishDir 'CsvPeek.dll'
$publishedVersion = [System.Reflection.AssemblyName]::GetAssemblyName($publishedDll).Version.ToString(3)
if ($publishedVersion -ne $version) { throw "La aplicacion publicada es $publishedVersion; se esperaba $version." }
if (-not (Test-Path -LiteralPath $installerPath)) { throw "No se genero el MSI: $installerPath" }

$hash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $checksumPath -Value "$hash  CSV-Peek-Setup-x64.msi" -Encoding ascii

Write-Host "Version: $version"
Write-Host "Publicacion: $publishDir"
Write-Host "Instalador: $installerPath"
Write-Host "SHA-256: $hash"
