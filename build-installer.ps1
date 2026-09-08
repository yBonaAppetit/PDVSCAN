param(
    [string]$PfxPath,
    [SecureString]$PfxPassword,
    [string]$TimestampServer = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
$projectDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$installerDirectory = Join-Path $projectDirectory 'installer'
$exePath = Join-Path $projectDirectory 'dist-1.4.0\PdvBarcodeFilter.exe'
$simulatorExePath = Join-Path $projectDirectory 'simulator-dist-1.4.0\PdvQrScannerSimulator.exe'
$installerPath = Join-Path $projectDirectory 'installer\PdvBarcodeFilter-Setup-1.4.0-x64.exe'
$issPath = Join-Path $projectDirectory 'installer.iss'

New-Item -ItemType Directory -Path $installerDirectory -Force | Out-Null

function Find-InnoCompiler {
    $candidates = @(
        (Join-Path $env:LocalAppData 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    throw 'Inno Setup 6 não encontrado. Instale JRSoftware.InnoSetup pelo winget.'
}

function Sign-Artifact([string]$Path, [System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate) {
    $signature = Set-AuthenticodeSignature `
        -FilePath $Path `
        -Certificate $Certificate `
        -HashAlgorithm SHA256 `
        -TimestampServer $TimestampServer `
        -IncludeChain All

    if ($signature.Status -ne 'Valid') {
        throw "Falha ao assinar $Path. Status: $($signature.Status); mensagem: $($signature.StatusMessage)"
    }
}

& (Join-Path $projectDirectory 'build-release.ps1')

$certificate = $null
if ($PfxPath) {
    if (-not $PfxPassword) {
        $PfxPassword = Read-Host 'Senha do certificado PFX' -AsSecureString
    }

    $certificate = Get-PfxCertificate -FilePath $PfxPath -Password $PfxPassword
    Sign-Artifact -Path $exePath -Certificate $certificate
    Sign-Artifact -Path $simulatorExePath -Certificate $certificate
}

$innoCompiler = Find-InnoCompiler
& $innoCompiler $issPath
if ($LASTEXITCODE -ne 0) {
    throw "O Inno Setup retornou o código $LASTEXITCODE."
}

if ($certificate) {
    Sign-Artifact -Path $installerPath -Certificate $certificate
    Write-Host 'Filtro, simulador e instalador assinados digitalmente.'
}
else {
    Write-Warning 'Artefatos gerados sem assinatura. Forneça -PfxPath para assinatura confiável.'
}

Write-Host "Instalador criado em: $installerPath"
