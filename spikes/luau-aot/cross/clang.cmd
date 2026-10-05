@echo off
rem Stands in for clang during a linux-x64 publish on Windows. The real clang
rem runs in WSL. Arguments go through a file: wsl.exe eats backslashes and quotes.
setlocal
rem MSBuild calls this as "clang", quoted and found on PATH. %~dp0 is then the
rem current directory, not this folder, so look the folder up on PATH instead.
for %%I in (clang.cmd) do set "SPIKE_CROSS=%%~dp$PATH:I"
set "SPIKE_ARGS=%TEMP%\spectra-luau-link-%RANDOM%%RANDOM%.txt"
> "%SPIKE_ARGS%" echo %*
wsl -d Ubuntu -- bash "$(wslpath '%SPIKE_CROSS%wsl-run.sh')" clang "$(wslpath '%SPIKE_ARGS%')"
set SPIKE_EXIT=%ERRORLEVEL%
del "%SPIKE_ARGS%" 2>nul
exit /b %SPIKE_EXIT%
