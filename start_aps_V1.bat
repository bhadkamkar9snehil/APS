@echo off
title X-APS Launcher

echo.
echo  ========================================
echo   X-APS  -  Advanced Planning System
echo  ========================================
echo.

:: Kill any existing servers on ports 5000 / 3131
for /f "tokens=5" %%a in ('netstat -ano 2^>nul ^| findstr ":5000 "') do taskkill /PID %%a /F >nul 2>&1

:: Start API server (background)
echo  [1/2] Starting API server on port 5000...
start /B "X-APS API" python xaps_application_api.py

:: Wait for API to boot
timeout /t 4 /nobreak >nul

:: Check API is alive
curl -s http://localhost:5000/api/health >nul 2>&1
if errorlevel 1 (
    echo  [!] API server failed to start - check python / dependencies
    pause
    exit /b 1
)
echo  [OK] API server running

:: Start UI server
echo  [2/2] Starting UI server on port 3131...
start /B "X-APS UI" npx serve -s ui_design -p 3131 --no-clipboard

timeout /t 2 /nobreak >nul
echo  [OK] UI server running

:: Open browser
echo  [->] Opening browser...
start http://localhost:3131

echo.
echo  X-APS is running:
echo    UI  -^>  http://localhost:3131
echo    API -^>  http://localhost:5000
echo.
echo  Close this window to keep servers running in background.
echo  Press Ctrl+C in each background window to stop them.
pause
