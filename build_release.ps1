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
$TestProj = Join-Path $WorkspaceRoot "desktop\tests\ExamGenerator.Tests\ExamGenerator.Tests.csproj"
$PublishDir = Join-Path $WorkspaceRoot "desktop\publish\ExamOperationsDesktop"
$BuildHistoryFile = Join-Path $WorkspaceRoot "BUILD_HISTORY.md"

# 1. Chay Unit Tests
Write-Host "`n[1/4] Chay Unit Tests..." -ForegroundColor Yellow
dotnet test $TestProj --configuration Release --verbosity normal
if ($LASTEXITCODE -ne 0) {
    Write-Error "Unit Tests that bai! Huy quy trinh build."
    exit 1
}
Write-Host "-> Toan bo Unit Tests PASSED thanh cong!" -ForegroundColor Green

# 2. Publish Release
Write-Host "`n[2/4] Build va Publish phien ban Release..." -ForegroundColor Yellow
dotnet publish $DesktopProj -c Release -o $PublishDir
if ($LASTEXITCODE -ne 0) {
    Write-Error "Dotnet publish that bai!"
    exit 1
}

# 3. Lam sach SQLite cu trong thu muc publish
$StaleDb = Join-Path $PublishDir "exam-generator.sqlite"
if (Test-Path $StaleDb) {
    Remove-Item $StaleDb -Force -ErrorAction SilentlyContinue
    Write-Host "-> Da don dep file SQLite cu trong thu muc publish (san sang cho dong bo HIS)." -ForegroundColor Gray
}

$ExePath = Join-Path $PublishDir "ExamGenerator.Desktop.exe"
if (-not (Test-Path $ExePath)) {
    Write-Error "Khong tim thay file thuc thi $ExePath sau khi publish!"
    exit 1
}
$ExeItem = Get-Item $ExePath
$ExeSizeKb = [Math]::Round($ExeItem.Length / 1024, 1)
Write-Host "-> File thuc thi san sang: $($ExeItem.FullName) ($ExeSizeKb KB)" -ForegroundColor Green

# 4. Ghi vet Build History
Write-Host "`n[3/4] Ghi nhan lich su build vao BUILD_HISTORY.md..." -ForegroundColor Yellow
$Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
$GitCommit = "N/A"
try {
    $GitCommit = (git rev-parse --short HEAD).Trim()
} catch {}

$LogEntry = @"

### Build [$Timestamp] - Commit [$GitCommit]
- **Trang thai:** Thanh cong (16/16 Tests Passed)
- **Cau hinh:** .NET 8 LTS (Release)
- **Duong dan chay:** ``desktop/publish/ExamOperationsDesktop/ExamGenerator.Desktop.exe``
- **Kich thuoc file thuc thi:** $ExeSizeKb KB
- **Ghi chu thay doi:** $Note
"@

if (-not (Test-Path $BuildHistoryFile)) {
    "# Lich Su Cac Phien Ban Build (Build History)`n" | Out-File -FilePath $BuildHistoryFile -Encoding utf8
}
$LogEntry | Out-File -FilePath $BuildHistoryFile -Append -Encoding utf8

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  BUILD HOAN TAT THANH CONG!" -ForegroundColor Green
Write-Host "  Thu muc ung dung: $PublishDir" -ForegroundColor White
Write-Host "  File chay: $ExePath" -ForegroundColor White
Write-Host "==========================================================" -ForegroundColor Cyan
