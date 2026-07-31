@echo off
setlocal
set ROOT=%~dp0

rem ============================================================
rem  release.bat <version>
rem  Bumps APP_VERSION, test-compiles, commits, pushes and tags.
rem  GitHub Actions takes it from there: it builds RobloxKeeper.exe
rem  from the tagged source and publishes the release itself, so no
rem  binary is ever uploaded by hand.
rem  Accepts "1.4.2" or "v1.4.2" - both work.
rem ============================================================

if "%~1"=="" (
    echo Usage: release.bat ^<version^>
    echo Example: release.bat 1.4.2
    exit /b 1
)
set VERSION=%~1

rem Strip any leading v/V so "v1.4.2" and "1.4.2" both work
:stripv
if /i "%VERSION:~0,1%"=="v" (
    set VERSION=%VERSION:~1%
    goto :stripv
)

rem Validate x.y or x.y.z
echo %VERSION%| findstr /r /c:"^[0-9][0-9]*\.[0-9][0-9]*$" /c:"^[0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*$" >nul
if errorlevel 1 (
    echo Invalid version "%~1" - expected numbers like 1.0 or 1.4.2
    exit /b 1
)

rem A tag that already exists would be silently ignored by the push, and no
rem release would ever appear. Catch it before anything is committed.
git -C "%ROOT%." rev-parse -q --verify "refs/tags/v%VERSION%" >nul
if not errorlevel 1 (
    echo Tag v%VERSION% already exists - pick a new version number.
    exit /b 1
)

echo [1/5] Setting APP_VERSION to %VERSION% ...
powershell -NoProfile -Command "$f = '%ROOT%src\AppInfo.cs'; $c = Get-Content $f -Raw; $c = $c -replace 'APP_VERSION = \".+?\"', 'APP_VERSION = \"%VERSION%\"'; Set-Content $f -Value $c -Encoding UTF8"
if errorlevel 1 goto :fail
powershell -NoProfile -Command "if ((Get-Content '%ROOT%src\AppInfo.cs' -Raw) -notmatch [regex]::Escape('APP_VERSION = \"%VERSION%\"')) { exit 1 }"
if errorlevel 1 (
    echo Version stamp did not apply - aborting.
    goto :fail
)

rem Compiled to a scratch path so a running RobloxKeeper.exe never has to be
rem killed just to check that the tag will build. The real binary is produced
rem by the release workflow, not here.
echo [2/5] Test-compiling ...
call "%ROOT%build.bat" "%TEMP%\RobloxKeeper.build-check.exe"
if errorlevel 1 goto :fail
del "%TEMP%\RobloxKeeper.build-check.exe" 2>nul

echo [3/5] Committing ...
git -C "%ROOT%." add -A
git -C "%ROOT%." diff --cached --quiet
if errorlevel 1 (
    git -C "%ROOT%." commit -m "Release v%VERSION%"
    if errorlevel 1 goto :fail
) else (
    echo        No source changes to commit.
)

echo [4/5] Pushing main ...
git -C "%ROOT%." push origin main
if errorlevel 1 goto :fail

echo [5/5] Tagging v%VERSION% and pushing the tag ...
git -C "%ROOT%." tag "v%VERSION%"
if errorlevel 1 goto :fail
git -C "%ROOT%." push origin "v%VERSION%"
if errorlevel 1 goto :fail

echo.
echo Done - GitHub Actions is now building and publishing v%VERSION%.
echo Watch it here:
echo   https://github.com/VladDerK1ng/RobloxKeeper/actions
exit /b 0

:fail
echo.
echo Release FAILED at the step above. Nothing was tagged.
exit /b 1
