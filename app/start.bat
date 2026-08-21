@echo off
setlocal enabledelayedexpansion
set "ROOT=%~dp0"
set "BACKEND_PORT=5071"
set "FRONTEND_PORT=5174"
set "APP_URL=http://localhost:%FRONTEND_PORT%"

echo ============================================
echo   Launching Personal COO
echo     Backend:  http://localhost:%BACKEND_PORT%
echo     Frontend: %APP_URL%
echo ============================================
echo.

REM ---------- Backend ----------
call :probe %BACKEND_PORT% backend
if "!PORTSTATE!"=="foreign" (
    call :conflict %BACKEND_PORT% backend
    if "!RESOLVED!"=="0" goto :abort
)
if "!PORTSTATE!"=="ours" (
    echo Backend already running on port %BACKEND_PORT% - reusing it.
) else (
    echo Starting backend...
    start "Personal COO - Backend" cmd /k ""%ROOT%backend\ProjectManager.Api\run-backend.bat""
    echo Waiting for backend to be ready ^(first run can take a minute^)...
    call :probe %BACKEND_PORT% backend 120
    if "!PORTSTATE!"=="ours" (
        echo Backend is up.
    ) else (
        echo.
        echo WARNING: Backend did not come up within the timeout.
        echo          Check the "Personal COO - Backend" window for the actual error.
        echo.
    )
)

REM ---------- Frontend ----------
call :probe %FRONTEND_PORT% frontend
if "!PORTSTATE!"=="foreign" (
    call :conflict %FRONTEND_PORT% frontend
    if "!RESOLVED!"=="0" goto :abort
)
set "FRONTEND_UP=0"
if "!PORTSTATE!"=="ours" (
    echo Frontend already running on port %FRONTEND_PORT% - reusing it.
    set "FRONTEND_UP=1"
) else (
    echo Starting frontend...
    start "Personal COO - Frontend" cmd /k ""%ROOT%frontend\project-manager-web\run-frontend.bat""
    echo Waiting for frontend to be ready...
    call :probe %FRONTEND_PORT% frontend 60
    if "!PORTSTATE!"=="ours" (
        echo Frontend is up.
        set "FRONTEND_UP=1"
    ) else (
        echo.
        echo WARNING: Frontend did not come up within the timeout.
        echo          Check the "Personal COO - Frontend" window for the actual error.
        echo.
    )
)

echo.
if "!FRONTEND_UP!"=="1" (
    echo Opening %APP_URL% ...
    start "" %APP_URL%
) else (
    echo NOT opening %APP_URL% - the frontend is not serving, so the browser
    echo would only show that the site cannot be reached. Fix the error shown
    echo in the "Personal COO - Frontend" window, then run this again.
)

echo.
echo The app runs in two windows (backend + frontend). Close either window to
echo stop that half. You can close THIS window now.
echo.
pause
exit /b 0

:abort
echo.
echo Startup cancelled - nothing was changed.
echo.
pause
exit /b 1

REM ================= helpers =================

:probe
REM %1 = port, %2 = backend^|frontend, %3 = optional seconds to wait for our app.
REM Sets PORTSTATE to ours / foreign / free (or unknown if the probe itself failed,
REM which is treated like free: try to start and let the real error surface).
set "PORTSTATE=unknown"
set "PORTOWNER=an unidentified process"
set "WAITARG=%3"
if "%WAITARG%"=="" set "WAITARG=0"
for /f "usebackq tokens=1,* delims=|" %%A in (`powershell -NoProfile -ExecutionPolicy Bypass -File "%ROOT%port-check.ps1" -Port %1 -Kind %2 -WaitSeconds %WAITARG%`) do (
    set "PORTSTATE=%%A"
    if not "%%B"=="" set "PORTOWNER=%%B"
)
exit /b 0

:conflict
REM %1 = port, %2 = backend^|frontend. Sets RESOLVED=1 if the port is now usable.
set "RESOLVED=0"
echo.
echo   PORT CONFLICT: %1 is already in use by !PORTOWNER!.
echo   That is not the Personal COO %2, so starting ours here would fail -
echo   and the app would talk to the wrong server if it did not.
echo.
choice /c SQ /n /m "   [S] stop that process and continue, [Q] quit: "
if errorlevel 2 goto :conflict_quit
echo.
echo   Stopping !PORTOWNER! ...
call :killport %1
call :probe %1 %2
if "!PORTSTATE!"=="foreign" (
    echo   Could not free port %1 - it is still held by !PORTOWNER!.
    echo   You may need to close that program yourself, or run this script as admin.
    exit /b 0
)
echo   Port %1 is free.
set "RESOLVED=1"
exit /b 0

:conflict_quit
echo.
echo   Leaving port %1 alone. Close whatever is using it, then run this again.
exit /b 0

:killport
REM Force-stops whatever owns TCP port %1.
set "PIDS="
for /f %%P in ('powershell -NoProfile -Command "$c=Get-NetTCPConnection -LocalPort %1 -State Listen -ErrorAction SilentlyContinue; foreach($x in $c){$x.OwningProcess}"') do set "PIDS=!PIDS! %%P"
for %%P in (!PIDS!) do taskkill /PID %%P /F /T >nul 2>nul
exit /b 0
