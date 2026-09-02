param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$artifactRoot = Join-Path $repoRoot "tests-run/cernlib"
$project = Join-Path $repoRoot "src/besm6.net/tests/Besm6.Tests/Besm6.Tests.csproj"

$matrixPath = Join-Path $artifactRoot "matrix.csv"
$caseName = $null
if (Test-Path -LiteralPath $matrixPath) {
    $firstFailure = Import-Csv -LiteralPath $matrixPath |
        Where-Object success -ne "true" |
        Sort-Object { [int]$_.index } |
        Select-Object -First 1
    if ($null -ne $firstFailure) {
        $caseName = $firstFailure.case
    }
}

$runInfo = $null
if (-not [string]::IsNullOrWhiteSpace($caseName)) {
    $parts = $caseName -split "/", 2
    if ($parts.Count -eq 2) {
        $candidate = Join-Path (Join-Path (Join-Path $artifactRoot $parts[0]) $parts[1]) "run.json"
        if (Test-Path -LiteralPath $candidate) {
            $runInfo = Get-Item -LiteralPath $candidate
        }
    }
}
if ($null -eq $runInfo) {
    $runInfo = Get-ChildItem -LiteralPath $artifactRoot -Filter run.json -File -Recurse |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
}
if ($null -eq $runInfo) {
    throw "No failed CERN run.json was found under $artifactRoot"
}

$caseName = (Get-Content -LiteralPath $runInfo.FullName -Raw | ConvertFrom-Json).case
if ([string]::IsNullOrWhiteSpace($caseName)) {
    throw "Missing case in $($runInfo.FullName)"
}

$env:BESM6_TRACE = "1"
$env:BESM6_TRACE_CASE = $caseName
$env:BESM6_CANON_TRACE_LIMIT = "50000"
dotnet test $project `
    --configuration $Configuration `
    --no-build `
    --filter "FullyQualifiedName~Besm6.Tests.CernLibTests.GenTrace"
if ($LASTEXITCODE -ne 0) {
    throw "Trace generation failed for $caseName (exit $LASTEXITCODE)"
}

Write-Host "Generated diagnostic traces for $caseName next to $($runInfo.FullName)"
