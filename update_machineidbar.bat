@echo off
:: ============================================================================
:: Machine ID Bar — Update Script
:: ============================================================================
:: Replaces the installed MachineIdBar.exe with a new version while preserving
:: all registry settings (label, color, font, position, show/hide flags).
::
:: The new exe must be placed next to this script as "MachineIdBar.exe".
::
:: Usage:
::   1. Copy this folder (update_machineidbar.bat + MachineIdBar.exe) to the
::      target machine.
::   2. Double-click update_machineidbar.bat (or run from an elevated prompt
::      if the machine has UAC restrictions).
::   3. The script will:
::      a. Kill any running MachineIdBar.exe
::      b. Back up the old exe (MachineIdBar.exe.bak)
::      c. Copy the new exe into place
::      d. Relaunch it
::
:: No registry settings are touched — your label, color, and preferences
:: carry over automatically.
:: ============================================================================

setlocal
cd /d "%~dp0"

set "SRC=%~dp0MachineIdBar.exe"
set "DST=%LOCALAPPDATA%\MachineIdBar\MachineIdBar.exe"
set "INSTALL_DIR=%LOCALAPPDATA%\MachineIdBar"

echo.
echo ============================================
echo   Machine ID Bar - Update
echo ============================================
echo.

:: --- Check that the new exe exists next to this script ---
if not exist "%SRC%" (
    echo ERROR: MachineIdBar.exe not found next to this script.
    echo        Expected: %SRC%
    echo        Place the new MachineIdBar.exe in the same folder as this script.
    echo.
    pause
    exit /b 1
)

:: --- Check that the app is installed ---
if not exist "%INSTALL_DIR%" (
    echo WARNING: Install directory not found: %INSTALL_DIR%
    echo          The app may not be installed yet.
    echo          Creating directory and installing...
    mkdir "%INSTALL_DIR%" 2>nul
)

:: --- Kill running instance ---
echo Stopping running instance...
taskkill /F /IM MachineIdBar.exe >nul 2>&1
ping 127.0.0.1 -n 3 >nul 2>&1

:: --- Back up old exe ---
if exist "%DST%" (
    echo Backing up old version...
    copy /Y "%DST%" "%DST%.bak" >nul 2>&1
)

:: --- Copy new exe (retry up to 5 times) ---
echo Installing new version...
set COPIED=0
set RETRY=0

:copyretry
copy /Y "%SRC%" "%DST%" >nul 2>&1
if not errorlevel 1 goto copydone
set /a RETRY=RETRY+1
if %RETRY% LSS 5 (
    ping 127.0.0.1 -n 2 >nul 2>&1
    goto copyretry
)

:copydone
if exist "%DST%" set COPIED=1
if not "%COPIED%"=="1" (
    echo.
    echo ERROR: Could not copy the new exe to %DST%
    echo        The file may be locked. Try:
    echo        1. Close Machine ID Bar ^(Ctrl+click the bar^)
    echo        2. Run Task Manager and end any MachineIdBar.exe process
    echo        3. Run this script again
    echo.
    pause
    exit /b 1
)

echo.
echo ============================================
echo   Update complete!
echo ============================================
echo.
echo   Installed:  %DST%
echo   Backup:     %DST%.bak
echo   Settings:   Preserved ^(from registry^)
echo.

:: --- Clean up old debug log ---
if exist "%INSTALL_DIR%\MachineIdBar_debug.log" (
    del /F /Q "%INSTALL_DIR%\MachineIdBar_debug.log" >nul 2>&1
)

:: --- Relaunch ---
echo Launching Machine ID Bar...
start "" "%DST%"

echo.
echo   Done! The bar should appear on your taskbar(s).
echo   If you need to revert, rename MachineIdBar.exe.bak back
echo   to MachineIdBar.exe in %INSTALL_DIR%
echo.
pause
