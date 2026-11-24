@echo off
rem Asks DeskCiv to save and quit. Use this if the tray icon cannot be reached.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\stop-deskciv.ps1"
