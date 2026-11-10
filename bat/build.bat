@echo off
setlocal
cd /d "%~dp0.."
dotnet publish src\ExcerptStringPutback\ExcerptStringPutback.csproj -c Release -r win-x64 -o bin\win-x64
set "rc=%ERRORLEVEL%"
if not "%rc%"=="0" exit /b %rc%
echo built bin\win-x64\excerptStringPutback.exe
dotnet publish src\ExcerptStringPutback\ExcerptStringPutback.csproj -c Release -o bin\dll -p:UseAppHost=false -p:DebugType=none -p:DebugSymbols=false
set "rc=%ERRORLEVEL%"
if not "%rc%"=="0" exit /b %rc%
echo built bin\dll\excerptStringPutback.dll
exit /b 0
