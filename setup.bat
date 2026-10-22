@echo off
:: Machine ID Bar - Setup launcher
:: Runs the Python setup script which handles label, color, install, and startup.
cd /d "%~dp0"
python setup.py
pause
