[CmdletBinding()]
param(
    [string]$Dll = 'src/Besm6.Cli/bin/Release/net8.0/besm6.dll',
    [string]$Deck = 'examples/whetstone-longbottom.dub',
    [double]$ControlTolerance = 0.000001,
    [switch]$ForexAsFortran,
    [string]$OutputDirectory = ('tests-run/whetstone-longbottom-' + (Get-Date -Format 'yyyyMMdd-HHmmss')),
    [ValidateSet('original','virtual','max')][string[]]$Modes = @('original','virtual'),
    [switch]$CheckGuards
)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $repositoryRoot
try {
    if (Test-Path -LiteralPath $OutputDirectory) { throw 'Choose a fresh output directory.' }
    New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
    $culture = [Globalization.CultureInfo]::InvariantCulture
    # Independent binary64 evaluation of the upstream kernels, IX100=100,
    # IXTRA=8. Guest REAL has a 40-bit mantissa; accumulated tolerance 1E-6.
    $reference = @(-1.1333333305905169,-1.1333332861052487,1,12,0.50000005785011914,0.99999980904064234,3,0.75792040197524879)
    $work = @(153600,1075200,828000,2520000,665600,4315200,1478400,297600)
    $config = Join-Path $OutputDirectory 'config.json'
    @{tapes=(Join-Path $repositoryRoot 'tapes');useWallClock=$false;forexAsFortran=[bool]$ForexAsFortran} | ConvertTo-Json | Set-Content -LiteralPath $config -Encoding utf8
    $source = Get-Content -LiteralPath $Deck -Raw
    $results = @()
    $modelProgram = $null
    foreach ($mode in $Modes) {
        Write-Host "Running Longbottom Whetstone $mode"
        $lines = & dotnet $Dll run $Deck --config $config --speed $mode --guest-clock --stats --no-wall-clock --no-hang-detect --limit 2000000000 2>&1
        $code = $LASTEXITCODE
        $output = ($lines | ForEach-Object { $_.ToString() }) -join "`n"
        Set-Content -LiteralPath (Join-Path $OutputDirectory "$mode.out") -Value $output -Encoding utf8
        if ($code -ne 0 -or $output -notmatch 'Halted by STOP' -or $output -notmatch 'ALL 8 CONTROL VALUES MATCH: YES' -or $output -notmatch 'BAD CONTROL VALUES=\s*0\b') { throw "$mode failed termination/control checks" }
        $sections = @()
        for ($index=0; $index -lt 8; $index++) {
            $match = [regex]::Match($output, '(?m)^N' + ($index+1) + '\s+.+?\s+(-?\d+\.\d+)\s+(\d+\.\d+)\s+(\d+\.\d+)\s*$')
            if (-not $match.Success) { throw "Missing section $($index+1)" }
            $value = [double]::Parse($match.Groups[1].Value,$culture)
            $rate = [double]::Parse($match.Groups[2].Value,$culture)
            $seconds = [double]::Parse($match.Groups[3].Value,$culture)
            if ([Math]::Abs($value-$reference[$index]) -gt $ControlTolerance) { throw "Independent reference failed: N$($index+1)" }
            $rawSeconds = $seconds * $(if ($index -eq 0) {10} else {1})
            if ($rawSeconds -lt 0.04) { throw 'Insufficient timer interval' }
            $expected = $work[$index]/(1000000.0*$seconds)
            if ([Math]::Abs($rate-$expected) -gt 0.000002) { throw 'Section operation count/rate failed' }
            $sections += [pscustomobject]@{section=($index+1);value=$value;rate=$rate;seconds=$seconds;unit=$(if ($index -in @(0,1,5)) {'MFLOPS'} else {'MOPS'})}
        }
        $sum = ($sections | Measure-Object seconds -Sum).Sum
        $mwips = [double]::Parse([regex]::Match($output,'(?m)^GUEST MWIPS:\s*(\S+)').Groups[1].Value,$culture)
        $gm = [double]::Parse([regex]::Match($output,'(?m)^GUEST MFLOPS GEOMEAN N1 N2 N6:\s*(\S+)').Groups[1].Value,$culture)
        $expectedGm = [Math]::Pow($sections[0].rate*$sections[1].rate*$sections[5].rate,1.0/3)
        if ([Math]::Abs($mwips-80.0/$sum) -gt 0.000002 -or [Math]::Abs($gm-$expectedGm) -gt 0.000002) { throw 'Aggregate formula failed' }
        $program = ($output -split '≠')[-1].Split('Halted by STOP')[0].Trim()
        if ($mode -ne 'virtual') {
            if ($null -ne $modelProgram -and $modelProgram -cne $program) { throw 'Original/max guest output differs' }
            $modelProgram = $program
        }
        $stats = [regex]::Match($output,'(?m)^Execution stats: (.+)$').Groups[1].Value | ConvertFrom-Json
        $results += [pscustomobject]@{mode=$mode;validated=$true;deck=$Deck;forexAsFortran=[bool]$ForexAsFortran;mwips=$mwips;mflopsGeomean=$gm;normalizedSeconds=$sum;sections=$sections;stats=$stats}
        $results | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'results.json') -Encoding utf8
        Write-Host "$mode passed: $mwips MWIPS, $gm MFLOPS"
    }
    if ($CheckGuards) {
        foreach ($scenario in @('clock-off','bad-control')) {
            $deck = Join-Path $OutputDirectory "$scenario.dub"
            $text = $source
            if ($scenario -eq 'bad-control') { $text=$text.Replace('DATA REFVAL / -1.13333333059052','DATA REFVAL / 0.') }
            Set-Content -LiteralPath $deck -Value $text -Encoding utf8
            $clockArgs = @()
            if ($scenario -eq 'bad-control') { $clockArgs=@('--guest-clock') }
            $lines = & dotnet $Dll run $deck --config $config --speed max --no-wall-clock --no-hang-detect --limit 2000000000 @clockArgs 2>&1
            $code = $LASTEXITCODE
            $output = ($lines | ForEach-Object { $_.ToString() }) -join "`n"
            Set-Content -LiteralPath (Join-Path $OutputDirectory "$scenario.out") -Value $output -Encoding utf8
            $reason = if ($scenario -eq 'bad-control') {'CONTROL VALUES DO NOT MATCH'} else {'ENABLE --GUEST-CLOCK OR RUN LONGER'}
            if ($code -ne 0 -or $output -notmatch 'Halted by STOP' -or $output -match '(?m)^GUEST (MWIPS|MFLOPS)' -or -not $output.Contains('RATINGS UNAVAILABLE: ' + $reason)) { throw "$scenario guard failed" }
            Write-Host "$scenario guard passed"
        }
    }
    $results | Select-Object mode,validated,mwips,mflopsGeomean,normalizedSeconds | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'results.csv') -NoTypeInformation -Encoding utf8
}
finally { Pop-Location }
