@echo off
rem Builds the installer and update packages for Dev Runner into .\releases
rem
rem   release.cmd 0.2.0          a Stable release
rem   release.cmd 0.2.0-dev.1 dev   a Dev (test) release
rem
rem Output: releases\DevRunner-win-Setup.exe (or -dev-), the update packages, and the feed file.
rem Publishing them to GitHub Releases comes later (needs the GitHub repo).
setlocal
cd /d "%~dp0"
if "%~1"=="" (
  echo usage: release.cmd ^<version^> [dev]
  exit /b 1
)
set VERSION=%~1
set CHANNEL=win
if /i "%~2"=="dev" set CHANNEL=dev

dotnet tool restore || exit /b 1
if exist publish rmdir /s /q publish
dotnet publish src\mk.dev.runner.desktop -c Release -o publish -p:Version=%VERSION% --nologo -v q || exit /b 1
dotnet vpk pack --packId DevRunner --packVersion %VERSION% --packDir publish --mainExe DevRunner.Desktop.exe ^
  --packTitle "Dev Runner" --packAuthors "Mike Knights" --channel %CHANNEL% --outputDir releases || exit /b 1
echo.
echo Done: %~dp0releases
