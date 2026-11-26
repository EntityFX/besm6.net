param(
    [string]$Apk = "src/Besm6.Android/bin/Debug/net10.0-android36.0/ru.besm6.android-Signed.apk",
    [string]$Serial = "emulator-5554",
    [int]$TimeoutSec = 90
)

$ErrorActionPreference = "Stop"
$adb = Join-Path $env:LOCALAPPDATA "Android/Sdk/platform-tools/adb.exe"
if (-not (Test-Path $adb)) { throw "adb not found: $adb" }
if (-not (Test-Path $Apk)) { throw "APK not found: $Apk" }

function Invoke-Adb([string[]]$Arguments) {
    $output = & $adb -s $Serial @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "adb $($Arguments -join ' ') failed: $output" }
    return $output
}

function Get-UiXml {
    $remote = "/sdcard/besm6-ui.xml"
    $local = Join-Path $env:TEMP "besm6-ui.xml"
    for ($attempt = 0; $attempt -lt 8; $attempt++) {
        Remove-Item $local -ErrorAction SilentlyContinue
        & $adb -s $Serial shell uiautomator dump $remote 2>$null | Out-Null
        Start-Sleep -Milliseconds 400
        & $adb -s $Serial pull $remote $local 2>$null | Out-Null
        try {
            [xml]$xml = Get-Content $local -Raw -Encoding UTF8
            return $xml
        } catch {
            Start-Sleep -Milliseconds 800
        }
    }
    throw "Cannot read UI hierarchy"
}

function Find-Node([xml]$Xml, [string]$Text) {
    return $Xml.SelectSingleNode("//node[@text='$Text']")
}

function Tap-Node([xml]$Xml, [string]$Text) {
    $node = Find-Node $Xml $Text
    if ($null -eq $node) { throw "UI node not found: $Text" }
    $bounds = $node.bounds
    if ($bounds -notmatch '^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$') {
        throw "Bad bounds for ${Text}: $bounds"
    }
    $x = [int]((([int]$matches[1]) + ([int]$matches[3])) / 2)
    $y = [int]((([int]$matches[2]) + ([int]$matches[4])) / 2)
    Invoke-Adb @("shell", "input", "tap", "$x", "$y") | Out-Null
}

$devices = (& $adb devices) -join "`n"
if ($devices -notmatch "$Serial\s+device") {
    throw "Android device $Serial is not online. Start the emulator first."
}

Write-Host "Installing $Apk"
Invoke-Adb @("install", "-r", $Apk) | Out-Null
Invoke-Adb @("shell", "am", "force-stop", "ru.besm6.android") | Out-Null
$activity = (Invoke-Adb @("shell", "cmd", "package", "resolve-activity", "--brief", "ru.besm6.android") |
    Select-Object -Last 1).Trim()
Invoke-Adb @("shell", "am", "start", "-n", $activity) | Out-Null
Start-Sleep -Seconds 12

$xml = Get-UiXml
foreach ($tab in @("Код", "Поиск", "Регистры", "Терминал", "Печать")) {
    if ($null -eq (Find-Node $xml $tab)) { throw "Missing tab: $tab" }
}

Tap-Node $xml "Код"
Start-Sleep -Milliseconds 300
$xml = Get-UiXml
Tap-Node $xml "Пуск"
Write-Host "Waiting for completion..."

$deadline = (Get-Date).AddSeconds($TimeoutSec)
$completed = $false
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 2
    $xml = Get-UiXml
    $texts = @($xml.SelectNodes("//node/@text") | ForEach-Object { $_.Value })
    if ($texts -match "Ошибка:") { throw "Machine run failed: $($texts | Where-Object { $_ -like 'Ошибка:*' } | Select-Object -First 1)" }
    if ($texts -match "Завершено\.") { $completed = $true; break }
}
if (-not $completed) { throw "Run did not complete within $TimeoutSec s" }

Tap-Node $xml "Печать"
Start-Sleep -Seconds 1
$xml = Get-UiXml
$printer = @($xml.SelectNodes("//node[@text]") | ForEach-Object { $_.text }) -join "`n"
if ($printer -notmatch "HELLO,\s*WORLD") {
    throw "Printer output does not contain HELLO, WORLD!"
}

Write-Host "Android emulator smoke test passed."
