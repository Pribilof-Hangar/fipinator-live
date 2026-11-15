@echo off
rem Starts Logitech's DirectOutput service so FIPinator can use the FIPs.
rem Close SPAD.neXt first. Windows will ask for administrator permission.
echo Starting the Logitech DirectOutput service (close SPAD.neXt first)...
powershell -NoProfile -Command "Start-Process powershell -Verb RunAs -Wait -ArgumentList '-NoProfile','-Command','Start-Service SaiDOutput'"
powershell -NoProfile -Command "$s = Get-Service SaiDOutput -ErrorAction SilentlyContinue; if ($s) { 'Service is now: ' + $s.Status } else { 'The Logitech DirectOutput service is not installed.' }"
pause
