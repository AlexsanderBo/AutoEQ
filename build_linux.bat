@echo off
setlocal

set "BASE=%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%BASE%scripts\windows\publish-linux.ps1"
@if errorlevel 1 exit /b 1

endlocal
