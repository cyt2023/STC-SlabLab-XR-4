@echo off
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-Backend.ps1" -ReplaceMismatched
if errorlevel 1 pause
