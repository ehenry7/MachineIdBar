@echo off
:: Machine ID Bar launcher
:: Left or right-click the label to copy info. Ctrl+click to exit.
:: Change the label and color below for each machine.
cd /d "%~dp0"
taskkill /F /IM machine_id_bar.exe >nul 2>&1
start "" dist\machine_id_bar.exe --label="Secure PC" --bg="#d13438"
