@echo off
cd /d "%~dp0"

where npm >nul 2>nul
if errorlevel 1 goto :nonode

REM Check for the vite binary itself, not just the node_modules folder. A folder
REM can survive an interrupted install or an antivirus sweep while holding none
REM of the executables - and the old "if exist node_modules" check would then
REM skip the install forever, leaving 'npm run dev' to fail with
REM "'vite' is not recognized" every single time.
if exist "node_modules\.bin\vite.cmd" goto :rundev

if exist node_modules (
    echo Dependencies look incomplete - reinstalling from scratch...
    rmdir /s /q node_modules
) else (
    echo Installing frontend dependencies ^(first run only^)...
)

REM 'npm ci' is the reproducible install when a lockfile is present; it also
REM guarantees a clean tree, which is the point after a corrupt one.
if exist package-lock.json (
    call npm ci
) else (
    call npm install
)
if errorlevel 1 goto :installfail
if not exist "node_modules\.bin\vite.cmd" goto :installfail

:rundev
echo Starting dev server on http://localhost:5174 ...
call npm run dev
pause
exit /b 0

:nonode
echo Node.js/npm not found. Install it from https://nodejs.org
pause
exit /b 1

:installfail
echo.
echo Installing dependencies failed - see the errors above.
echo A network or proxy problem is the usual cause; try again once online.
pause
exit /b 1
