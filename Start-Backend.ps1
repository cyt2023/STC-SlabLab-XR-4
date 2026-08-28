param(
    [string]$BindAddress = '127.0.0.1',
    [int]$MatPlotPort = 8010,
    [int]$S4DPort = 8020,
    [switch]$ReplaceMismatched
)

$ErrorActionPreference = 'Stop'

$workspaceRoot = $PSScriptRoot
$workspacePython = Join-Path $workspaceRoot '.venv\Scripts\python.exe'
$python = if (Test-Path -LiteralPath $workspacePython) {
    $workspacePython
} elseif (Test-Path -LiteralPath 'D:\python\python.exe') {
    'D:\python\python.exe'
} else {
    throw 'Python was not found. Create .venv or install Python at D:\python\python.exe.'
}

$matPlotRoot = Join-Path $workspaceRoot 'Services\MatPlotAgent'
$datasetRoot = Join-Path $workspaceRoot 'datasets'
$runtimeRoot = Join-Path $workspaceRoot '.runtime\backend'
$logRoot = Join-Path $runtimeRoot 'logs'
$pidFile = Join-Path $runtimeRoot 'processes.json'
$matPlotHealthUrl = "http://${BindAddress}:$MatPlotPort/health"
$s4dHealthUrl = "http://${BindAddress}:$S4DPort/health"
$expectedMatPlotRoot = [IO.Path]::GetFullPath($matPlotRoot).TrimEnd('\')
$expectedMatPlotWorkspace = [IO.Path]::GetFullPath(
    (Join-Path $matPlotRoot 'workspace\api')
).TrimEnd('\')
$expectedWorkspaceRoot = [IO.Path]::GetFullPath($workspaceRoot).TrimEnd('\')

New-Item -ItemType Directory -Path $logRoot -Force | Out-Null

function Get-Health([string]$Url) {
    try {
        return Invoke-RestMethod -Uri $Url -TimeoutSec 2
    } catch {
        return $null
    }
}

function Test-SamePath([string]$Left, [string]$Right) {
    if ([string]::IsNullOrWhiteSpace($Left) -or [string]::IsNullOrWhiteSpace($Right)) {
        return $false
    }
    return [IO.Path]::GetFullPath($Left).TrimEnd('\') -ieq [IO.Path]::GetFullPath($Right).TrimEnd('\')
}

function Stop-KnownBackendOnPort([int]$Port, [string]$ExpectedCommandPattern) {
    $listeners = @(Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue)
    foreach ($listener in $listeners) {
        $process = Get-CimInstance Win32_Process -Filter "ProcessId=$($listener.OwningProcess)"
        if ($null -eq $process -or $process.CommandLine -notmatch $ExpectedCommandPattern) {
            throw "Port $Port is occupied by an unrelated process (PID $($listener.OwningProcess))."
        }
        Stop-Process -Id $listener.OwningProcess -Force
        Wait-Process -Id $listener.OwningProcess -Timeout 10 -ErrorAction SilentlyContinue
    }
}

function Wait-ForExpectedHealth(
    [string]$Url,
    [scriptblock]$IdentityCheck,
    [System.Diagnostics.Process]$Process,
    [string]$ErrorLog
) {
    for ($attempt = 0; $attempt -lt 120; $attempt++) {
        Start-Sleep -Milliseconds 500
        if ($Process.HasExited) {
            $details = if (Test-Path -LiteralPath $ErrorLog) {
                (Get-Content -LiteralPath $ErrorLog -Raw -ErrorAction SilentlyContinue)
            } else { '' }
            throw "Backend process exited during startup. $details"
        }
        $health = Get-Health $Url
        if ($null -ne $health -and (& $IdentityCheck $health)) {
            return $health
        }
    }
    throw "Backend did not become healthy at $Url within 60 seconds. See $ErrorLog"
}

$matPlotHealth = Get-Health $matPlotHealthUrl
$matPlotMatches = $null -ne $matPlotHealth -and (
    (Test-SamePath ([string]$matPlotHealth.workspaceRoot) $expectedMatPlotRoot) -or
    (Test-SamePath ([string]$matPlotHealth.workspace) $expectedMatPlotWorkspace)
)
if ($null -ne $matPlotHealth -and -not $matPlotMatches) {
    if (-not $ReplaceMismatched) {
        throw "Port $MatPlotPort belongs to another MatPlotAgent workspace: $($matPlotHealth.workspace). Re-run with -ReplaceMismatched."
    }
    Stop-KnownBackendOnPort $MatPlotPort 'api_server\.py'
    $matPlotHealth = $null
}

$s4dHealth = Get-Health $s4dHealthUrl
$s4dMatches = $null -ne $s4dHealth -and (
    Test-SamePath ([string]$s4dHealth.workspaceRoot) $expectedWorkspaceRoot
)
if ($null -ne $s4dHealth -and -not $s4dMatches) {
    if (-not $ReplaceMismatched) {
        throw "Port $S4DPort belongs to another or unidentified S4D workspace. Re-run with -ReplaceMismatched."
    }
    Stop-KnownBackendOnPort $S4DPort 'Services\.S4DAnalysisService\.app:app'
    $s4dHealth = $null
}

$started = @()
if ($null -eq $matPlotHealth) {
    $env:MATPLOT_API_HOST = $BindAddress
    $env:MATPLOT_API_PORT = [string]$MatPlotPort
    if (-not $env:MATPLOT_CODE_MODEL) { $env:MATPLOT_CODE_MODEL = 'qwen-flash' }
    if (-not $env:MATPLOT_SUMMARY_MODEL) { $env:MATPLOT_SUMMARY_MODEL = 'qwen-vl-max' }
    if (-not $env:MATPLOT_MAX_CONCURRENT) { $env:MATPLOT_MAX_CONCURRENT = '9' }
    $matOut = Join-Path $logRoot 'matplot.stdout.log'
    $matErr = Join-Path $logRoot 'matplot.stderr.log'
    $matProcess = Start-Process -FilePath $python -ArgumentList 'api_server.py' `
        -WorkingDirectory $matPlotRoot -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput $matOut -RedirectStandardError $matErr
    $started += [pscustomobject]@{ Name = 'MatPlotAgent'; Pid = $matProcess.Id }
    $matPlotHealth = Wait-ForExpectedHealth $matPlotHealthUrl {
        param($health)
        Test-SamePath ([string]$health.workspaceRoot) $expectedMatPlotRoot
    } $matProcess $matErr
}

if ($null -eq $s4dHealth) {
    $env:S4D_DATASET_ROOT = $datasetRoot
    $env:S4D_MATPLOT_URL = "http://${BindAddress}:$MatPlotPort"
    if (-not $env:S4D_MATPLOT_SUBMIT_CONCURRENCY) { $env:S4D_MATPLOT_SUBMIT_CONCURRENCY = '9' }
    if (-not $env:VOICE_LOCAL_FIRST) { $env:VOICE_LOCAL_FIRST = '1' }
    if (-not $env:VOICE_LOCAL_MODEL) { $env:VOICE_LOCAL_MODEL = 'base' }
    $s4dOut = Join-Path $logRoot 's4d.stdout.log'
    $s4dErr = Join-Path $logRoot 's4d.stderr.log'
    $s4dProcess = Start-Process -FilePath $python `
        -ArgumentList '-m', 'uvicorn', 'Services.S4DAnalysisService.app:app', '--host', $BindAddress, '--port', ([string]$S4DPort) `
        -WorkingDirectory $workspaceRoot -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput $s4dOut -RedirectStandardError $s4dErr
    $started += [pscustomobject]@{ Name = 'S4DAnalysisService'; Pid = $s4dProcess.Id }
    $s4dHealth = Wait-ForExpectedHealth $s4dHealthUrl {
        param($health)
        Test-SamePath ([string]$health.workspaceRoot) $expectedWorkspaceRoot
    } $s4dProcess $s4dErr
}

$datasets = Invoke-RestMethod -Uri "http://${BindAddress}:$S4DPort/datasets" -TimeoutSec 10
$forVr = @($datasets | Where-Object { $_.datasetId -eq 'for_vr_hong_kong_events_v1' })
if (@($forVr).Count -ne 1) {
    throw 'The forvr dataset is not registered by the running S4D service.'
}
$runtimeProcesses = foreach ($port in @($MatPlotPort, $S4DPort)) {
    $listener = Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction Stop |
        Select-Object -First 1
    [pscustomobject]@{
        Name = if ($port -eq $MatPlotPort) { 'MatPlotAgent' } else { 'S4DAnalysisService' }
        Port = $port
        Pid = $listener.OwningProcess
    }
}
$runtimeProcesses | ConvertTo-Json | Set-Content -LiteralPath $pidFile -Encoding UTF8

Write-Host 'Current workspace backend is ready.' -ForegroundColor Green
Write-Host "MatPlotAgent: $matPlotHealthUrl  provider=$($matPlotHealth.provider) configured=$($matPlotHealth.providerConfigured)"
Write-Host "S4D service:   $s4dHealthUrl  datasets=$($s4dHealth.datasets)"
Write-Host "forvr data:    $($forVr[0].datasetId)  variables=$(@($forVr[0].variables).Count)"
Write-Host "Logs:          $logRoot"
