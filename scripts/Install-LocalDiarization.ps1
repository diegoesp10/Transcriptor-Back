[CmdletBinding()]
param([string]$PythonPath = 'python', [switch]$DownloadModel)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$environmentRoot = Join-Path $projectRoot 'tools\diarization'
$venvPath = Join-Path $environmentRoot '.venv'
$venvPython = Join-Path $venvPath 'Scripts\python.exe'
if (-not (Test-Path -LiteralPath $venvPython)) {
    & $PythonPath -c 'import sys, sysconfig; assert (3,12) <= sys.version_info < (3,15) and not sysconfig.get_config_var("Py_GIL_DISABLED"), "Use standard Python 3.12 or 3.14 x64"'
    if ($LASTEXITCODE -ne 0) { throw 'Usa Python estándar x64 (3.12 o 3.14), con Add python.exe to PATH.' }
    & $PythonPath -m venv $venvPath
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo crear el entorno Python local.' }
}
& $venvPython -m pip install --upgrade pip
if ($LASTEXITCODE -ne 0) { throw 'No se pudo preparar pip.' }
& $venvPython -m pip install -r (Join-Path $PSScriptRoot 'diarization-requirements.txt') -c (Join-Path $PSScriptRoot 'diarization-constraints.txt')
if ($LASTEXITCODE -ne 0) { throw 'No se pudieron instalar las dependencias de diarización.' }
if ($DownloadModel) {
    Write-Host 'Acepta primero las condiciones: https://huggingface.co/pyannote/speaker-diarization-community-1'
    $previousToken = $env:HF_TOKEN
    $tokenPointer = [IntPtr]::Zero
    try {
        if ([string]::IsNullOrWhiteSpace($env:HF_TOKEN)) {
            $secureToken = Read-Host 'Token de lectura Hugging Face (oculto; no se guardará)' -AsSecureString
            $tokenPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureToken)
            $env:HF_TOKEN = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($tokenPointer)
        }
        & $venvPython (Join-Path $PSScriptRoot 'download_diarization.py') (Join-Path $environmentRoot 'models\community-1')
        if ($LASTEXITCODE -ne 0) { throw 'No se pudo preparar Community-1. Revisa los requisitos indicados.' }
    }
    finally {
        $env:HF_TOKEN = $previousToken
        if ($tokenPointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($tokenPointer) }
        if ($secureToken) { $secureToken.Dispose() }
    }
}
if ($DownloadModel) {
    Write-Host 'Instalación completada: entorno y modelo de hablantes preparados.'
} else {
    Write-Host 'Entorno instalado. Para descargar el modelo, ejecuta este script con -DownloadModel.'
}
