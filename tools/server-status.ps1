[CmdletBinding()]
param()

$projectRoot = Split-Path -Parent $PSScriptRoot
$runtimeDir = Join-Path $projectRoot ".server"
$pidFile = Join-Path $runtimeDir "server.pid"

if (-not (Test-Path -LiteralPath $pidFile)) {
    Write-Host "Trang thai: DA DUNG"
    exit 1
}

$serverProcessId = 0
$pidText = (Get-Content -LiteralPath $pidFile -Raw).Trim()
if (-not [int]::TryParse($pidText, [ref]$serverProcessId)) {
    Write-Host "Trang thai: PID FILE KHONG HOP LE"
    exit 1
}

$serverProcess = Get-Process -Id $serverProcessId -ErrorAction SilentlyContinue
if (-not $serverProcess) {
    Write-Host "Trang thai: DA DUNG (PID file cu)"
    exit 1
}

try {
    $response = Invoke-WebRequest `
        -Uri "http://127.0.0.1:8000/api/templates" `
        -UseBasicParsing `
        -TimeoutSec 2
}
catch {
    Write-Host "Trang thai: DANG CHAY NHUNG API KHONG PHAN HOI"
    Write-Host "PID: $serverProcessId"
    exit 1
}

Write-Host "Trang thai: DANG CHAY"
Write-Host "Dia chi: http://127.0.0.1:8000"
Write-Host "PID: $serverProcessId"
Write-Host "HTTP: $($response.StatusCode)"
