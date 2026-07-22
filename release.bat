@echo off
setlocal
cd /d %~dp0

rem ============================================================
rem  release.bat <version>
rem  Bumps APP_VERSION, builds, commits, pushes, and publishes
rem  a GitHub release with RobloxKeeper.exe attached.
rem  Example: release.bat 1.4.1
rem ============================================================

if "%~1"=="" (
    echo Usage: release.bat ^<version^>
    echo Example: release.bat 1.4.1
    exit /b 1
)
set VERSION=%~1

echo [1/5] Setting APP_VERSION to %VERSION% ...
powershell -NoProfile -Command "$c = Get-Content RobloxKeeper.cs -Raw; $c = $c -replace 'APP_VERSION = \"[\d.]+\"', 'APP_VERSION = \"%VERSION%\"'; Set-Content RobloxKeeper.cs -Value $c -Encoding UTF8"
if errorlevel 1 goto :fail

echo [2/5] Building ...
call build.bat
if not exist RobloxKeeper.exe goto :fail

echo [3/5] Committing ...
git add -A
git diff --cached --quiet
if errorlevel 1 (
    git commit -m "Release v%VERSION%"
    if errorlevel 1 goto :fail
) else (
    echo        No source changes to commit.
)

echo [4/5] Pushing ...
git push origin main
if errorlevel 1 goto :fail

echo [5/5] Publishing GitHub release v%VERSION% ...
gh release create v%VERSION% RobloxKeeper.exe --title "RobloxKeeper v%VERSION%" --generate-notes
if errorlevel 1 goto :fail

echo.
echo Done — v%VERSION% is live.
exit /b 0

:fail
echo.
echo Release FAILED at the step above.
exit /b 1
