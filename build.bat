@echo off
setlocal

rem ============================================================
rem  PCL2 CE Offline Launcher - build script
rem  Requires: .NET Framework 4.x compiler (ships with Windows)
rem ============================================================

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo [x] Cannot find csc.exe, need .NET Framework 4.x
    echo     Path: %CSC%
    pause
    exit /b 1
)

if not exist "build" mkdir "build"

echo [1/2] Building patcher...
"%CSC%" /nologo /target:exe /platform:x64 /optimize+ /out:build\PCLCE-Patch.exe src\Patcher.cs
if errorlevel 1 goto fail

echo [2/2] Building launcher...
"%CSC%" /nologo /target:exe /platform:x64 /optimize+ /out:build\PCLCE-Launcher.exe src\Launcher.cs
if errorlevel 1 goto fail

echo.
echo [OK] Build finished, output in build\
echo        build\PCLCE-Patch.exe
echo        build\PCLCE-Launcher.exe
echo.
echo Usage:
echo   1. Download PCL2 CE from official releases, put it into build\
echo      https://github.com/PCL-Community/PCL-CE/releases
echo   2. Double-click build\PCLCE-Launcher.exe
echo.
pause
exit /b 0

:fail
echo.
echo [x] Build failed, see the error messages above.
pause
exit /b 1