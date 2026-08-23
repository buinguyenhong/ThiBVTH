[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [int]$ExpectedProcessId
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$runtimeDir = Join-Path $projectRoot ".server"
$pidFile = Join-Path $runtimeDir "server.pid"
$startScript = Join-Path $PSScriptRoot "start-server.ps1"

$listeners = @(
    Get-NetTCPConnection `
        -LocalPort 8000 `
        -State Listen `
        -ErrorAction SilentlyContinue
)

if ($listeners.Count -gt 0) {
    $ownerIds = @(
        $listeners |
            Select-Object -ExpandProperty OwningProcess -Unique
    )
    if (
        $ownerIds.Count -ne 1 -or
        $ownerIds[0] -ne $ExpectedProcessId
    ) {
        throw (
            "Cong 8000 khong con thuoc PID du kien " +
            "$ExpectedProcessId. Khong dung tien trinh."
        )
    }

    $openApi = Invoke-RestMethod `
        -Uri "http://127.0.0.1:8000/openapi.json" `
        -TimeoutSec 3
    if (
        $openApi.info.title -ne
        "Exam Generator & HIS Data Populator API"
    ) {
        throw "PID $ExpectedProcessId khong phai ExamGenerator."
    }

    Stop-Process -Id $ExpectedProcessId
    try {
        Wait-Process `
            -Id $ExpectedProcessId `
            -Timeout 10 `
            -ErrorAction Stop
    }
    catch {
        Stop-Process `
            -Id $ExpectedProcessId `
            -Force `
            -ErrorAction Stop
    }
}

Remove-Item `
    -LiteralPath $pidFile `
    -Force `
    -ErrorAction SilentlyContinue

& $startScript
if ($LASTEXITCODE -ne 0) {
    throw "Khong khoi dong lai duoc ExamGenerator."
}

