@echo off
dotnet restore
if errorlevel 1 exit /b 1
dotnet run --project AutoEQ\AutoEQ.csproj