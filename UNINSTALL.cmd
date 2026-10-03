@echo off
setlocal
title Uninstall MagicDragon Safeguardian
set "UNINSTALLER=%LOCALAPPDATA%\MagicDragon_Safeguardian\Uninstall_MagicDragon_Safeguardian.exe"
if exist "%UNINSTALLER%" (
  start /wait "" "%UNINSTALLER%"
) else (
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1" -Uninstall
)
if errorlevel 1 pause
