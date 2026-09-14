@echo off
start "" powershell.exe -NoProfile -WindowStyle Hidden -File "%~dp0scripts\config-ui.ps1" -Action Launch
