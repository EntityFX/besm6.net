# Вспомогательный скрипт: активная матрица CERNlib из эталона cernlib_test.cpp,
# проверка .f/expect и (опционально) генерация коммиченного manifest'а.
#
# Переносимость: корень репозитория вычисляется от расположения скрипта (plans/),
# эталон — параметром -RefRoot (по умолчанию <root>\ref).
#
# Использование:
#   pwsh -File plans\_count_cernlib.ps1
#   pwsh -File plans\_count_cernlib.ps1 -RefRoot D:\dubna
#   pwsh -File plans\_count_cernlib.ps1 -OutJson src\besm6.net\tests\Besm6.Tests\cernlib_manifest.json
#
# Exit code: 0 — всё в порядке; 1 — отсутствуют .f/expect-файлы или дубликаты; 2 — не найден эталон.
[CmdletBinding()]
param(
    [string]$RefRoot,
    [string]$OutJson
)
$ErrorActionPreference = 'Stop'

$root    = Split-Path $PSScriptRoot -Parent
$refRoot = if ($RefRoot) { $RefRoot } else { Join-Path $root 'ref' }
$cppPath = Join-Path $refRoot 'tests\cernlib_test.cpp'
if (-not (Test-Path $cppPath)) {
    Write-Host "Не найден эталон: $cppPath (укажите -RefRoot)"
    exit 2
}

# Активные вызовы (закомментированные строки не входят).
$cases = New-Object System.Collections.Generic.List[object]
foreach ($line in (Get-Content -LiteralPath $cppPath)) {
    if ($line -match '^\s*//') { continue }
    if ($line -match '^\s*test_cernlib\(\s*([12])\s*,\s*"([^"]+)"') {
        $cases.Add([pscustomobject]@{ Library = [int]$Matches[1]; Name = $Matches[2] })
    }
}

$lib1 = @($cases | Where-Object { $_.Library -eq 1 })
$lib2 = @($cases | Where-Object { $_.Library -eq 2 })
Write-Host ("lib1 active: " + $lib1.Count)
Write-Host ("lib2 active: " + $lib2.Count)
Write-Host ("total active: " + $cases.Count)

# Наличие .f и expect_*.txt.
$badF = 0; $badE = 0
foreach ($c in $cases) {
    $dir = Join-Path $refRoot ("tests\lib" + $c.Library)
    if (-not (Test-Path (Join-Path $dir ($c.Name + '.f')))) {
        Write-Host ("  нет .f    : lib" + $c.Library + '/' + $c.Name); $badF++
    }
    if (-not (Test-Path (Join-Path $dir ('expect_' + $c.Name + '.txt')))) {
        Write-Host ("  нет expect: lib" + $c.Library + '/' + $c.Name); $badE++
    }
}

# .f-файлы без активного теста.
foreach ($lib in 1, 2) {
    $dir = Join-Path $refRoot ("tests\lib" + $lib)
    if (-not (Test-Path $dir)) { continue }
    $active = @($cases | Where-Object { $_.Library -eq $lib } | ForEach-Object Name)
    $orphans = @(Get-ChildItem -Path $dir -Filter *.f | ForEach-Object BaseName |
                 Where-Object { $active -notcontains $_ })
    if ($orphans.Count) { Write-Host ("lib" + $lib + " .f без теста: " + ($orphans -join ', ')) }
}

# Уникальность пар (Library, Name).
$dup = @($cases | Group-Object { "$($_.Library)/$($_.Name)" } | Where-Object { $_.Count -gt 1 })
if ($dup.Count) {
    Write-Host ("  дубликаты: " + (($dup | ForEach-Object Name) -join ', '))
    $badF++
}

# Генерация manifest-JSON (порядок = порядок в эталоне; без timestamp — воспроизводимо).
if ($OutJson) {
    $json = [ordered]@{
        schema      = 1
        source      = 'ref/tests/cernlib_test.cpp'
        description = 'Active CERNlib test matrix (lib1: 183, lib2: 214, total: 397). Generated from the reference by plans/_count_cernlib.ps1; regenerate when the reference changes.'
        counts      = [ordered]@{ lib1 = $lib1.Count; lib2 = $lib2.Count; total = $cases.Count }
        cases       = @($cases | ForEach-Object {
            [ordered]@{ library = [int]$_.Library; name = [string]$_.Name }
        })
    }
    $out = ConvertTo-Json $json -Depth 5
    $resolved = Join-Path $root $OutJson
    $dir = Split-Path $resolved -Parent
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    [System.IO.File]::WriteAllText($resolved, $out + "`n", [System.Text.UTF8Encoding]::new($false))
    Write-Host ("manifest written: " + $resolved)
}

if ($badF -gt 0 -or $badE -gt 0) { exit 1 }
exit 0
