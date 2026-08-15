@echo off
echo [CI] Stopping vECU process...
taskkill /F /IM vECU.exe >nul 2>&1
taskkill /F /IM vecu.exe >nul 2>&1
echo [CI] vECU process terminated successfully.
exit /b 0
