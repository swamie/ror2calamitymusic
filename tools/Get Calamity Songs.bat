@echo off
setlocal
title Calamity Music - song installer
cd /d "%~dp0"
set CALAMITY_NO_PAUSE=1

rem Prefer the official Python launcher; fall back to python on PATH.
where py >nul 2>nul
if %errorlevel%==0 (
    py -3 "%~dp0get_songs.py" %*
    goto done
)
where python >nul 2>nul
if %errorlevel%==0 (
    python "%~dp0get_songs.py" %*
    goto done
)

echo Python 3 is needed to install the songs. Opening the download page...
echo Install it (tick "Add python.exe to PATH"), then run this again.
start "" https://www.python.org/downloads/

:done
echo.
pause
