@echo off
setlocal
REM %1 = regular expression
REM %2 = input file
REM %3 = excerpt file
if "%~1"=="" goto usage
if "%~2"=="" goto usage
if "%~3"=="" goto usage
"%~dp0exe\win-x64\excerptStringPutback.exe" excerpt "%~1" "%~2" "%~3"
set "rc=%ERRORLEVEL%"
echo ERRORLEVEL=%rc%
exit /b %rc%

:usage
echo usage: excerpt.bat pattern input-file excerpt-file
exit /b 1
