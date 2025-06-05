@echo off
setlocal

:: Set the program path or name here
set PROGRAM="C:\Path\To\KSPLocalizer.exe"

:: Call the program with all arguments passed to this script
%PROGRAM% %*

endlocal

