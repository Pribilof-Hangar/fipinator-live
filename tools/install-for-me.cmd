@echo off
rem Runs install-for-me.ps1 (developer install of FIPinator Live and FIPinator 3000 for this user).
rem Builds first unless you pass -NoBuild. The log is in publish\install-log.txt.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-for-me.ps1" %*
