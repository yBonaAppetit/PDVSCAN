$ErrorActionPreference = 'Stop'

$projectDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectFile = Join-Path $projectDirectory 'src\PdvBarcodeFilter\PdvBarcodeFilter.csproj'
$simulatorProjectFile = Join-Path $projectDirectory 'tools\PdvQrScannerSimulator\PdvQrScannerSimulator.csproj'
$outputDirectory = Join-Path $projectDirectory 'dist-1.4.0'
$simulatorOutputDirectory = Join-Path $projectDirectory 'simulator-dist-1.4.0'

dotnet publish $projectFile `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $outputDirectory `
    --source 'https://api.nuget.org/v3/index.json' `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishTrimmed=false `
    -p:DebugType=embedded

dotnet publish $simulatorProjectFile `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $simulatorOutputDirectory `
    --source 'https://api.nuget.org/v3/index.json' `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishTrimmed=false `
    -p:DebugType=embedded

Copy-Item `
    -LiteralPath (Join-Path $projectDirectory 'filtersettings.example.json') `
    -Destination (Join-Path $outputDirectory 'filtersettings.example.json') `
    -Force

Write-Host "Executável criado em: $outputDirectory\PdvBarcodeFilter.exe"
Write-Host "Simulador criado em: $simulatorOutputDirectory\PdvQrScannerSimulator.exe"
