# Правя чист архив за изпращане при НОИТ: само код, документация и тестове.
# Изключва локални данни, тайни и всичко, което се генерира при build.
# Употреба:  powershell -File Tools\make-submission-archive.ps1
#           powershell -File Tools\make-submission-archive.ps1 -IncludeExecutable
# Архивът се записва в submission\ и не влиза в самия себе си.
# С ключа -IncludeExecutable се прави и втори архив с изпълнимите кодове;
# регламентът ги иска отделно от изходния текст.

param(
    [switch]$IncludeExecutable
)

$ErrorActionPreference = 'Stop'

# Една и съща упаковка за двата архива. PowerShell 5.1 слага обратна наклонена
# черта в имената на записите, а форматът ZIP изисква права. Без тази разлика
# архивът се разпакчва правилно само на Windows: под Linux и macOS всеки запис
# става файл с име, което включва и пътеката му.
function New-SubmissionZip {
    param([string]$SourceRoot, [string]$ZipPath)

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem

    $rootPath = (Resolve-Path $SourceRoot).Path.TrimEnd([System.IO.Path]::DirectorySeparatorChar)
    $stream = [System.IO.File]::Open($ZipPath, [System.IO.FileMode]::CreateNew)
    $archive = New-Object System.IO.Compression.ZipArchive(
        $stream, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem -LiteralPath $rootPath -Recurse -File) {
            $relative = $file.FullName.Substring($rootPath.Length + 1).Replace('\', '/')
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $archive, $file.FullName, $relative,
                [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally {
        $archive.Dispose()
        $stream.Dispose()
    }

    $entries = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    $count = $entries.Entries.Count
    $wrongSeparators = @($entries.Entries | Where-Object { $_.FullName -match '\\' })
    $entries.Dispose()
    if ($wrongSeparators.Count -gt 0) {
        throw "Архивът съдържа $($wrongSeparators.Count) записа с обратна наклонена черта."
    }

    return $count
}

$root = Split-Path -Parent $PSScriptRoot
$staging = Join-Path $env:TEMP 'civiceye-submission-staging'
$outDir = Join-Path $root 'submission'

$excludeDirs = @(
    '.git', '.private', '.vs', '.vscode', 'bin', 'obj', 'App_Data и App_Data-backup',
    'publish', 'TestResults', 'submission', 'node_modules', 'shots'
)
Write-Output ("Изключени папки: " + ($excludeDirs -join ', '))

# Скриптът за автоматично качване носи паролата на хостинг акаунта като
# чист текст до името на сайта. За журито тя е безполезна и е изтичане,
# затова файлът не се копира, а проверката по-долу го спира и по съдържание.
$excludeLeafNames = @('deploy.bat.txt')

if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Path $staging | Out-Null
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }

# Всяко копие на папката с данни е извън архива, не само оригиналът:
# в резервното копие има ключ за подпис на бисквитите и снимките,
# които гражданин е качил.
# 'shots' е временната папка, в която локални проверки са слагали
# изход от build-а; в нея няма нито ред от проекта.
$excludeNamePattern = '\\(\.git|\.private|\.vs|\.vscode|bin|obj|App_Data[^\\]*|publish[^\\]*|TestResults|submission|node_modules|shots)\\'
$excludeExtensionPattern = '\.(db|db-shm|db-wal|log|user)$'

$sourceFiles = Get-ChildItem -LiteralPath $root -Recurse -File -Force |
    Where-Object {
        $_.FullName -notmatch [regex]::Escape($outDir) -and
        $_.FullName -notmatch $excludeNamePattern -and
        $_.FullName -notmatch $excludeExtensionPattern -and
        $_.Name -notin $excludeLeafNames
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

# 2b. Същите тайни по съдържание, не по име: файлът за автоматично качване
# носи паролата на хостинг акаунта в един ред. Ако се казва по друг начин, пак
# няма право да влезе в архива. Проверката е по името на инструмента за
# публикуване, сглобено на две, за да не се открои скриптът, когато проверява
# себе си; думата не се среща нито в кода, нито в документите, защото живият
# сайт се качва през панела на хоста.
$deployHits = Get-ChildItem $staging -Recurse -File |
    Where-Object { $_.Length -lt 5MB -and $_.Extension -match '^\.(cs|cshtml|html|json|md|txt|ps1|config|yml|yaml|js|bat)$' } |
    Select-String -Pattern ('ms' + 'deploy') -List
foreach ($hit in $deployHits) {
    $problems += "скрипт за качване с парола в $($hit.Path.Substring($staging.Length))"
}

# 3. Няма ли останки от база данни или качени снимки?
$dataFiles = Get-ChildItem $staging -Recurse -File |
    Where-Object { $_.Extension -in '.db', '.db-shm', '.db-wal' -or $_.FullName -match '\\report-images\\' }
foreach ($file in $dataFiles) { $problems += "локални данни: $($file.Name)" }

# 4. Няма ли в кодовия архив сглобен изход? Регламентът иска изходен код и
# изпълними кодове, но като две неща: тук влиза само кодът, а библиотеките
# вървят във втория архив, който се прави с -IncludeExecutable. Преди време
# една временна папка с такъв изход се беше промъкнала вътре и архивът скочи
# от 12 MB на 107 MB.
$binaryFiles = Get-ChildItem $staging -Recurse -File |
    Where-Object { $_.Extension -in '.dll', '.exe', '.pdb', '.so', '.dylib' }
foreach ($file in $binaryFiles) { $problems += "сглобен изход: $($file.Name)" }

# 5. Вътрешни пътеки от този компютър. Word оставя в .docx външни връзки към
#    оригиналните снимки, а в тях се чете потребителското име на Windows;
#    за журито те са едновременно излишна лична информация и счупени линкове.
#    Проверката гледа и текстовите файлове, и XML-частите вътре в пакетите,
#    защото при компресията пътеките не се виждат като сурови байтове.
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$user = $env:USERNAME
# Самото име на схемата се сглобява на две, за да не се открои скриптът
# като файл с вътрешна пътека, когато проверява себе си.
$needle = 'file:' + '///'
$textExt = '.md', '.html', '.htm', '.txt', '.json', '.cshtml', '.ps1', '.yml', '.yaml', '.cs', '.css', '.js'
foreach ($file in Get-ChildItem $staging -Recurse -File) {
    $where = $file.FullName.Substring($staging.Length)
    if ($file.Extension -in '.docx', '.pptx', '.xlsx') {
        $package = [System.IO.Compression.ZipFile]::OpenRead($file.FullName)
        try {
            foreach ($entry in $package.Entries) {
                if ($entry.FullName -notmatch '\.(xml|rels)$') { continue }
                $stream = $entry.Open()
                try {
                    $reader = New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::ASCII)
                    $body = $reader.ReadToEnd()
                } finally { $stream.Dispose() }
                if ($body -like ('*' + $needle + '*') -or ($user -and $body -like ('*' + $user + '*'))) {
                    $problems += "вътрешна пътека в $where -> $($entry.FullName)"
                }
            }
        } finally { $package.Dispose() }
    }
    elseif ($file.Extension -in $textExt) {
        $body = Get-Content -LiteralPath $file.FullName -Raw
        if ($body -like ('*' + $needle + '*') -or ($user -and $body -like ('*' + $user + '*'))) {
            $problems += "вътрешна пътека в $where"
        }
    }
}

if ($problems.Count -gt 0) {
    Write-Output 'АРХИВЪТ НЕ Е ГОТОВ — намерени са:'
    $problems | ForEach-Object { Write-Output "  - $_" }
    throw "Архивът съдържа нежелани файлове. Премахни ги и пусни отново."
}

$stamp = Get-Date -Format 'yyyy-MM-dd'

# --- Архивът с кода ---
$zipPath = Join-Path $outDir "CivicEyeGD-prilojenie-$stamp.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

$count = New-SubmissionZip -SourceRoot $staging -ZipPath $zipPath
Remove-Item $staging -Recurse -Force

Write-Output "Архивът с кода е готов: $zipPath"
Write-Output "Файлове в архива: $count"
Write-Output "Размер: $([math]::Round((Get-Item $zipPath).Length / 1MB, 2)) MB"
Write-Output 'Проверено: без API ключ, без пароли в код, без база данни, без App_Data, без publish.'

# --- Архивът с изпълнимите кодове ---
# Регламентът иска до изходния код и „изпълними кодове" с „указания за
# инсталация и изпълнение". Те идват в свой файл, защото са библиотеки:
# смесени с кода, вдигат пакета от 12 MB на стотици, а отделен архив журито
# сваля само когато предпочете да пусне проекта само вместо през живия адрес.
if ($IncludeExecutable) {
    $pubSrc = Join-Path $root 'publish'
    $pubDll = Join-Path $pubSrc 'CivicEyeGD.dll'
    if (-not (Test-Path -LiteralPath $pubDll)) {
        throw "Липсва $pubDll. Пусни първо командата от README: dotnet publish CivicEyeGD.csproj -c Release -r win-x64 --self-contained false -o publish"
    }

    # Изпратен архив с по-стар код от последната промяна е по-зле от без
    # архив: журито вижда грешка, която отдавна е поправена.
    $newestSource = Get-ChildItem -LiteralPath $root -Recurse -File |
        Where-Object { $_.Extension -in '.cs', '.cshtml', '.csproj', '.css', '.js' -and
                       $_.FullName -notmatch $excludeNamePattern } |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($newestSource.LastWriteTimeUtc -gt (Get-Item -LiteralPath $pubDll).LastWriteTimeUtc) {
        Write-Output "ПРЕДУПРЕЖДЕНИЕ: publish-ът е по-стар от промяната в $($newestSource.Name). Поднови го и пусни скрипта отново."
    }

    # Публикуването без `-r win-x64` носи библиотеки за Linux, macOS и
    # Android; само един такъв .so тежи 38 MB. За Windows стигат runtimes\win*.
    $pubStaging = Join-Path $env:TEMP 'civiceye-submission-exec'
    if (Test-Path $pubStaging) { Remove-Item $pubStaging -Recurse -Force }
    New-Item -ItemType Directory -Path $pubStaging | Out-Null

    # Publish-ът може да е донесъл локални остатъци: под папка shots в него
    # стои копие от временен проект, а manifest-ът за статични ресурси в
    # Development режим носи абсолютни пътеки от тази машина. Нито едното не
    # трябва да стига до журито.
    $pubExcludePattern = '\\(runtimes\\(?!win)|App_Data[^\\]*|report-images|shots\\|\.git\\)'
    foreach ($file in (Get-ChildItem -LiteralPath $pubSrc -Recurse -File -Force)) {
        if ($file.FullName -match $pubExcludePattern -or
            $file.FullName -match $excludeExtensionPattern -or
            $file.Name -in $excludeLeafNames) { continue }
        $relative = $file.FullName.Substring($pubSrc.Length).TrimStart('\')
        $target = Join-Path $pubStaging $relative
        $parent = Split-Path -Parent $target
        if (-not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
        Copy-Item -LiteralPath $file.FullName -Destination $target -Force
    }

    # Publish-ът може да е донесъл локалните данни или ключа, ако някога са
    # стояли до проекта; тук се проверява само това, защото останалото го
    # видя кодовият архив.
    $execProblems = @()
    foreach ($file in (Get-ChildItem $pubStaging -Recurse -File)) {
        if ($file.Extension -in '.db', '.db-shm', '.db-wal') {
            $execProblems += "локални данни: $($file.Name)"
        }
        if ($file.Name -match '^(secrets\.json|appsettings\.Production\.local\.json|seed-accounts\.json)$') {
            $execProblems += "файл със тайни: $($file.Name)"
        }
        if ($file.Length -lt 5MB -and $file.Extension -in '.json', '.config', '.xml') {
            $body = Get-Content -LiteralPath $file.FullName -Raw
            if ($body -match 'AIza[0-9A-Za-z_\-]{20,}') { $execProblems += "ключ с формат на Google в $($file.Name)" }
            if ($user -and $body -like ('*' + $user + '*')) { $execProblems += "вътрешна пътека в $($file.Name)" }
        }
    }
    if ($execProblems.Count -gt 0) {
        Write-Output 'АРХИВЪТ С ИЗПЪЛНИМИТЕ КОДОВЕ НЕ Е ГОТОВ — намерени са:'
        $execProblems | ForEach-Object { Write-Output "  - $_" }
        throw "Извади ги от publish-а и пусни отново."
    }

    # Указанията за инсталация и изпълнение също са изрична част от
    # изискването; стоят в README, но журито отваря този архив, без да е
    # разархивирал кода, затова се повтарят на най-видимото място в него.
    $runInfo = @'
CivicEye GD - изпълнима част (framework-dependent, Windows x64)

Какво е нужно
  .NET 10 ASP.NET Core Runtime (не целия SDK):
  https://dotnet.microsoft.com/en-us/download/dotnet/10.0

Стартиране
  1. Извади архива в собствена папка.
  2. Отвори конзола в тази папка и изпълни:  dotnet CivicEyeGD.dll
  3. Отвори http://localhost:5000
  При първото стартиране приложението само създава базата си в подпапка
  App_Data до модула; там остават и качените снимки.

Демо акаунти
  Сайтът тръгва и без тях, но не създава профили за ролите, ако паролите не
  са зададени предварително. Задай ги в същата конзола и чак след това
  стартирай модула:
    set SeedAccounts__CitizenPassword=PAROLA-ZA-KOMISIYATA
    set SeedAccounts__AdminPassword=PAROLA-ZA-KOMISIYATA
    set SeedAccounts__SecondCitizenPassword=PAROLA-ZA-KOMISIYATA
    dotnet CivicEyeGD.dll
  Имейлите на трите роли са в README.md, раздел "Демо акаунти". Пароли не
  са написани никъде в архива.

Без инсталация
  https://civiceye.runasp.net - същият код, пуснат като интернет-базиран
  проект.

Контейнер (алтернатива за всеки оператор)
  docker build -t civiceye-gd .
  docker run -p 8080:8080 civiceye-gd
  Dockerfile-ът е в архива с кода; слага библиотеките за шрифтовете, без
  които PDF отчетът спира под Linux.
'@
    Set-Content -LiteralPath (Join-Path $pubStaging 'IZPALNENIE.txt') -Value $runInfo -Encoding UTF8

    $execZip = Join-Path $outDir "CivicEyeGD-izpalnimo-$stamp.zip"
    if (Test-Path $execZip) { Remove-Item $execZip -Force }
    $execCount = New-SubmissionZip -SourceRoot $pubStaging -ZipPath $execZip
    Remove-Item $pubStaging -Recurse -Force

    $execMb = [math]::Round((Get-Item $execZip).Length / 1MB, 2)
    Write-Output "Архивът с изпълнимите кодове е готов: $execZip"
    Write-Output "Записи: $execCount, размер: $execMb MB"
    if ($execMb -gt 80) {
        Write-Output 'Размерът сочи, че publish-ът не е с -r win-x64: в архива има native библиотеки за други платформи.'
    }
}
