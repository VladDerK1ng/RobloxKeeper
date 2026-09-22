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
rem
rem  It builds even while RobloxKeeper is running - which it usually is,
rem  because the window's X hides it to the tray rather than exiting.
rem  Windows will not overwrite a running program but will rename one, so
rem  the running copy is moved aside as RobloxKeeper.old.exe and keeps
rem  going, and the new build takes its place. The next build tidies the
rem  old copy away once it is no longer running.
rem ============================================================
set OUT=%~1
if "%OUT%"=="" set OUT=RobloxKeeper.exe
for %%F in ("%OUT%") do (
    set NEW=%%~dpnF.new%%~xF
    set OLDBASE=%%~dpnF.old
    set EXT=%%~xF
)
set OLD=%OLDBASE%%EXT%

if not exist app.ico powershell -NoProfile -ExecutionPolicy Bypass -File make-icon.ps1

rem Old copies whose program has since exited. One still running stays.
del "%OLDBASE%*%EXT%" >nul 2>&1
if exist "%NEW%" del "%NEW%" >nul 2>&1

C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /optimize+ /target:winexe /out:"%NEW%" /win32icon:app.ico /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Management.dll /r:System.Security.dll /r:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Runtime.dll /r:C:\Windows\System32\WinMetadata\Windows.Foundation.winmd /r:C:\Windows\System32\WinMetadata\Windows.Globalization.winmd /r:C:\Windows\System32\WinMetadata\Windows.Graphics.winmd /r:C:\Windows\System32\WinMetadata\Windows.Media.winmd /r:C:\Windows\System32\WinMetadata\Windows.Storage.winmd /r:lib\Microsoft.Web.WebView2.Core.dll /r:lib\Microsoft.Web.WebView2.WinForms.dll /resource:lib\Microsoft.Web.WebView2.Core.dll,Microsoft.Web.WebView2.Core.dll /resource:lib\Microsoft.Web.WebView2.WinForms.dll,Microsoft.Web.WebView2.WinForms.dll /resource:lib\WebView2Loader.dll,WebView2Loader.dll src\*.cs

rem Propagate the compiler's exit code - without this the script always reports
rem success and a broken build sails straight through CI.
if errorlevel 1 (
    if exist "%NEW%" del "%NEW%" >nul 2>&1
    echo Build FAILED
    exit /b 1
)

rem Into place. This works whenever RobloxKeeper is not running.
move /y "%NEW%" "%OUT%" >nul 2>&1
if not errorlevel 1 goto built

rem It is running. Move the running copy aside - under a numbered name if
rem an earlier old copy is itself still running - and put the build in its
rem place.
set N=0
:pickold
if not exist "%OLD%" goto moveaside
set /a N+=1
if %N% gtr 20 goto cantreplace
set OLD=%OLDBASE%%N%%EXT%
goto pickold

:moveaside
move /y "%OUT%" "%OLD%" >nul 2>&1
if errorlevel 1 goto cantreplace
move /y "%NEW%" "%OUT%" >nul 2>&1
if errorlevel 1 (
    move /y "%OLD%" "%OUT%" >nul 2>&1
    goto cantreplace
)
echo Built %OUT%
echo.
echo RobloxKeeper is still running the previous build. To use this one, exit it -
echo right-click its icon by the clock and choose Exit - then start it again.
exit /b 0

:cantreplace
echo Built, but couldn't put it in place of %OUT%.
echo Exit RobloxKeeper - right-click its icon by the clock and choose Exit - and run build.bat again.
if exist "%NEW%" del "%NEW%" >nul 2>&1
echo Build FAILED
exit /b 1

:built
echo Built %OUT%
exit /b 0
