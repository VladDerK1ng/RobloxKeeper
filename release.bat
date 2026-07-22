@echo off
setlocal
set ROOT=%~dp0

rem ============================================================
rem  release.bat <version>
rem  Stops the app, bumps APP_VERSION, builds, commits, pushes,
rem  publishes a GitHub release with RobloxKeeper.exe attached,
rem  then starts the app again.
rem  Example: release.bat 1.4.1
rem ============================================================

if "%~1"=="" (
    echo Usage: release.bat ^<version^>
    echo Example: release.bat 1.4.1
    exit /b 1
)
set VERSION=%~1

echo [1/6] Stopping RobloxKeeper if running ...
taskkill /im RobloxKeeper.exe /f >nul 2>nul

echo [2/6] Setting APP_VERSION to %VERSION% ...
powershell -NoProfile -Command "$f = '%ROOT%RobloxKeeper.cs'; $c = Get-Content $f -Raw; $c = $c -replace 'APP_VERSION = \"[\d.]+\"', 'APP_VERSION = \"%VERSION%\"'; Set-Content $f -Value $c -Encoding UTF8"
if errorlevel 1 goto :fail

echo [3/6] Building ...
del "%ROOT%RobloxKeeper.exe" 2>nul
call "%ROOT%build.bat"
if not exist "%ROOT%RobloxKeeper.exe" goto :fail

echo [4/6] Committing ...
git -C "%ROOT%." add -A
git -C "%ROOT%." diff --cached --quiet
if errorlevel 1 (
    git -C "%ROOT%." commit -m "Release v%VERSION%"
    if errorlevel 1 goto :fail
) else (
    echo        No source changes to commit.
)

echo [5/6] Pushing ...
git -C "%ROOT%." push origin main
if errorlevel 1 goto :fail

echo [6/6] Publishing GitHub release v%VERSION% ...
gh release create v%VERSION% "%ROOT%RobloxKeeper.exe" --title "RobloxKeeper v%VERSION%" --generate-notes
if errorlevel 1 goto :fail

start "" "%ROOT%RobloxKeeper.exe"
echo.
echo Done - v%VERSION% is live and the app was restarted.
exit /b 0

:fail
echo.
echo Release FAILED at the step above.
exit /b 1
