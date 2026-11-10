@echo off
setlocal
REM %1 = input file
REM %2 = excerpt file
REM %3 = output file
if "%~1"=="" goto usage
if "%~2"=="" goto usage
if "%~3"=="" goto usage
dotnet run --project "%~dp0src\ExcerptStringPutback\ExcerptStringPutback.csproj" -c Release --no-launch-profile -- putback "%~1" "%~2" "%~3"
set "rc=%ERRORLEVEL%"
echo ERRORLEVEL=%rc%
exit /b %rc%

:usage
echo usage: putback.bat input-file excerpt-file output-file
exit /b 1
