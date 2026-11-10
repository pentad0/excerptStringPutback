@echo off
"%~dp0exe\win-x64\excerptStringPutback.exe" extract %*
exit /b %ERRORLEVEL%
