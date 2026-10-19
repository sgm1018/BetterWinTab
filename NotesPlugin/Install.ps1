$ErrorActionPreference = 'Stop'
Get-Command node -ErrorAction Stop | Out-Null
Get-Command codex -ErrorAction Stop | Out-Null
& codex plugin marketplace add $PSScriptRoot --json
if ($LASTEXITCODE -ne 0) { throw 'No se pudo registrar el marketplace local.' }
& codex plugin add 'betterwintab-notes@betterwintab-local' --json
if ($LASTEXITCODE -ne 0) { throw 'No se pudo instalar el complemento.' }
Write-Host 'BetterWinTab Notes instalado. Selecciónalo en un nuevo chat de Codex.'
