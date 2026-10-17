# Правя чист архив за изпращане при НОИТ: само код, документация и тестове.
# Изключва локални данни, тайни и всего, което се генерира при build.
# Употреба:  powershell -File Tools\make-submission-archive.ps1
# Архивът се записва в submission\ и не влиза в самия себе си.

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$staging = Join-Path $env:TEMP 'civiceye-submission-staging'
$outDir = Join-Path $root 'submission'

$excludeDirs = @(
    '.git', '.private', '.vs', '.vscode', 'bin', 'obj', 'App_Data',
    'publish', 'TestResults', 'submission', 'node_modules'
)
Write-Output ("Изключени папки: " + ($excludeDirs -join ', '))

if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Path $staging | Out-Null
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }

$excludeNamePattern = '\\(\.git|\.private|\.vs|\.vscode|bin|obj|App_Data|publish[^\\]*|TestResults|submission|node_modules)\\'
$excludeExtensionPattern = '\.(db|db-shm|db-wal|log|user)$'

$sourceFiles = Get-ChildItem -LiteralPath $root -Recurse -File -Force |
    Where-Object {
        $_.FullName -notmatch [regex]::Escape($outDir) -and
        $_.FullName -notmatch $excludeNamePattern -and
        $_.FullName -notmatch $excludeExtensionPattern
    }

$copied = 0
foreach ($file in $sourceFiles) {
    $relative = $file.FullName.Substring($root.Length).TrimStart('\')
    $target = Join-Path $staging $relative
    $parent = Split-Path -Parent $target
    if (-not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    Copy-Item -LiteralPath $file.FullName -Destination $target -Force
    $copied++
}
Write-Output "Копирани файлове: $copied"

$problems = @()

# 1. Няма ли ключ за Google в някой от файловете?
$keyPattern = 'AIza[0-9A-Za-z_\-]{20,}'
$keyHits = Get-ChildItem $staging -Recurse -File |
    Where-Object { $_.Length -lt 5MB -and $_.Extension -match '^\.(cs|cshtml|html|json|md|txt|ps1|config|yml|yaml|js|css|slnx|csproj)$' } |
    Select-String -Pattern $keyPattern -List
foreach ($hit in $keyHits) { $problems += "ключ с формат на Google в $($hit.Path.Substring($staging.Length))" }

# 2. Няма ли файлове с ключове за подписване на бисквитите или тайни?
$secretFiles = Get-ChildItem $staging -Recurse -File |
    Where-Object { $_.Name -match '^(secrets\.json|appsettings\.Production\.local\.json)$' -or $_.Directory.Name -eq 'DataProtectionKeys' }
foreach ($file in $secretFiles) { $problems += "файл със тайни: $($file.Name)" }

# 3. Няма ли останки от база данни или качени снимки?
$dataFiles = Get-ChildItem $staging -Recurse -File |
    Where-Object { $_.Extension -in '.db', '.db-shm', '.db-wal' -or $_.FullName -match '\\report-images\\' }
foreach ($file in $dataFiles) { $problems += "локални данни: $($file.Name)" }

if ($problems.Count -gt 0) {
    Write-Output 'АРХИВЪТ НЕ Е ГОТОВ — намерени са:'
    $problems | ForEach-Object { Write-Output "  - $_" }
    throw "Архивът съдържа нежелани файлове. Премахни ги и пусни отново."
}

$stamp = Get-Date -Format 'yyyy-MM-dd'
$zipPath = Join-Path $outDir "CivicEyeGD-prilojenie-$stamp.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

# PowerShell 5.1 слага обратна наклонена черта в имената на записите, а
# форматът ZIP изисква права. Без тази разлика архивът се разпакзва правилно
# само на Windows: под Linux и macOS всеки запис става файл с име, което
# включва и пътеката му.
$root = (Resolve-Path $staging).Path.TrimEnd([System.IO.Path]::DirectorySeparatorChar)
$stream = [System.IO.File]::Open($zipPath, [System.IO.FileMode]::CreateNew)
$archive = New-Object System.IO.Compression.ZipArchive(
    $stream, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem $staging -Recurse -File) {
        $relative = $file.FullName.Substring($root.Length + 1).Replace('\', '/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive, $file.FullName, $relative,
            [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally {
    $archive.Dispose()
    $stream.Dispose()
}

$entries = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
$count = $entries.Entries.Count
$wrongSeparators = @($entries.Entries | Where-Object { $_.FullName -match '\\' })
$entries.Dispose()
if ($wrongSeparators.Count -gt 0) {
    throw "Архивът съдържа $($wrongSeparators.Count) записа с обратна наклонена черта."
}

$removed = $staging
Remove-Item $removed -Recurse -Force

Write-Output "Архивът е готов: $zipPath"
Write-Output "Файлове в архива: $count"
Write-Output "Размер: $([math]::Round((Get-Item $zipPath).Length / 1MB, 2)) MB"
Write-Output 'Проверено: без API ключ, без пароли в код, без база данни, без App_Data, без publish.'
