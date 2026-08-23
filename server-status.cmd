@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\server-status.ps1"
if errorlevel 1 pause
