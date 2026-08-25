# Вспомогательный скрипт: активные имена тестов CERN + проверка .f/expect.
$f = 'E:\Projects\besm6.net\ref\tests\cernlib_test.cpp'
$lines = Get-Content -LiteralPath $f
$lib1 = New-Object System.Collections.Generic.List[string]
$lib2 = New-Object System.Collections.Generic.List[string]
foreach ($l in $lines) {
    if ($l -match '^\s*//') { continue }
    if ($l -match 'test_cernlib\(1,\s*"([^"]+)"') { $lib1.Add($Matches[1]) }
    elseif ($l -match 'test_cernlib\(2,\s*"([^"]+)"') { $lib2.Add($Matches[1]) }
}
function Report($names, $dir) {
    $noF = @(); $noE = @()
    foreach ($n in $names) {
        if (-not (Test-Path (Join-Path $dir ($n + '.f')))) { $noF += $n }
        if (-not (Test-Path (Join-Path $dir ('expect_' + $n + '.txt')))) { $noE += $n }
    }
    if ($noF.Count) { Write-Host ("  no .f : " + ($noF -join ', ')) }
    if ($noE.Count) { Write-Host ("  no expect: " + ($noE -join ', ')) }
}
Write-Host ("lib1 active: " + $lib1.Count)
Report $lib1 'E:\Projects\besm6.net\ref\tests\lib1'
Write-Host ("lib2 active: " + $lib2.Count)
Report $lib2 'E:\Projects\besm6.net\ref\tests\lib2'
# .f files без теста
$f1 = (Get-ChildItem 'E:\Projects\besm6.net\ref\tests\lib1' -Filter *.f).BaseName
$f2 = (Get-ChildItem 'E:\Projects\besm6.net\ref\tests\lib2' -Filter *.f).BaseName
$orphan1 = @($f1 | Where-Object { $lib1 -notcontains $_ })
$orphan2 = @($f2 | Where-Object { $lib2 -notcontains $_ })
Write-Host ("lib1 .f без теста: " + ($orphan1 -join ', '))
Write-Host ("lib2 .f без теста: " + ($orphan2 -join ', '))

