param(
    [Parameter(Mandatory = $true)]
    [string[]]$ExtensionId
)

$ErrorActionPreference = 'Stop'
$invalidIds = $ExtensionId | Where-Object { $_ -notmatch '^[a-p]{32}$' }
if ($invalidIds) {
    throw "Extension IDs must be 32 lowercase letters from a-p. Invalid: $($invalidIds -join ', ')"
}
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$hostProject = Join-Path $root 'NativeHost\BetterWinTab.NativeHost.csproj'
$hostOutput = Join-Path $root 'NativeHost\bin\Release\net8.0\BetterWinTab.NativeHost.exe'
$installRoot = Join-Path $env:LOCALAPPDATA 'BetterWinTab\NativeHost'
$installedHost = Join-Path $installRoot 'BetterWinTab.NativeHost.exe'
$manifestPath = Join-Path $installRoot 'com.betterwintab.tabs.json'
$registryPaths = @(
    'HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.betterwintab.tabs',
    'HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\com.betterwintab.tabs',
    'HKCU:\Software\BraveSoftware\Brave-Browser\NativeMessagingHosts\com.betterwintab.tabs'
)

if (-not (Test-Path $hostOutput)) {
    dotnet build $hostProject -c Release
}

New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
Get-Process BetterWinTab.NativeHost -ErrorAction SilentlyContinue | Stop-Process -Force
Copy-Item $hostOutput $installedHost -Force

$manifest = [ordered]@{
    name = 'com.betterwintab.tabs'
    description = 'BetterWinTab Chromium tab bridge'
    path = $installedHost
    type = 'stdio'
    allowed_origins = @($ExtensionId | ForEach-Object { "chrome-extension://$_/" })
}
$manifest | ConvertTo-Json | Set-Content -Path $manifestPath -Encoding utf8
foreach ($registryPath in $registryPaths) {
    New-Item -Path $registryPath -Force | Out-Null
    Set-ItemProperty -Path $registryPath -Name '(Default)' -Value $manifestPath
}

Write-Host "Native host installed for extension $ExtensionId"
Write-Host "Restart the browser and BetterWinTab."
