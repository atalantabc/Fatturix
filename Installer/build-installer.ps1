$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $root 'FattureViewer.csproj'
$installerProject = Join-Path $PSScriptRoot 'Installer.csproj'
$appOutput = Join-Path $root 'artifacts\app-win-x64'
$installerOutput = Join-Path $root 'artifacts\installer-win-x64'

dotnet publish $appProject -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o $appOutput

dotnet publish $installerProject -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -o $installerOutput

$installer = Join-Path $installerOutput 'Installer.exe'
$final = Join-Path $root 'FattureViewerInstaller-3.1.1.exe'
Copy-Item -Force $installer $final
Write-Host "Installer creato: $final"
