@echo off
cd /d %~dp0
rem ============================================================
rem  build.bat [output.exe]
rem  Rebuilds RobloxKeeper using the C# compiler that ships with Windows.
rem  The GitHub Actions release workflow runs this same script, so a local
rem  build and a published build come from the same compiler, flags and
rem  sources - there is only ever one build command in this repository.
rem  Pass an output path to build somewhere else (release.bat uses this to
rem  test-compile without touching a running RobloxKeeper.exe).
rem ============================================================
set OUT=%~1
if "%OUT%"=="" set OUT=RobloxKeeper.exe

if not exist app.ico powershell -NoProfile -ExecutionPolicy Bypass -File make-icon.ps1

C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /optimize+ /target:winexe /out:"%OUT%" /win32icon:app.ico /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Management.dll src\*.cs

rem Propagate the compiler's exit code - without this the script always reports
rem success and a broken build sails straight through CI.
if errorlevel 1 (
    echo Build FAILED
    exit /b 1
)
echo Built %OUT%
exit /b 0
