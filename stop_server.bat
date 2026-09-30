@echo off
echo Stopping SilentLink...
taskkill /F /IM SilentLink.exe >nul 2>&1
for /f "tokens=5" %%a in ('netstat -aon ^| findstr /r ":5000 *LISTENING"') do (
    taskkill /F /PID %%a >nul 2>&1
)
echo SilentLink stopped.
pause
