@echo off
setlocal
REM %1 = input file
REM %2 = excerpt file
if "%~1"=="" goto usage
if "%~2"=="" goto usage
"%~dp0exe\win-x64\excerptStringPutback.exe" excerpt "%~1" "%~2"
set "rc=%ERRORLEVEL%"
echo ERRORLEVEL=%rc%
exit /b %rc%

:usage
echo usage: excerpt.bat input-file excerpt-file
exit /b 1
