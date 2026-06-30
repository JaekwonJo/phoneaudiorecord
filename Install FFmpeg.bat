@echo off
setlocal
set "APP_HOME=%~dp0"

where ffmpeg.exe >nul 2>nul
if %errorlevel%==0 (
  for /f "delims=" %%F in ('where ffmpeg.exe') do (
    copy /Y "%%F" "%APP_HOME%tools\ffmpeg.exe" >nul
    echo FFmpeg copied to tools folder.
    pause
    exit /b 0
  )
)

echo FFmpeg was not found on this PC.
echo.
echo Install it with this command, then run this file again:
echo winget install Gyan.FFmpeg
echo.
pause
