@echo off
setlocal
cd /d "%~dp0.."
dotnet publish src\ExcerptStringPutback\ExcerptStringPutback.csproj -c Release -o bin\dll -p:UseAppHost=false -p:DebugType=none -p:DebugSymbols=false
set "rc=%ERRORLEVEL%"
if not "%rc%"=="0" exit /b %rc%
echo built bin\dll\excerptStringPutback.dll
exit /b 0
