# Chay Phan mem Quan tri va Cham thi (ExamDesktop)
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$appDir = Join-Path $scriptDir "desktop\publish\ExamOperationsDesktop"
$appExe = Join-Path $appDir "ExamGenerator.Desktop.exe"

Start-Process -FilePath $appExe -WorkingDirectory $appDir
