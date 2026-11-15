# Regenerates the DOCX and the first PDF of the documentation from the HTML
# source with the locally installed Word. Nothing else touches the page setup:
# the HTML import lands on a Letter page with single spacing, while the
# regulation asks for A4, Times New Roman 12 pt and 1,5 line spacing, so the
# paper size, the margins and the spacing are forced here.
#
# The running header and the page numbers are NOT added here - driving them
# through COM left Word hanging on an invisible dialog. apply-page-style.ps1
# writes them into the package instead, and export-pdf.ps1 then redraws the
# PDF with the fields resolved. Tools/make-documentation.ps1 runs the three
# steps in the right order.
#
# Keep this file ASCII: the console of Git Bash mangles Cyrillic, so results
# are reported as numbers only.
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
foreach ($in in @($Html)) {
    if (-not (Test-Path -LiteralPath $in)) { throw "Missing file: $in" }
}

$src = [string](Resolve-Path -LiteralPath $Html).Path
$docx = [string](Split-Path -Parent $Docx) + '\' + (Split-Path -Leaf $Docx)
$pdf = [string](Split-Path -Parent $Pdf) + '\' + (Split-Path -Leaf $Pdf)

$word = New-Object -ComObject Word.Application
$word.Visible = $false
$word.DisplayAlerts = 0
try {
    $doc = $word.Documents.Open($src, $false, $false)
    # HTML import leaves the pictures as links; without this the saved DOCX
    # has no media folder and breaks when the folder moves.
    foreach ($shape in $doc.InlineShapes) {
        if ($shape.LinkFormat) {
            $shape.LinkFormat.SavePictureWithDocument = $true
        }
    }
    $page = $doc.PageSetup
    $page.PaperSize = 7                       # 7 = wdPaperA4
    $page.Orientation = 0                     # 0 = wdOrientPortrait
    $page.TopMargin = $word.CentimetersToPoints(2.5)
    $page.BottomMargin = $word.CentimetersToPoints(2.5)
    $page.LeftMargin = $word.CentimetersToPoints(2.0)
    $page.RightMargin = $word.CentimetersToPoints(2.0)
    $doc.Content.ParagraphFormat.LineSpacingRule = 1   # 1 = wdLineSpace1pt5

    # 12 = wdFormatXMLDocument, 17 = wdFormatPDF
    $doc.SaveAs2($docx, [int]12)
    $doc.ExportAsFixedFormat($pdf, [int]17)
    Write-Output ('PAGES=' + $doc.ComputeStatistics([int]2))
    Write-Output ('WORDS=' + $doc.ComputeStatistics([int]0))
    $doc.Close([bool]$false)
} finally {
    $word.Quit()
    [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($word)
}

Write-Output ('DOCX_BYTES=' + (Get-Item -LiteralPath $docx).Length)
Write-Output ('PDF_BYTES=' + (Get-Item -LiteralPath $pdf).Length)
