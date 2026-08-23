[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$runtimeDir = Join-Path $projectRoot ".server"
$pidFile = Join-Path $runtimeDir "server.pid"
$stdoutLog = Join-Path $runtimeDir "server.stdout.log"
$stderrLog = Join-Path $runtimeDir "server.stderr.log"
$healthUrl = "http://127.0.0.1:8000/api/templates"

function Get-CommandLine {
    param([int]$ProcessId)

    try {
        return (Get-CimInstance Win32_Process -Filter "ProcessId = $ProcessId").CommandLine
    }
    catch {
        return ""
    }
}

function Test-IsExamGenerator {
    param([int]$ProcessId)

    $commandLine = Get-CommandLine -ProcessId $ProcessId
    return $commandLine -match "backend[\\/]app\.py"
}

function Test-ServerHealth {
    try {
        $response = Invoke-WebRequest `
            -Uri $healthUrl `
            -UseBasicParsing `
            -TimeoutSec 2
        return $response.StatusCode -eq 200
    }
    catch {
        return $false
    }
}

New-Item -ItemType Directory -Path $runtimeDir -Force | Out-Null

if (Test-Path -LiteralPath $pidFile) {
    $serverProcessId = 0
    $pidText = (Get-Content -LiteralPath $pidFile -Raw).Trim()

    if ([int]::TryParse($pidText, [ref]$serverProcessId)) {
        $trackedProcess = Get-Process -Id $serverProcessId -ErrorAction SilentlyContinue
        if ($trackedProcess -and (Test-IsExamGenerator -ProcessId $serverProcessId)) {
            if (Test-ServerHealth) {
                Write-Host "Server dang chay."
                Write-Host "Dia chi: http://127.0.0.1:8000"
                Write-Host "PID: $serverProcessId"
                exit 0
            }

            Write-Error "Tien trinh server PID $serverProcessId ton tai nhung API khong phan hoi. Xem log tai $runtimeDir"
            exit 1
        }
    }

    Remove-Item -LiteralPath $pidFile -Force
}

$listeners = @(
    Get-NetTCPConnection -LocalPort 8000 -State Listen -ErrorAction SilentlyContinue
)
if ($listeners.Count -gt 0) {
    $ownerIds = ($listeners | Select-Object -ExpandProperty OwningProcess -Unique) -join ", "
    Write-Error "Khong the khoi dong: cong 8000 dang duoc su dung boi PID $ownerIds."
    exit 1
}

$pythonCommand = Get-Command python -ErrorAction SilentlyContinue
if (-not $pythonCommand) {
    Write-Error "Khong tim thay Python trong PATH."
    exit 1
}

$serverProcess = Start-Process `
    -FilePath $pythonCommand.Source `
    -ArgumentList @("-u", "backend\app.py") `
    -WorkingDirectory $projectRoot `
    -WindowStyle Hidden `
    -RedirectStandardOutput $stdoutLog `
    -RedirectStandardError $stderrLog `
    -PassThru

Set-Content -LiteralPath $pidFile -Value $serverProcess.Id -Encoding ascii

$deadline = (Get-Date).AddSeconds(20)
do {
    if ($serverProcess.HasExited) {
        Remove-Item -LiteralPath $pidFile -Force -ErrorAction SilentlyContinue
        Write-Error "Server dung dot ngot khi khoi dong. Xem log tai $stderrLog"
        exit 1
    }

    if (Test-ServerHealth) {
        Write-Host "Khoi dong server thanh cong."
        Write-Host "Dia chi: http://127.0.0.1:8000"
        Write-Host "PID: $($serverProcess.Id)"
        Write-Host "Log: $runtimeDir"
        exit 0
    }

    Start-Sleep -Milliseconds 500
} while ((Get-Date) -lt $deadline)

Stop-Process -Id $serverProcess.Id -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $pidFile -Force -ErrorAction SilentlyContinue
Write-Error "Server khong san sang sau 20 giay. Xem log tai $stderrLog"
exit 1
