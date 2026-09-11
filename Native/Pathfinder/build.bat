@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\..\Tools\build-native.ps1"
exit /b %errorlevel%
