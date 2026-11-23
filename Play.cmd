@echo off
title Heavy Pockets
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Tools\Start-Raid.ps1"
if errorlevel 1 pause
