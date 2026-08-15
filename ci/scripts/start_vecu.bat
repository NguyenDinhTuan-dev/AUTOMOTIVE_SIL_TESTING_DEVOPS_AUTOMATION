@echo off
echo [CI] Starting vECU DoIP Server in background...
start "" "%~dp0..\..\apps\vecu\build\vECU.exe"
exit /b 0
