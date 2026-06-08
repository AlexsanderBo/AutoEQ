@echo off
setlocal

pushd "%~dp0" >nul

if not exist "main.cpp" (
    echo [wasapi_autoeq] main.cpp not found in %CD%.
    popd >nul
    exit /b 1
)

where cl.exe >nul 2>nul
if errorlevel 1 (
    echo [wasapi_autoeq] cl.exe not found.
    echo [wasapi_autoeq] Open a Visual Studio Developer Command Prompt, then run native\wasapi_autoeq\build_native.bat.
    popd >nul
    exit /b 1
)

echo [wasapi_autoeq] Building wasapi_autoeq.exe...
cl /nologo /O2 /EHsc /std:c++17 main.cpp /Fe:wasapi_autoeq.exe ole32.lib uuid.lib winmm.lib
set BUILD_EXIT=%ERRORLEVEL%

if not "%BUILD_EXIT%"=="0" (
    echo [wasapi_autoeq] Build failed with exit code %BUILD_EXIT%.
    popd >nul
    exit /b %BUILD_EXIT%
)

echo [wasapi_autoeq] Built %CD%\wasapi_autoeq.exe
popd >nul
exit /b 0