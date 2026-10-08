@echo off
rem Orkeon installer launcher (Windows).
rem
rem Runs install.ps1, the script beside this file, on the PowerShell that ships
rem with Windows and under an execution policy that holds for this one command:
rem no setting of the machine or of the account is changed. Arguments pass
rem through unchanged:
rem
rem   install.cmd
rem   install.cmd -InstallDir "D:\Tools\Orkeon"
rem   install.cmd -Uninstall
rem
rem The exit code is install.ps1's. A policy set by an organization (Group
rem Policy) overrules the option below: only a signed script runs there.
setlocal
set "ORKEON_PS=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if not exist "%ORKEON_PS%" set "ORKEON_PS=powershell.exe"

"%ORKEON_PS%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*
set "ORKEON_EXIT=%ERRORLEVEL%"

rem Opened by a double-click, this window closes with the script and takes the
rem result with it: keep it open on that one case. The case is known by who
rem started this command interpreter -- Explorer -- and by nothing else: from a
rem terminal, another script or a scheduler, return at once. A lookup that
rem fails means "not Explorer": nothing ever waits for a key nobody will press.
"%ORKEON_PS%" -NoProfile -Command "$me = Get-WmiObject Win32_Process -Filter ('ProcessId=' + $PID); $shell = Get-WmiObject Win32_Process -Filter ('ProcessId=' + $me.ParentProcessId); $opener = Get-WmiObject Win32_Process -Filter ('ProcessId=' + $shell.ParentProcessId); if ($opener.Name -ieq 'explorer.exe') { exit 0 }; exit 1" >nul 2>&1
if errorlevel 1 goto :done
echo.
pause

:done
endlocal & exit /b %ORKEON_EXIT%
