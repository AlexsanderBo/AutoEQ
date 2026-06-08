@echo off
setlocal

set "BASE=%~dp0"
set "DIST=%BASE%dist\AutoEQ"
set "SHORTCUT=%USERPROFILE%\Desktop\AutoEQ.lnk"

echo Closing running AutoEQ instances so the portable build can be replaced...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-Process AutoEQ -ErrorAction SilentlyContinue | Stop-Process -Force"

echo Publishing Windows portable app...
powershell -NoProfile -ExecutionPolicy Bypass -File "%BASE%scripts\windows\publish-windows.ps1" -OutputDir "%DIST%"
@if errorlevel 1 exit /b 1

echo Creating desktop shortcut...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$s=(New-Object -ComObject WScript.Shell).CreateShortcut('%SHORTCUT%'); $s.TargetPath='%DIST%\AutoEQ.exe'; $s.WorkingDirectory='%DIST%'; $s.IconLocation='%DIST%\AutoEQ.exe'; $s.Save()"
@if errorlevel 1 exit /b 1

echo Registering Windows startup...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$exe=Join-Path '%DIST%' 'AutoEQ.exe'; $cmd=[char]34 + $exe + [char]34 + ' --background'; New-Item -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Force | Out-Null; Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'AutoEQ' -Value $cmd"
@if errorlevel 1 exit /b 1

echo.
echo Portable app folder:
echo %DIST%
echo.
echo Executable:
echo %DIST%\AutoEQ.exe
echo.
echo Desktop shortcut:
echo %SHORTCUT%

endlocal
