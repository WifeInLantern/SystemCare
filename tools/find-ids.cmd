@echo off
echo ================= Brave =================
winget search --name "Brave" --source winget --disable-interactivity --accept-source-agreements
echo.
echo ================= Paint.NET =================
winget search "paint.net" --source winget --disable-interactivity --accept-source-agreements
echo.
echo ================= FileZilla =================
winget search "filezilla" --source winget --disable-interactivity --accept-source-agreements
echo.
pause
