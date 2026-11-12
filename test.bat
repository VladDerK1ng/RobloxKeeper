@echo off
cd /d %~dp0
rem ============================================================
rem  test.bat
rem  Compiles the production sources together with tests\ into a
rem  console runner and runs it. Same csc.exe as build.bat, so the
rem  code under test is the code that ships - there is no second
rem  compiler, no framework and no package to install.
rem
rem  /main: picks the test runner's entry point over Program.Main.
rem ============================================================
set OUT=%TEMP%\RobloxKeeper.Tests.exe

C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:exe /out:"%OUT%" /main:RobloxKeeper.Tests.Harness /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Management.dll /r:System.Security.dll /resource:MODDING.md,RobloxKeeper.MODDING.md /r:System.IO.Compression.dll /r:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Runtime.dll /r:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Runtime.WindowsRuntime.dll /r:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Runtime.InteropServices.WindowsRuntime.dll /r:C:\Windows\System32\WinMetadata\Windows.Foundation.winmd /r:C:\Windows\System32\WinMetadata\Windows.Globalization.winmd /r:C:\Windows\System32\WinMetadata\Windows.Graphics.winmd /r:C:\Windows\System32\WinMetadata\Windows.Media.winmd /r:C:\Windows\System32\WinMetadata\Windows.Storage.winmd /r:lib\Microsoft.Web.WebView2.Core.dll /r:lib\Microsoft.Web.WebView2.WinForms.dll /r:lib\MoonSharp.Interpreter.dll /resource:lib\MoonSharp.Interpreter.dll,MoonSharp.Interpreter.dll /resource:THIRD-PARTY-NOTICES.md,RobloxKeeper.THIRD-PARTY-NOTICES.md src\*.cs tests\*.cs

if errorlevel 1 (
    echo Test build FAILED
    exit /b 1
)

"%OUT%"
exit /b %errorlevel%
