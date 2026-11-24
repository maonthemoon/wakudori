@echo off
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo ERROR: csc.exe was not found. .NET Framework 4.x is required.
    set "RC=1"
    goto :end
)

if not exist out mkdir out

"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 /win32manifest:app.manifest /win32icon:app.ico /out:out\Wakudori.exe /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll src\*.cs
if errorlevel 1 (
    echo Build FAILED
    set "RC=1"
) else (
    echo Build succeeded: out\Wakudori.exe
    set "RC=0"
)

:end
rem Skip the pause when "nopause" is given as an argument.
if /i not "%~1"=="nopause" pause
exit /b %RC%
