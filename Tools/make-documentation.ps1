# The whole documentation chain in one command: HTML to DOCX with the page
# setup of the regulation, then the running header and the page numbers, then
# the PDF that the jury reads. It is three scripts on purpose - the middle one
# must not run through COM, because driving headers and footers that way left
# Word hanging on an invisible dialog - and this wrapper only keeps the order
# and the numbers straight.
#
# The file properties of a Word document carry the page count of the layout
# made *before* the furniture was added, so the wrapper feeds the real count
# back into apply-page-style.ps1 and redraws the PDF once more when the two
# disagree. The unstyled DOCX is kept in the temporary folder, because styling
# the same package twice would add the header and the footer a second time.
#
#   powershell -File Tools\make-documentation.ps1
param(
    [string]$Html = '',
    [string]$Docx = '',
    [string]$Pdf = ''
)
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
if (-not $Html) { $Html = Join-Path $root 'Docs\dokumentacia-v4.html' }
if (-not $Docx) { $Docx = Join-Path $root 'Docs\dokumentacia-civiceye-gd-v4.docx' }
if (-not $Pdf) { $Pdf = Join-Path $root 'Docs\dokumentacia-civiceye-gd-v4.pdf' }

function Get-Count([string]$text, [string]$name) {
    $m = [regex]::Match($text, ('(?m)^' + $name + '=(\d+)\s*$'))
    if (-not $m.Success) { throw ('no ' + $name + ' reported') }
    return [int]$m.Groups[1].Value
}

$out = & (Join-Path $PSScriptRoot 'make-docs-formats.ps1') -Html $Html -Docx $Docx -Pdf $Pdf | Out-String
$pages = Get-Count $out 'PAGES'
$words = Get-Count $out 'WORDS'
Write-Output ('IMPORT_PAGES=' + $pages + ' IMPORT_WORDS=' + $words)

$raw = Join-Path $env:TEMP 'CivicEyeGD-docs-raw.docx'
Copy-Item -LiteralPath $Docx -Destination $raw -Force

for ($pass = 1; $pass -le 2; $pass++) {
    & (Join-Path $PSScriptRoot 'apply-page-style.ps1') -In $raw -Out $Docx -Pages $pages -Words $words | Out-Null
    $pdfOut = & (Join-Path $PSScriptRoot 'export-pdf.ps1') -In $Docx -Out $Pdf | Out-String
    $finalPages = Get-Count $pdfOut 'PAGES'
    $finalWords = Get-Count $pdfOut 'WORDS'
    Write-Output ('PASS' + $pass + '_PAGES=' + $finalPages + ' PASS' + $pass + '_WORDS=' + $finalWords)
    if ($finalPages -eq $pages -and $finalWords -eq $words) {
        Write-Output ('DOCUMENTATION_READY=1 PAGES=' + $finalPages + ' WORDS=' + $finalWords)
        Write-Output ('DOCX=' + $Docx)
        Write-Output ('PDF=' + $Pdf)
        return
    }
    $pages = $finalPages
    $words = $finalWords
}
throw 'the page count kept changing between passes'
