@echo off
"%~dp0exe\win-x64\excerptStringPutback.exe" write %*
exit /b %ERRORLEVEL%
