@echo off
REM Build skynet_crypt_shim.c -> skynet_crypt.dll (x64)
REM Output: Assets\Plugins\Native\lib\x86_64\skynet_crypt.dll

setlocal

set SHIM_SRC=%~dp0src\skynet_crypt_shim.c
set OUT_DIR=%~dp0lib\x86_64
set OUT_DLL=%OUT_DIR%\skynet_crypt.dll

REM Use MinGW gcc (Windows x64, target x86_64-w64-mingw32)
set CC=gcc
set CFLAGS=-O2 -Wall -shared -fPIC

echo Building %OUT_DLL% ...
if not exist "%OUT_DIR%" mkdir "%OUT_DIR%"

"%CC%" %CFLAGS% -o "%OUT_DLL%" "%SHIM_SRC%" -Wl,--out-implib,"%OUT_DIR%\skynet_crypt.lib"

if %ERRORLEVEL% NEQ 0 (
    echo BUILD FAILED
    exit /b 1
)

echo BUILD OK: %OUT_DLL%
dir "%OUT_DLL%"

endlocal
