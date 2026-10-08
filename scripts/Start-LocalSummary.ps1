[CmdletBinding()]
param([switch]$PullModel)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$logs = Join-Path $projectRoot '.cli'
New-Item -ItemType Directory -Path $logs -Force | Out-Null
$ollama = Get-Command ollama -ErrorAction Stop
$ready = $false
try { Invoke-RestMethod 'http://127.0.0.1:11434/api/version' -TimeoutSec 2 | Out-Null; $ready = $true } catch { }
if (-not $ready) {
    $previousCloud = $env:OLLAMA_NO_CLOUD
    $previousHost = $env:OLLAMA_HOST
    try {
        $env:OLLAMA_NO_CLOUD = '1'
        $env:OLLAMA_HOST = '127.0.0.1:11434'
        Start-Process -FilePath $ollama.Source -ArgumentList 'serve' -WindowStyle Hidden -WorkingDirectory $projectRoot `
            -RedirectStandardOutput (Join-Path $logs 'ollama.stdout.log') -RedirectStandardError (Join-Path $logs 'ollama.stderr.log') | Out-Null
    }
    finally { $env:OLLAMA_NO_CLOUD = $previousCloud; $env:OLLAMA_HOST = $previousHost }
    for ($attempt = 0; $attempt -lt 15; $attempt++) {
        try { Invoke-RestMethod 'http://127.0.0.1:11434/api/version' -TimeoutSec 1 | Out-Null; $ready = $true; break } catch { }
        Start-Sleep -Milliseconds 500
    }
    if (-not $ready) { throw 'Ollama no respondió. Ejecuta ollama serve y revisa el puerto 11434.' }
}
if ($PullModel) {
    $previousHost = $env:OLLAMA_HOST
    try {
        $env:OLLAMA_HOST = '127.0.0.1:11434'
        & $ollama.Source pull 'qwen3:8b'
        if ($LASTEXITCODE -ne 0) { throw 'No se pudo descargar qwen3:8b.' }
    }
    finally { $env:OLLAMA_HOST = $previousHost }
}
Write-Host 'Ollama está disponible en http://127.0.0.1:11434. Modelo de resumen: qwen3:8b.'
