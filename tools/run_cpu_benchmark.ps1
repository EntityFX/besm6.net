[CmdletBinding()]
param(
    [switch]$Quick,
    [switch]$SkipBuild,
    [string]$OutputDirectory = ("tests-run/cpu-target-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
)

# Run on the target computer from a repository checkout. Requires .NET SDK 8+
# and Python 3. Reports omit account, computer and user-profile names.
$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $repositoryRoot
try {
    Get-Command dotnet -ErrorAction Stop | Out-Null
    Get-Command python -ErrorAction Stop | Out-Null
    if (Test-Path -LiteralPath $OutputDirectory) {
        throw "Choose a fresh output directory; previous measurements are retained."
    }
    if (-not $SkipBuild) {
        & dotnet build src/Besm6.Cli/Besm6.Cli.csproj -c Release --verbosity minimal
        if ($LASTEXITCODE -ne 0) { throw "Release build failed." }
    }
    $cliDll = "src/Besm6.Cli/bin/Release/net8.0/besm6.dll"
    if (-not (Test-Path -LiteralPath $cliDll)) { throw "Release CLI is missing." }
    $jobs = if ($Quick) { @("dh10000", "mp3") } else {
        @("dh0", "dh5000", "dh10000", "mp0", "mp1", "mp2", "mp3")
    }
    & python tools/benchmark_speed.py --dll $cliDll --jobs @jobs `
        --modes max original --warmups 1 --repeats 5 --timeout 600 --output $OutputDirectory
    $benchmarkExit = $LASTEXITCODE
    $resultFile = Join-Path $OutputDirectory "results.json"
    if (-not (Test-Path -LiteralPath $resultFile)) { throw "No benchmark result was produced." }
    $report = Get-Content -LiteralPath $resultFile -Raw | ConvertFrom-Json
    if ($report.PSObject.Properties.Name -contains "error") {
        throw "Benchmark validation failed. See the local result and logs."
    }
    if ($benchmarkExit -notin @(0, 1)) { throw "Benchmark process failed with exit code $benchmarkExit." }
    $report.measurements | Select-Object job, variant, loop_seconds, model_seconds, original_tempo_met |
        Format-Table -AutoSize
    $late = @($report.measurements | Where-Object {
        $_.variant -eq "original" -and $_.original_tempo_met -eq $false
    })
    if ($late.Count -eq 0) { Write-Host "Original model tempo met on all selected workloads." }
    else { Write-Host "Host falls behind the original model tempo on $($late.Count) selected workloads." }
    Write-Host "Keep results.json and results.csv. Raw logs remain local."
}
finally { Pop-Location }
