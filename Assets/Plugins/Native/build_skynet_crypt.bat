@echo off
REM Build skynet_crypt_shim.c -> skynet_crypt.dll (x64)
REM Output: Assets\Plugins\x86_64\skynet_crypt.dll (Unity 识别的 plugin 位置, 唯一一份)
REM
REM 注: 之前输出到 Assets\Plugins\Native\lib\x86_64\ 会跟 Unity 期望位置
REM     Assets\Plugins\x86_64\ 同名冲突, Unity Editor 报
REM     "Multiple plugins with the same name"。

setlocal

set SHIM_SRC=%~dp0src\skynet_crypt_shim.c
set OUT_DIR=%~dp0..\x86_64
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

REM 清理冗余的旧 build 路径 (lib/x86_64/), 跟 Plugins\x86_64\ 同 dll 会触发
REM Unity Editor 报 "Multiple plugins with the same name"。此 bat 现已只输出
REM 到 OUT_DIR = ..\x86_64\, lib/ 是历史 build 路径产物, 删之不损功能。
if exist "%~dp0lib" (
    echo Cleaning up old build path: %~dp0lib
    rmdir /s /q "%~dp0lib"
)
if exist "%~dp0lib.meta" del /q "%~dp0lib.meta"

endlocal
