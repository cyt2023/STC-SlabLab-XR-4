$ErrorActionPreference = 'Stop'

# Always verify that ports 8010/8020 belong to this checkout before launching
# Quest. This prevents a stale backend from another copy of STC serving the
# wrong manifest or generated plots.
& (Join-Path $PSScriptRoot 'Start-Backend.ps1') -ReplaceMismatched

$adbCandidates = @(
    'D:\untiy\2022.3.62f3\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe',
    (Join-Path $env:LOCALAPPDATA 'Android\Sdk\platform-tools\adb.exe')
)
$adb = $adbCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($adb)) {
    throw 'ADB was not found. The PC backend is running, but Quest forwarding could not be configured.'
}

& $adb start-server | Out-Null
$devices = @(& $adb devices | Select-String "\sdevice$")
if ($devices.Count -eq 0) {
    throw 'No authorized Quest/Android device is connected. The PC backend is running.'
}

& $adb reverse tcp:8010 tcp:8010 | Out-Null
& $adb reverse tcp:8020 tcp:8020 | Out-Null
& $adb shell am force-stop com.volumestcube.quest | Out-Null
& $adb shell monkey -p com.volumestcube.quest -c android.intent.category.LAUNCHER 1 | Out-Null

Write-Host 'Quest demo started.' -ForegroundColor Green
Write-Host 'ADB reverse: Quest 8010/8020 -> current PC workspace'
Write-Host 'App:         com.volumestcube.quest'
