@echo off
rem Stands in for objcopy during a linux-x64 publish on Windows. See clang.cmd.
setlocal
for %%I in (objcopy.cmd) do set "SPIKE_CROSS=%%~dp$PATH:I"
set "SPIKE_ARGS=%TEMP%\spectra-luau-objcopy-%RANDOM%%RANDOM%.txt"
> "%SPIKE_ARGS%" echo %*
wsl -d Ubuntu -- bash "$(wslpath '%SPIKE_CROSS%wsl-run.sh')" objcopy "$(wslpath '%SPIKE_ARGS%')"
set SPIKE_EXIT=%ERRORLEVEL%
del "%SPIKE_ARGS%" 2>nul
exit /b %SPIKE_EXIT%
