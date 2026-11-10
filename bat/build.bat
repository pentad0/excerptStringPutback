@echo off
setlocal
cd /d "%~dp0.."
dotnet publish src\ExcerptStringPutback\ExcerptStringPutback.csproj -c Release -r win-x64 -o exe\win-x64
set "rc=%ERRORLEVEL%"
if not "%rc%"=="0" exit /b %rc%
echo built exe\win-x64\excerptStringPutback.exe
exit /b 0
