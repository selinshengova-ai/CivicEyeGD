# Adds the page furniture - the running header and the "page N of M" footer -
# to a Word-generated DOCX and removes the external picture links that Word
# leaves in word/_rels/document.xml.rels. It works on the OPC package
# directly, so no Word instance is involved and nothing can hang on a modal
# dialog. The Bulgarian texts of the two parts live in page-header.xml and
# page-footer.xml next to this script and are read as UTF-8; the script itself
# stays ASCII.
#
# Word writes the page and word counts of the pre-layout HTML import into
# docProps/app.xml, so a document of 58 pages claims three pages in its file
# properties. Pass what export-pdf.ps1 reported to have them corrected.
param(
    [Parameter(Mandatory = $true)][string]$In,
    [Parameter(Mandatory = $true)][string]$Out,
    [int]$Pages = 0,
    [int]$Words = 0
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$hdrSrc = Join-Path $PSScriptRoot 'page-header.xml'
$ftrSrc = Join-Path $PSScriptRoot 'page-footer.xml'
foreach ($needed in @($In, $hdrSrc, $ftrSrc)) {
    if (-not (Test-Path -LiteralPath $needed)) { throw "Missing file: $needed" }
}

function Read-Entry {
    param($Zip, [string]$Name)
    $entry = $Zip.GetEntry($Name)
    if ($null -eq $entry) { throw "missing part: $Name" }
    $stream = $entry.Open()
    try {
        $reader = New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::UTF8)
        return $reader.ReadToEnd()
    } finally { $stream.Dispose() }
}

function Write-Entry {
    param($Zip, [string]$Name, [string]$Text)
    $old = $Zip.GetEntry($Name)
    if ($null -ne $old) { $old.Delete() }
    $entry = $Zip.CreateEntry($Name, [System.IO.Compression.CompressionLevel]::Optimal)
    $stream = $entry.Open()
    try {
        $writer = New-Object System.IO.StreamWriter($stream, (New-Object System.Text.UTF8Encoding($false)))
        $writer.Write($Text)
        $writer.Flush()
        $writer.Dispose()
    } finally { $stream.Dispose() }
}

# Styling a package twice would add the header and the footer a second time,
# so the copy onto itself is only skipped when the two paths really match.
$inPath = [string](Resolve-Path -LiteralPath $In).Path
$outPath = [string](Split-Path -Parent $Out) + '\' + (Split-Path -Leaf $Out)
if ($inPath -ne $outPath) { Copy-Item -LiteralPath $In -Destination $Out -Force }

$dropped = New-Object System.Collections.Generic.List[string]
$zip = [System.IO.Compression.ZipFile]::Open($Out, [System.IO.Compression.ZipArchiveMode]::Update)
try {
    $relsName = 'word/_rels/document.xml.rels'
    $rels = Read-Entry -Zip $zip -Name $relsName
    if ($rels -match 'relationships/header') {
        throw 'this package already carries a header; style the unstyled DOCX instead'
    }

    $maxId = 0
    foreach ($m in [regex]::Matches($rels, 'Id="rId(\d+)"')) {
        $n = [int]$m.Groups[1].Value
        if ($n -gt $maxId) { $maxId = $n }
    }

    $rels = [regex]::Replace($rels, '<Relationship\b[^>]*/>', {
            param($m)
            if ($m.Value -match 'TargetMode="External"' -or $m.Value -match 'Target="file:') {
                $id = [regex]::Match($m.Value, 'Id="([^"]+)"').Groups[1].Value
                if ($id) { [void]$dropped.Add($id) }
                return ''
            }
            return $m.Value
        })

    $document = Read-Entry -Zip $zip -Name 'word/document.xml'
    foreach ($id in $dropped) {
        $document = $document.Replace(' r:link="' + $id + '"', '')
    }

    $headerId = 'rId' + ($maxId + 1)
    $footerId = 'rId' + ($maxId + 2)
    $relAdd = '<Relationship Id="' + $headerId + '" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header" Target="header1.xml" />' +
        '<Relationship Id="' + $footerId + '" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer" Target="footer1.xml" />'
    $rels = $rels.Replace('</Relationships>', $relAdd + '</Relationships>')

    $refs = '<w:headerReference w:type="default" r:id="' + $headerId + '" />' +
        '<w:footerReference w:type="default" r:id="' + $footerId + '" />'
    $document = [regex]::Replace($document, '<w:sectPr\b[^>]*>', {
            param($m)
            return $m.Value + $refs
        })
    if ($document -notmatch 'headerReference') { throw 'no sectPr found in document.xml' }

    $ct = Read-Entry -Zip $zip -Name '[Content_Types].xml'
    if ($ct -notmatch 'wordprocessingml\.header\+xml') {
        $ctAdd = '<Override PartName="/word/header1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml" />' +
            '<Override PartName="/word/footer1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml" />'
        $ct = $ct.Replace('</Types>', $ctAdd + '</Types>')
    }

    if ($Pages -gt 0 -or $Words -gt 0) {
        $app = Read-Entry -Zip $zip -Name 'docProps/app.xml'
        if ($Pages -gt 0) { $app = [regex]::Replace($app, '<Pages>\d+</Pages>', ('<Pages>' + $Pages + '</Pages>')) }
        if ($Words -gt 0) { $app = [regex]::Replace($app, '<Words>\d+</Words>', ('<Words>' + $Words + '</Words>')) }
        Write-Entry -Zip $zip -Name 'docProps/app.xml' -Text $app
    }

    Write-Entry -Zip $zip -Name 'word/document.xml' -Text $document
    Write-Entry -Zip $zip -Name $relsName -Text $rels
    Write-Entry -Zip $zip -Name '[Content_Types].xml' -Text $ct
    Write-Entry -Zip $zip -Name 'word/header1.xml' -Text ([System.IO.File]::ReadAllText($hdrSrc, [System.Text.Encoding]::UTF8))
    Write-Entry -Zip $zip -Name 'word/footer1.xml' -Text ([System.IO.File]::ReadAllText($ftrSrc, [System.Text.Encoding]::UTF8))
} finally { $zip.Dispose() }

Write-Output ('LINKS_REMOVED=' + $dropped.Count)
Write-Output ('HEADER_REF=' + $headerId + ' FOOTER_REF=' + $footerId)
Write-Output ('OUT_BYTES=' + (Get-Item -LiteralPath $Out).Length)
