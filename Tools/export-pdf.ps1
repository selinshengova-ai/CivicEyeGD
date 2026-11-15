# Exports a DOCX to PDF with the locally installed Word without touching the
# document: it is opened read-only, repaginated so that the PAGE and NUMPAGES
# fields of the footer resolve, and saved through ExportAsFixedFormat. The
# reported page and word counts are the numbers of the finished document, so
# they are what apply-page-style.ps1 needs for the file properties.
param(
    [Parameter(Mandatory = $true)][string]$In,
    [Parameter(Mandatory = $true)][string]$Out
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $In)) { throw "Missing file: $In" }
$src = [string](Resolve-Path -LiteralPath $In).Path
$dst = [string](Split-Path -Parent $Out) + '\' + (Split-Path -Leaf $Out)

$word = New-Object -ComObject Word.Application
$word.Visible = $false
$word.DisplayAlerts = 0
try {
    $doc = $word.Documents.Open($src, $false, $true)
    $doc.Repaginate()
    Write-Output ('PAGES=' + $doc.ComputeStatistics([int]2))
    Write-Output ('WORDS=' + $doc.ComputeStatistics([int]0))
    $doc.ExportAsFixedFormat($dst, [int]17)   # 17 = wdFormatPDF
    $doc.Close([bool]$false)
} finally {
    $word.Quit()
    [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($word)
}
Write-Output ('PDF_BYTES=' + (Get-Item -LiteralPath $dst).Length)
