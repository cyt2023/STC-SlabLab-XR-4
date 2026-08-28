param(
    [int[]]$Ports = @(8010, 8020)
)

$ErrorActionPreference = 'Stop'
$stopped = @()
foreach ($port in $Ports) {
    $listeners = @(Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue)
    foreach ($listener in $listeners) {
        $process = Get-CimInstance Win32_Process -Filter "ProcessId=$($listener.OwningProcess)"
        $known = $null -ne $process -and (
            $process.CommandLine -match 'api_server\.py' -or
            $process.CommandLine -match 'Services\.S4DAnalysisService\.app:app'
        )
        if (-not $known) {
            throw "Refusing to stop unrelated process PID $($listener.OwningProcess) on port $port."
        }
        Stop-Process -Id $listener.OwningProcess -Force
        $stopped += "PID $($listener.OwningProcess) on port $port"
    }
}
if ($stopped.Count -eq 0) {
    Write-Host 'Backend is already stopped.'
} else {
    Write-Host ('Stopped: ' + ($stopped -join '; ')) -ForegroundColor Yellow
}
