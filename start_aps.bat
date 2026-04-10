@echo off
setlocal
title X-APS Launcher

cd /d "%~dp0"

echo.
echo  ========================================
echo   X-APS  -  Advanced Planning System
echo  ========================================
echo.

set "API_PORT=5000"
set "UI_PORT=3131"
set "WORKBOOK_PATH=%CD%\APS_BF_SMS_RM.xlsx"

:: Kill any existing servers on ports 5000 / 3131
for %%P in (%API_PORT% %UI_PORT%) do (
    for /f "tokens=5" %%a in ('netstat -ano 2^>nul ^| findstr ":%%P "') do (
        taskkill /PID %%a /F >nul 2>&1
    )
)

:: Start API server (background)
echo  [1/2] Starting API server on port %API_PORT%...
start "X-APS API" /B cmd /c "cd /d ""%CD%"" && set ""WORKBOOK_PATH=%WORKBOOK_PATH%"" && python xaps_application_api.py"

:: Wait for API to boot
timeout /t 10 /nobreak >nul

:: Check API is alive with retry logic
setlocal enabledelayedexpansion
set "retry=0"
set "max_retries=5"
:health_check_loop
curl -f -s http://localhost:%API_PORT%/api/health >nul 2>&1
if errorlevel 1 (
    set /a retry=!retry!+1
    if !retry! lss !max_retries! (
        timeout /t 2 /nobreak >nul
        goto health_check_loop
    )
    echo  [!] API server failed to start - check python / dependencies
    echo  [!] Expected server file: xaps_application_api.py
    pause
    exit /b 1
)
echo  [OK] API server running

:: Start UI server
echo  [2/2] Starting UI server on port %UI_PORT%...
start "X-APS UI" /B cmd /c "cd /d ""%CD%"" && npx serve -s ui_design -p %UI_PORT% --no-clipboard"

timeout /t 2 /nobreak >nul
echo  [OK] UI server running

:: Open browser
echo  [^-^>] Opening browser...
start http://localhost:%UI_PORT%/

echo.
echo  X-APS is running:
echo    UI  -^>  http://localhost:%UI_PORT%
echo    API -^>  http://localhost:%API_PORT%
echo.
echo  Workbook:
echo    %WORKBOOK_PATH%
echo.
echo  Close this window to keep servers running in background.
echo  Press Ctrl+C in each background window to stop them.
pause