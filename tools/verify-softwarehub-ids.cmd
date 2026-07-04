@echo off
REM Double-click to verify every Software Hub winget Id against the real winget catalog.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0verify-softwarehub-ids.ps1"
pause
