@echo off
rem Publishes the Dev Runner app (DevRunner.Desktop.exe) into .\dist
setlocal
cd /d "%~dp0"
dotnet publish src\mk.dev.runner.desktop -c Release -o dist --nologo -v q || exit /b 1
echo.
echo Done: %~dp0dist\DevRunner.Desktop.exe
