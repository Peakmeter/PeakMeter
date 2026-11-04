# Only for PeakMeter 1.45 and older: their updater still runs this file. PeakMeter.exe now installs itself.
param([switch]$Silent)
Start-Process -FilePath (Join-Path $PSScriptRoot 'PeakMeter.exe') -ArgumentList '--install-update'
