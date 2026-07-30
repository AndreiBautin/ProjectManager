@echo off
cd /d "%~dp0"

where npm >nul 2>nul
if errorlevel 1 goto :nonode

if exist node_modules goto :rundev
echo Installing frontend dependencies (first run only)...
call npm install

:rundev
echo Starting dev server on http://localhost:5174 ...
call npm run dev
pause
exit /b 0

:nonode
echo Node.js/npm not found. Install it from https://nodejs.org
pause
exit /b 1
