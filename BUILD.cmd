@echo off
setlocal
title Build MagicDragon Safeguardian from Source
echo MagicDragon Safeguardian 1.0.1 - Windows Source Build
echo.
echo This will compile the Windows application and uninstaller from C# source.
echo The compiled executables are not digitally signed.
echo Windows SmartScreen or antivirus software may display a warning.
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build.ps1"
if errorlevel 1 (
  echo.
  echo Build failed. Read the error shown above.
  pause
  exit /b 1
)
echo.
echo Build completed successfully.
echo Output: %~dp0Build
pause
