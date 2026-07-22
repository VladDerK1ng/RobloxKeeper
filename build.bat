@echo off
cd /d %~dp0
rem Rebuild RobloxKeeper.exe using the C# compiler that ships with Windows.
if not exist app.ico powershell -NoProfile -ExecutionPolicy Bypass -File make-icon.ps1
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /out:RobloxKeeper.exe /win32icon:app.ico /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll RobloxKeeper.cs
if %errorlevel%==0 (echo Built RobloxKeeper.exe) else (echo Build FAILED)
