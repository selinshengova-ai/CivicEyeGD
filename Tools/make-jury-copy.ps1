# Builds the copy of the documentation that is handed to the jury: the same
# file as the one in the repository, with the three demo passwords written
# into the cells of table 7 and named after the project's registration
# number, as annex 1 of the regulation requires.
#
# The passwords never come from this file, from the repository or from the
# console. They are read from App_Data/seed-accounts.json - the same file the
# application itself reads when it creates the demo accounts - and the
# generated documents are written into .private/, which .gitignore excludes
# and Tools/make-submission-archive.ps1 skips. So the only copy that ever
# holds a password is the one the jury receives.
#
# ASCII only on purpose: a row of table 7 is found by the e-mail address in
# its first column, so no Cyrillic literal is needed here and the console
# cannot mangle anything.
#
#   powershell -File Tools/make-jury-copy.ps1 -RegistrationNumber 12345 -DryRun
#   powershell -File Tools/make-jury-copy.ps1 -RegistrationNumber 12345
param(
    [Parameter(Mandatory = $true)][string]$RegistrationNumber,
    [string]$In = 'Docs\dokumentacia-civiceye-gd-v4.docx',
    [string]$SecretsFile = 'App_Data\seed-accounts.json',
    [string]$OutDir = '.private\jury',
    [switch]$DryRun
)
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path -LiteralPath (Join-Path $root 'CivicEyeGD.csproj'))) { $root = (Get-Location).Path }
Set-Location $root

if ($RegistrationNumber -notmatch '^[0-9]{3,10}$') {
    throw 'RegistrationNumber must be the digits from the registration form.'
}
foreach ($needed in @($In, $SecretsFile)) {
    if (-not (Test-Path -LiteralPath $needed)) { throw "Missing file: $needed" }
}

$seed = (Get-Content -LiteralPath $SecretsFile -Raw -Encoding UTF8 | ConvertFrom-Json).SeedAccounts
if (-not $seed) { throw 'The secrets file has no SeedAccounts section.' }

# The seeder reads the three accounts under these keys; the address of each is
# the label of its row in table 7 and can be overridden in the same file.
$accounts = @(
    [pscustomobject]@{ Email = 'citizen.demo@civiceye.test'; Password = $seed.CitizenPassword;       EmailKey = 'CitizenEmail' }
    [pscustomobject]@{ Email = 'citizen.two@civiceye.test';  Password = $seed.SecondCitizenPassword; EmailKey = 'SecondCitizenEmail' }
    [pscustomobject]@{ Email = 'admin.demo@civiceye.test';   Password = $seed.AdminPassword;         EmailKey = 'AdminEmail' }
)
foreach ($account in $accounts) {
    if ($seed.$($account.EmailKey)) { $account.Email = $seed.$($account.EmailKey) }
    if ([string]::IsNullOrWhiteSpace($account.Password)) {
        throw "The secrets file has no password for $($account.Email)."
    }
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$outDocx = (Resolve-Path -LiteralPath $OutDir).Path + '\' + $RegistrationNumber + '.docx'
$outPdf = (Resolve-Path -LiteralPath $OutDir).Path + '\' + $RegistrationNumber + '.pdf'
Copy-Item -LiteralPath $In -Destination $outDocx -Force

$word = New-Object -ComObject Word.Application
$word.Visible = $false
$word.DisplayAlerts = 0
try {
    $doc = $word.Documents.Open([string](Resolve-Path -LiteralPath $outDocx).Path, $false, $false)
    $found = @()
    $filled = 0

    foreach ($account in $accounts) {
        for ($t = 1; $t -le $doc.Tables.Count; $t++) {
            $table = $doc.Tables.Item($t)
            foreach ($cell in $table.Range.Cells) {
                # Cell text carries the end-of-cell marks; only the printable
                # characters are compared.
                $label = ($cell.Range.Text -replace '[^\x21-\x7E]', '')
                if ($label -ne $account.Email) { continue }
                $col = $cell.ColumnIndex + 1
                $found += "table=$t row=$($cell.RowIndex) col=$col account=$($account.Email)"
                if ($DryRun) { continue }
                $target = $table.Cell($cell.RowIndex, $col)
                $range = $target.Range
                # The last character of a cell range is the end-of-cell mark;
                # shrinking by one character keeps it out of the replacement,
                # which then covers the whole body of the cell.
                [void]$range.MoveEnd([int]1, [int]-1)
                $range.Text = $account.Password
                $left = ($target.Range.Text -replace '[^\x21-\x7E]', '')
                if ($left -ne $account.Password) {
                    throw ("Table 7 cell for $($account.Email) was not replaced cleanly.")
                }
                $filled++
            }
        }
    }

    if ($found.Count -ne $accounts.Count) {
        throw ('Expected the three demo addresses in one table, found ' + $found.Count +
               '. ' + ($found -join ' | '))
    }

    if ($DryRun) {
        Write-Output 'DRY_RUN=1'
        foreach ($line in $found) { Write-Output $line }
        $doc.Close([bool]$false)
    } else {
        $doc.Repaginate()
        # 12 = wdFormatXMLDocument, 17 = wdFormatPDF
        $doc.SaveAs2([string]$outDocx, [int]12)
        $doc.ExportAsFixedFormat([string]$outPdf, [int]17)
        Write-Output ('PAGES=' + $doc.ComputeStatistics([int]2))
        $doc.Close([bool]$false)
        Write-Output "FILLED_CELLS=$filled"
        Write-Output ('DOCX_BYTES=' + (Get-Item -LiteralPath $outDocx).Length)
        Write-Output ('PDF_BYTES=' + (Get-Item -LiteralPath $outPdf).Length)
    }
} finally {
    $word.Quit()
    [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($word)
}
