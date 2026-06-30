@echo off
set "APP_HOME=%~dp0"
if /i "%~1" neq "run" (
  start "Phone Audio Recorder" /min "%~f0" run
  exit /b
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File "%APP_HOME%Phone Audio Recorder.ps1"
