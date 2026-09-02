param(
    [string]$SvgDirectory = (Join-Path $PSScriptRoot '..\book\images')
)

$ErrorActionPreference = 'Stop'
$failures = [System.Collections.Generic.List[string]]::new()
$files = Get-ChildItem -LiteralPath $SvgDirectory -Filter '*.svg' | Sort-Object Name

if ($files.Count -eq 0) {
    throw "No SVG files found in $SvgDirectory"
}

foreach ($file in $files) {
    try {
        [xml]$document = Get-Content -Raw -LiteralPath $file.FullName
    }
    catch {
        $failures.Add("$($file.Name): invalid XML - $($_.Exception.Message)")
        continue
    }

    $root = $document.DocumentElement
    if ($root.LocalName -ne 'svg') {
        $failures.Add("$($file.Name): root element is not svg")
    }
    if ([string]::IsNullOrWhiteSpace($root.GetAttribute('viewBox'))) {
        $failures.Add("$($file.Name): missing viewBox")
    }
    if ($root.GetAttribute('data-theme') -notin @('monochrome', 'color')) {
        $failures.Add("$($file.Name): data-theme must be monochrome or color")
    }

    $namespace = [System.Xml.XmlNamespaceManager]::new($document.NameTable)
    $namespace.AddNamespace('s', 'http://www.w3.org/2000/svg')

    if ($null -eq $document.SelectSingleNode('/s:svg/s:title', $namespace)) {
        $failures.Add("$($file.Name): missing direct title element")
    }
    if ($null -eq $document.SelectSingleNode('/s:svg/s:desc', $namespace)) {
        $failures.Add("$($file.Name): missing direct desc element")
    }
    if ($document.SelectNodes('//s:image', $namespace).Count -gt 0) {
        $failures.Add("$($file.Name): embedded raster image is not allowed")
    }

    foreach ($marker in $document.SelectNodes('//s:marker', $namespace)) {
        if ($marker.GetAttribute('markerUnits') -ne 'userSpaceOnUse') {
            $failures.Add("$($file.Name): marker '$($marker.GetAttribute('id'))' must use userSpaceOnUse")
        }
        if ($marker.GetAttribute('overflow') -ne 'visible') {
            $failures.Add("$($file.Name): marker '$($marker.GetAttribute('id'))' must set overflow=visible")
        }
    }

    foreach ($node in $document.SelectNodes('//*[@marker-end or @marker-start]', $namespace)) {
        if ([string]::IsNullOrWhiteSpace($node.GetAttribute('data-from')) -or
            [string]::IsNullOrWhiteSpace($node.GetAttribute('data-to'))) {
            $failures.Add("$($file.Name): arrowed connector must declare data-from and data-to")
        }
    }
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Output "FAIL: $_" }
    exit 1
}

Write-Output "PASS: validated $($files.Count) SVG files in $SvgDirectory"
