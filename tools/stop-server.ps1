[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$runtimeDir = Join-Path $projectRoot ".server"
$pidFile = Join-Path $runtimeDir "server.pid"
$stderrLog = Join-Path $runtimeDir "server.stderr.log"

function Test-IsTrackedExamGenerator {
    param([int]$ProcessId)

    try {
        $commandLine = (
            Get-CimInstance Win32_Process -Filter "ProcessId = $ProcessId"
        ).CommandLine
    }
    catch {
        $commandLine = ""
    }

    if ($commandLine -match "backend[\\/]app\.py") {
        return $true
    }

    # Some Windows accounts can see the process but not its command line.
    # In that case require three independent matches before allowing a stop:
    # tracked PID owns port 8000, the API identifies this project, and the
    # server's own log records the same startup PID.
    $listener = Get-NetTCPConnection `
        -LocalPort 8000 `
        -State Listen `
        -ErrorAction SilentlyContinue |
        Where-Object { $_.OwningProcess -eq $ProcessId }
    if (-not $listener) {
        return $false
    }

    try {
        $openApi = Invoke-RestMethod `
            -Uri "http://127.0.0.1:8000/openapi.json" `
            -TimeoutSec 2
        if (
            $openApi.info.title -ne
            "Exam Generator & HIS Data Populator API"
        ) {
            return $false
        }
    }
    catch {
        return $false
    }

    if (-not (Test-Path -LiteralPath $stderrLog)) {
        return $false
    }
    $startupMarker = "Started server process [$ProcessId]"
    return (Get-Content -LiteralPath $stderrLog -Raw) -match
        [regex]::Escape($startupMarker)
}

if (-not (Test-Path -LiteralPath $pidFile)) {
    Write-Host "Server khong chay (khong co PID file)."
    exit 0
}

$serverProcessId = 0
$pidText = (Get-Content -LiteralPath $pidFile -Raw).Trim()
if (-not [int]::TryParse($pidText, [ref]$serverProcessId)) {
    Remove-Item -LiteralPath $pidFile -Force
    Write-Host "PID file khong hop le va da duoc xoa."
    exit 0
}

$serverProcess = Get-Process -Id $serverProcessId -ErrorAction SilentlyContinue
if (-not $serverProcess) {
    Remove-Item -LiteralPath $pidFile -Force
    Write-Host "Server da dung; PID file cu da duoc xoa."
    exit 0
}

if (-not (Test-IsTrackedExamGenerator -ProcessId $serverProcessId)) {
    Write-Error "PID $serverProcessId khong phai ExamGenerator. Khong dung tien trinh nay."
    exit 1
}

Stop-Process -Id $serverProcessId
try {
    Wait-Process -Id $serverProcessId -Timeout 10 -ErrorAction Stop
}
catch {
    Stop-Process -Id $serverProcessId -Force -ErrorAction SilentlyContinue
}

Remove-Item -LiteralPath $pidFile -Force -ErrorAction SilentlyContinue
Write-Host "Da dung server (PID $serverProcessId)."
