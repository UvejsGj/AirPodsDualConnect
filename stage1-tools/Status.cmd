@echo off
rem Shows what Windows currently sees for the AirPods. Changes nothing.
if not exist "%~dp0DualConnect.exe" (
  echo DualConnect.exe is missing. Run Build.cmd in this folder first.
  pause
  exit /b 1
)
"%~dp0DualConnect.exe" status %*
pause
