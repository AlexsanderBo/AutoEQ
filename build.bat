@echo off
setlocal

set "BASE=F:\autoEQ"
set "PROJECT=%BASE%\WoburnAutoEQ\WoburnAutoEQ.csproj"
set "PUBLISH=%BASE%\WoburnAutoEQ\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish"
set "DIST=%BASE%\dist\WoburnAutoEQ"
set "SHORTCUT=%USERPROFILE%\Desktop\WoburnAutoEQ.lnk"

if not exist "%BASE%" (
  echo Base folder F:\autoEQ not found. Please create it or run this script from F:\autoEQ.
  exit /b 1
)

cd /d "%BASE%"

echo Closing running WoburnAutoEQ instances so the shortcut build can be replaced...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-Process WoburnAutoEQ -ErrorAction SilentlyContinue | Stop-Process -Force"

echo Restoring packages...
dotnet restore
if errorlevel 1 exit /b 1

echo Building Release...
dotnet build WoburnAutoEQ.sln -c Release --no-restore
if errorlevel 1 exit /b 1

echo Publishing portable self-contained app...
dotnet publish WoburnAutoEQ\WoburnAutoEQ.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=false
if errorlevel 1 exit /b 1

if exist "%DIST%" (
  echo Cleaning old dist folder...
  del /q "%DIST%\*" >nul 2>nul
  for /d %%D in ("%DIST%\*") do rd /s /q "%%D"
) else (
  mkdir "%DIST%"
)

echo Copying publish output...
xcopy "%PUBLISH%\*" "%DIST%\" /E /I /Y >nul
if errorlevel 1 exit /b 1

copy /Y "%BASE%\README.md" "%DIST%\README.md" >nul

echo Creating desktop shortcut...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$s=(New-Object -ComObject WScript.Shell).CreateShortcut('%SHORTCUT%'); $s.TargetPath='%DIST%\WoburnAutoEQ.exe'; $s.WorkingDirectory='%DIST%'; $s.IconLocation='%DIST%\WoburnAutoEQ.exe'; $s.Save()"
if errorlevel 1 exit /b 1

> "%DIST%\run_as_admin_note.txt" echo Run WoburnAutoEQ.exe as Administrator the first time so it can add:
>> "%DIST%\run_as_admin_note.txt" echo Include: woburn_autoeq.txt
>> "%DIST%\run_as_admin_note.txt" echo.
>> "%DIST%\run_as_admin_note.txt" echo to:
>> "%DIST%\run_as_admin_note.txt" echo C:\Program Files\EqualizerAPO\config\config.txt
>> "%DIST%\run_as_admin_note.txt" echo.
>> "%DIST%\run_as_admin_note.txt" echo After that, normal user mode should be enough for monitoring.

echo.
echo Portable app folder:
echo F:\autoEQ\dist\WoburnAutoEQ
echo.
echo Executable:
echo F:\autoEQ\dist\WoburnAutoEQ\WoburnAutoEQ.exe
echo.
echo Desktop shortcut:
echo %SHORTCUT%

endlocal