[CmdletBinding()]
param([ValidateSet('small','large-v3-turbo')][string]$Model = 'large-v3-turbo')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$modelDirectory = Join-Path $projectRoot 'tools\whisper\models'
New-Item -ItemType Directory -Path $modelDirectory -Force | Out-Null
$fileName = "ggml-$Model.bin"
$destination = Join-Path $modelDirectory $fileName
$expectedHash = if ($Model -eq 'small') { '1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b' } else { '1fc70f774d38eb169993ac391eea357ef47c88757ef72ee5943879b7e8e2bc69' }
if ((Test-Path -LiteralPath $destination) -and ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -eq $expectedHash)) {
    Write-Host "$fileName ya está descargado y verificado."
    return
}
$temporary = Join-Path $modelDirectory ($fileName + '.' + [Guid]::NewGuid().ToString('N') + '.download')
try {
    & curl.exe --fail --location --retry 2 --silent --show-error --output $temporary "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/$fileName"
    if ($LASTEXITCODE -ne 0) { throw 'Falló la descarga del modelo Whisper.' }
    if ((Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash -ne $expectedHash) { throw 'La huella del modelo no coincide. No se utilizará el archivo.' }
    Move-Item -LiteralPath $temporary -Destination $destination -Force
    Write-Host "$fileName descargado y verificado."
}
finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary } }
