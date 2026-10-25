@echo off
rem Starts the PeakMeter installer (PowerShell, no admin rights needed).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
