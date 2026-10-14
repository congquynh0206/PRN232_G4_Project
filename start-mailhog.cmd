@echo off
setlocal

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\start-mailhog.ps1"
if errorlevel 1 (
    echo.
    echo MailHog could not start. See the error above.
    pause
    exit /b 1
)

start "" "http://127.0.0.1:8025"
exit /b 0
