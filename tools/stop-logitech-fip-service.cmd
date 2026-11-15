@echo off
rem Stops Logitech's DirectOutput service so SPAD.neXt can use the FIPs again.
rem Windows will ask for administrator permission.
echo Stopping the Logitech DirectOutput service...
powershell -NoProfile -Command "Start-Process powershell -Verb RunAs -Wait -ArgumentList '-NoProfile','-Command','Stop-Service SaiDOutput -Force'"
powershell -NoProfile -Command "$s = Get-Service SaiDOutput -ErrorAction SilentlyContinue; if ($s) { 'Service is now: ' + $s.Status + '. You can start SPAD.neXt again.' } else { 'The Logitech DirectOutput service is not installed.' }"
pause
