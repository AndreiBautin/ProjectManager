@echo off
set "ROOT=%~dp0"

echo Launching Personal COO...
echo   Backend:  http://localhost:5071
echo   Frontend: http://localhost:5174
echo.

start "Personal COO - Backend" cmd /k ""%ROOT%backend\ProjectManager.Api\run-backend.bat""

echo Waiting for backend to come up...
timeout /t 8 /nobreak >nul

start "Personal COO - Frontend" cmd /k ""%ROOT%frontend\project-manager-web\run-frontend.bat""

echo Waiting for frontend to come up...
timeout /t 6 /nobreak >nul

start "" http://localhost:5174

echo.
echo Two windows just opened (backend + frontend). Leave them running while you use the app.
echo Close either window to stop that half of the app.
