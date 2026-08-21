@echo off
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 goto :nodotnet

echo Restoring packages (first run may take a minute)...
dotnet restore
if errorlevel 1 goto :restorefail

echo Starting API on http://localhost:5071 ...
dotnet run
pause
exit /b 0

:nodotnet
echo .NET SDK not found. Install it from https://dotnet.microsoft.com/download/dotnet/8.0
pause
exit /b 1

:restorefail
echo Restore failed - see errors above.
pause
exit /b 1
