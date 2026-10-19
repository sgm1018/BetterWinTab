$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
if (Get-Process -Name BetterWinTab -ErrorAction SilentlyContinue) {
    throw 'Cierra BetterWinTab desde Settings > General > Close BetterWinTab antes de abrir la nueva compilación.'
}
Push-Location $taskRoot
try {
    & dotnet build BetterWinTab.csproj -p:Platform=x64 '-p:OutDir=bin\NotesPluginCheck\' -v:minimal
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo compilar BetterWinTab.' }
    Start-Process -FilePath (Join-Path $taskRoot 'bin\NotesPluginCheck\BetterWinTab.exe') -WorkingDirectory $taskRoot -WindowStyle Hidden
}
finally { Pop-Location }
