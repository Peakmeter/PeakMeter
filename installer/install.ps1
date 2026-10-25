# PeakMeter installer: copies the program to %LocalAppData%\Programs\PeakMeter (no admin rights needed),
# creates a desktop shortcut and starts it. Run through "PeakMeter installieren.cmd".
param([switch]$Silent)   # -Silent: called by PeakMeter's own updater (no browser, no pauses, always restart)
$ErrorActionPreference = 'Stop'
$src = $PSScriptRoot
$dst = Join-Path $env:LOCALAPPDATA 'Programs\PeakMeter'

Write-Host ''
Write-Host '  PeakMeter wird installiert ...' -ForegroundColor Cyan
Write-Host ''

# Npcap is the capture driver PeakMeter needs (free, also used by Wireshark). It may not be bundled.
$npcap = (Test-Path "$env:WINDIR\System32\Npcap\wpcap.dll") -or (Test-Path "$env:WINDIR\System32\wpcap.dll")
if (-not $npcap -and -not $Silent) {
    Write-Host '  Npcap fehlt noch. Die Download-Seite wird geoeffnet.' -ForegroundColor Yellow
    Write-Host '  Npcap mit den Standard-Einstellungen installieren, danach PeakMeter starten.' -ForegroundColor Yellow
    Start-Process 'https://npcap.com/#download'
}

# A running copy keeps its files locked: close it and wait until Windows lets go of the files.
Get-Process PeakMeter -ErrorAction SilentlyContinue | Stop-Process -Force
Wait-Process -Name PeakMeter -Timeout 15 -ErrorAction SilentlyContinue

New-Item -ItemType Directory -Force $dst | Out-Null
$copied = $false
for ($try = 1; $try -le 20 -and -not $copied; $try++) {
    try {
        foreach ($item in 'PeakMeter.exe', 'data', 'LICENSE', 'LIESMICH.txt') {
            $from = Join-Path $src $item
            if (Test-Path $from) { Copy-Item $from $dst -Recurse -Force -ErrorAction Stop }
        }
        $copied = $true
    }
    catch { Start-Sleep -Milliseconds 750 }
}
if (-not $copied) {
    Write-Host '  PeakMeter konnte nicht ersetzt werden - bitte PeakMeter beenden und nochmal versuchen.' -ForegroundColor Red
    if (-not $Silent) { Start-Sleep -Seconds 8 }
    exit 1
}

$desktop = [Environment]::GetFolderPath('Desktop')
$shell = New-Object -ComObject WScript.Shell
foreach ($folder in $desktop, (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs')) {
    $lnk = $shell.CreateShortcut((Join-Path $folder 'PeakMeter.lnk'))
    $lnk.TargetPath = Join-Path $dst 'PeakMeter.exe'
    $lnk.WorkingDirectory = $dst
    $lnk.IconLocation = (Join-Path $dst 'PeakMeter.exe') + ',0'
    $lnk.Description = 'PeakMeter - AION 2 DPS-Meter'
    $lnk.Save()
}

Write-Host '  Fertig! PeakMeter liegt auf dem Desktop und im Startmenue.' -ForegroundColor Green
Write-Host '  Tipp: PeakMeter starten, BEVOR du einen Dungeon betrittst.' -ForegroundColor Gray
Write-Host ''
if ($npcap -or $Silent) { Start-Process (Join-Path $dst 'PeakMeter.exe') }
if (-not $Silent) { Start-Sleep -Seconds 4 }
