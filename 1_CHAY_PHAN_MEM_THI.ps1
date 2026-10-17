# Chay Phan mem Thi Thi sinh (ExamClient)
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$appDir = Join-Path $scriptDir "desktop\publish\ExamClient"
$appExe = Join-Path $appDir "ExamGenerator.Client.exe"

Start-Process -FilePath $appExe -WorkingDirectory $appDir
