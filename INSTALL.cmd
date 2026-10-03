@echo off
setlocal
title Install MagicDragon Safeguardian
echo MagicDragon Safeguardian 1.0.1 - Windows Application
echo.
echo Close Valheim before installing or upgrading.
echo Existing backups, recovery copies, settings, and logs will be preserved.
echo.
echo IMPORTANT: This executable is not digitally signed.
echo Windows SmartScreen or antivirus software may display a warning.
echo.
choice /C YN /N /M "Install or upgrade now? [Y/N] "
if errorlevel 2 exit /b 0
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1"
if errorlevel 1 (
  echo.
  echo Installation did not complete.
  pause
  exit /b 1
)
echo.
echo Finished successfully.
pause
