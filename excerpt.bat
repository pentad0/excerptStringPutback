@echo off
setlocal
REM %1 = input file
REM %2 = excerpt file
if "%~1"=="" goto usage
if "%~2"=="" goto usage
dotnet run --project "%~dp0src\ExcerptStringPutback\ExcerptStringPutback.csproj" -c Release --no-launch-profile -- excerpt "%~1" "%~2"
set "rc=%ERRORLEVEL%"
echo ERRORLEVEL=%rc%
exit /b %rc%

:usage
echo usage: excerpt.bat input-file excerpt-file
exit /b 1
