@echo off
setlocal
REM %1 = regular expression
REM %2 = input file
REM %3 = excerpt file
REM %4 = output file
if "%~1"=="" goto usage
if "%~2"=="" goto usage
if "%~3"=="" goto usage
if "%~4"=="" goto usage
"%~dp0exe\win-x64\excerptStringPutback.exe" putback "%~1" "%~2" "%~3" "%~4"
set "rc=%ERRORLEVEL%"
echo ERRORLEVEL=%rc%
exit /b %rc%

:usage
echo usage: putback.bat pattern input-file excerpt-file output-file
exit /b 1
