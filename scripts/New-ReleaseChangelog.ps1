param(
    [Parameter(Mandatory = $true)]
    [string]$Tag,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

$tagCommit = (git rev-parse "$Tag^{commit}").Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($tagCommit)) {
    throw "No se pudo resolver el commit del tag '$Tag'."
}

# Describe desde el padre del tag para excluir siempre la versión que se está publicando.
$previousTag = git describe --tags --abbrev=0 --match 'v[0-9]*' "$tagCommit^" 2>$null
if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($previousTag)) {
    $previousTag = $previousTag.Trim()
    $commitRange = "$previousTag..$tagCommit"
    Write-Host "Generando changelog desde $previousTag hasta $Tag."
}
else {
    # En la primera versión, incluir todo el historial alcanzable desde el tag.
    $previousTag = $null
    $commitRange = $tagCommit
    Write-Host "No se encontró una versión anterior; se incluirá el historial completo."
}

$format = '%H%x1f%s%x1f%b%x1e'
$logOutput = git log --reverse "--format=$format" $commitRange
if ($LASTEXITCODE -ne 0) {
    throw "No se pudieron leer los commits del rango '$commitRange'."
}

$sections = [ordered]@{
    Added = [System.Collections.Generic.List[string]]::new()
    Changed = [System.Collections.Generic.List[string]]::new()
    Fixed = [System.Collections.Generic.List[string]]::new()
    Dependencies = [System.Collections.Generic.List[string]]::new()
}
$headings = @{
    Added = '### 🚀 Added'
    Changed = '### ♻️ Changed'
    Fixed = '### 🐛 Fixed'
    Dependencies = '### 📦 Dependencies Updated'
}
$sectionByType = @{
    add = 'Added'
    change = 'Changed'
    fix = 'Fixed'
    libupdate = 'Dependencies'
}

function ConvertTo-ChangelogEntry([string]$Text) {
    $normalized = $Text.Trim() -replace '^(?:[-*•])\s*', ''
    return "- $normalized"
}

$rawLog = $logOutput -join "`n"
$records = [regex]::Split($rawLog, [string][char]0x1e)
foreach ($record in $records) {
    if ([string]::IsNullOrWhiteSpace($record)) { continue }

    $fields = [regex]::Split($record.Trim(), [string][char]0x1f, 3)
    if ($fields.Count -lt 2) { continue }

    $hash = $fields[0].Trim()
    $subject = $fields[1].Trim()
    $body = if ($fields.Count -ge 3) { $fields[2] } else { '' }
    if ($subject -notmatch '^(?<type>add|fix|change|libupdate):\s*(?<title>.*)$') { continue }

    $section = $sectionByType[$Matches.type.ToLowerInvariant()]
    $details = [System.Collections.Generic.List[object]]::new()
    $title = $Matches.title.Trim()
    if (-not [string]::IsNullOrWhiteSpace($title)) {
        $details.Add([pscustomobject]@{ Section = $section; Text = (ConvertTo-ChangelogEntry $title) })
    }

    $currentSection = $section
    foreach ($line in ($body -split "\r?\n")) {
        $entry = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($entry)) { continue }

        if ($entry -match '^(?<type>add|fix|change|libupdate):\s*(?<title>.*)$') {
            $currentSection = $sectionByType[$Matches.type.ToLowerInvariant()]
            $sectionTitle = $Matches.title.Trim()
            if (-not [string]::IsNullOrWhiteSpace($sectionTitle)) {
                $details.Add([pscustomobject]@{
                    Section = $currentSection
                    Text = (ConvertTo-ChangelogEntry $sectionTitle)
                })
            }
        }
        else {
            $details.Add([pscustomobject]@{
                Section = $currentSection
                Text = (ConvertTo-ChangelogEntry $entry)
            })
        }
    }

    if ($details.Count -eq 0) {
        $shortHash = if ($hash.Length -ge 7) { $hash.Substring(0, 7) } else { $hash }
        $details.Add([pscustomobject]@{ Section = $section; Text = "- Commit $shortHash sin descripción." })
    }

    foreach ($detail in $details) {
        $sections[$detail.Section].Add($detail.Text)
    }
}

$date = (Get-Date).ToString('yyyy-MM-dd')
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("## [$Version] - $date")
$lines.Add('')
if ($previousTag) {
    $lines.Add("Los cambios se recogen desde la versión $previousTag hasta $Tag.")
}
else {
    $lines.Add('Los cambios se recogen desde el historial inicial hasta esta versión.')
}
$lines.Add('')

$hasChanges = $false
foreach ($sectionName in $sections.Keys) {
    if ($sections[$sectionName].Count -eq 0) { continue }
    $hasChanges = $true
    $lines.Add($headings[$sectionName])
    foreach ($entry in $sections[$sectionName]) {
        $lines.Add($entry)
    }
    $lines.Add('')
}

if (-not $hasChanges) {
    $lines.Add('No hay commits con los prefijos add, fix, change o libupdate en este rango.')
}

$outputDirectory = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}
Set-Content -Path $OutputPath -Value $lines -Encoding utf8
Write-Host "Changelog generado en $OutputPath."
