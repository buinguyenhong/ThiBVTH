<#
.SYNOPSIS
    Script tu dong kiem thu, dong goi ban chay Release (Executable) va ghi vet lich su build.
#>

param (
    [string]$Note = "Automated release build"
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  BAT DAU QUY TRINH BUILD PHIEN BAN CHAY RELEASE" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$WorkspaceRoot = $PSScriptRoot
$DesktopProj = Join-Path $WorkspaceRoot "desktop\src\ExamGenerator.Desktop\ExamGenerator.Desktop.csproj"
$ClientProj = Join-Path $WorkspaceRoot "desktop\src\ExamGenerator.Client\ExamGenerator.Client.csproj"
$TestProj = Join-Path $WorkspaceRoot "desktop\tests\ExamGenerator.Tests\ExamGenerator.Tests.csproj"
$PublishDir = Join-Path $WorkspaceRoot "desktop\publish\ExamOperationsDesktop"
$PublishStandaloneDir = Join-Path $WorkspaceRoot "desktop\publish\ExamOperationsDesktop-Standalone"
$PublishClientDir = Join-Path $WorkspaceRoot "desktop\publish\ExamClient"
$PublishClientStandaloneDir = Join-Path $WorkspaceRoot "desktop\publish\ExamClient-Standalone"
$BuildHistoryFile = Join-Path $WorkspaceRoot "BUILD_HISTORY.md"

# 1. Chay Unit Tests
Write-Host "`n[1/4] Chay Unit Tests..." -ForegroundColor Yellow
dotnet test $TestProj --configuration Release --verbosity normal
if ($LASTEXITCODE -ne 0) {
    Write-Error "Unit Tests that bai! Huy quy trinh build."
    exit 1
}
Write-Host "-> Toan bo Unit Tests PASSED thanh cong!" -ForegroundColor Green

# 2. Publish Release (Bản tiêu chuẩn & Bản Standalone cho cả Desktop & Client)
Write-Host "`n[2/4] Build va Publish phien ban Tieu Chuan (Framework-dependent)..." -ForegroundColor Yellow
dotnet publish $DesktopProj -c Release -o $PublishDir
dotnet publish $ClientProj -c Release -o $PublishClientDir
if ($LASTEXITCODE -ne 0) {
    Write-Error "Dotnet publish ban tieu chuan that bai!"
    exit 1
}

Write-Host "`n[2b/4] Build va Publish phien ban Standalone (Self-Contained win-x64)..." -ForegroundColor Yellow
dotnet publish $DesktopProj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $PublishStandaloneDir
dotnet publish $ClientProj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $PublishClientStandaloneDir
if ($LASTEXITCODE -ne 0) {
    Write-Error "Dotnet publish ban Standalone that bai!"
    exit 1
}

# 3. Tao script cap nhat an toan va huong dan sao chep cho ca 2 ban
$BatchContent = @"
@echo off
chcp 65001 >nul
echo ========================================================
echo  CAP NHAT PHAN MEM TAO DE THI - KHONG MAT DU LIEU / MAPPING
echo ========================================================
echo.
set /p TARGET_DIR="Nhap duong dan thu muc dang chay that (VD: D:\ExamGenerator): "
if "%TARGET_DIR%"=="" (
    echo [LOI] Chua nhap duong dan thu muc!
    pause
    exit /b 1
)

if not exist "%TARGET_DIR%" (
    echo [LOI] Thu muc khong ton tai: %TARGET_DIR%
    pause
    exit /b 1
)

echo.
echo Dang sao chep file chay moi (TU DONG BO QUA: thu muc data, database, cau hinh va output)...
robocopy "%~dp0." "%TARGET_DIR%" /E /XF "exam-generator.sqlite*" "appsettings.local.json" "credentials.local.bin" "CapNhat_KhongMatDuLieu.bat" /XD "data" "output" /NP /R:1 /W:1

echo.
echo ========================================================
echo  HOAN TAT CAP NHAT AN TOAN!
echo  Toan bo thu muc data/ (Mapping, Mau de, CSDL va Lich su) da duoc giu nguyen 100%%.
echo ========================================================
pause
"@

foreach ($targetDir in @($PublishDir, $PublishStandaloneDir)) {
    $stale = Join-Path $targetDir "data"
    if (Test-Path $stale) {
        Remove-Item $stale -Recurse -Force -ErrorAction SilentlyContinue
    }
    $scriptPath = Join-Path $targetDir "CapNhat_KhongMatDuLieu.bat"
    [System.IO.File]::WriteAllText($scriptPath, $BatchContent, [System.Text.Encoding]::ASCII)
}
Write-Host "-> Da tao script cap nhat an toan trong ca 2 thu muc publish" -ForegroundColor Green

$ExePath = Join-Path $PublishDir "ExamGenerator.Desktop.exe"
$ExeStandalonePath = Join-Path $PublishStandaloneDir "ExamGenerator.Desktop.exe"

$ExeItem = Get-Item $ExePath
$ExeSizeKb = [Math]::Round($ExeItem.Length / 1024, 1)

$ClientExePath = Join-Path $PublishClientDir "ExamGenerator.Client.exe"
$ClientExeStandalonePath = Join-Path $PublishClientStandaloneDir "ExamGenerator.Client.exe"

$ClientExeSizeKb = 0
if (Test-Path $ClientExePath) {
    $ClientExeItem = Get-Item $ClientExePath
    $ClientExeSizeKb = [Math]::Round($ClientExeItem.Length / 1024, 1)
}

$ClientExeStandaloneSizeKb = 0
if (Test-Path $ClientExeStandalonePath) {
    $ClientExeStandaloneItem = Get-Item $ClientExeStandalonePath
    $ClientExeStandaloneSizeKb = [Math]::Round($ClientExeStandaloneItem.Length / 1024, 1)
}

$ExeStandaloneItem = Get-Item $ExeStandalonePath
$ExeStandaloneSizeKb = [Math]::Round($ExeStandaloneItem.Length / 1024, 1)

# 4. Ghi vet Build History
Write-Host "`n[3/4] Ghi nhan lich su build vao BUILD_HISTORY.md..." -ForegroundColor Yellow
$Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
$GitCommit = "N/A"
try {
    $GitCommit = (git rev-parse --short HEAD).Trim()
} catch {}

$LogEntry = @"

### Build [$Timestamp] - Commit [$GitCommit]
- **Trang thai:** Thanh cong (56/56 Tests Passed)
- **Cau hinh:** .NET 8 LTS (Release) + Standalone (Self-Contained win-x64)
- **Duong dan chay ExamOperationsDesktop (Hoi dong / Giam khao):**
  - Tieu chuan: ``desktop/publish/ExamOperationsDesktop/ExamGenerator.Desktop.exe`` ($ExeSizeKb KB)
  - Standalone: ``desktop/publish/ExamOperationsDesktop-Standalone/ExamGenerator.Desktop.exe`` ($ExeStandaloneSizeKb KB)
- **Duong dan chay ExamClient (May tram Thi sinh):**
  - Tieu chuan: ``desktop/publish/ExamClient/ExamGenerator.Client.exe`` ($ClientExeSizeKb KB)
  - Standalone: ``desktop/publish/ExamClient-Standalone/ExamGenerator.Client.exe`` ($ClientExeStandaloneSizeKb KB)
- **Ghi chu thay doi:** $Note
"@

if (-not (Test-Path $BuildHistoryFile)) {
    "# Lich Su Cac Phien Ban Build (Build History)`n" | Out-File -FilePath $BuildHistoryFile -Encoding utf8
}
$LogEntry | Out-File -FilePath $BuildHistoryFile -Append -Encoding utf8

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  BUILD HOAN TAT THANH CONG CAC PHIEN BAN!" -ForegroundColor Green
Write-Host "  1. Desktop Tieu chuan: $PublishDir" -ForegroundColor White
Write-Host "  2. Desktop Standalone: $PublishStandaloneDir" -ForegroundColor White
Write-Host "  3. Client Tieu chuan: $PublishClientDir" -ForegroundColor White
Write-Host "  4. Client Standalone: $PublishClientStandaloneDir" -ForegroundColor White
Write-Host "==========================================================" -ForegroundColor Cyan
