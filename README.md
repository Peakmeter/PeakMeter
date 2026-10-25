<p align="center">
  <img src="docs/logo.png" width="112" alt="PeakMeter logo">
</p>

<h1 align="center">PeakMeter</h1>

<p align="center">
  <b>A free, clean and lightweight DPS meter for AION 2 (EU / NA).</b><br>
  Live damage, healing and tanking · boss HP · fight logs · event &amp; world boss timers · Odyle energy · updates itself.
</p>

<p align="center">
  <a href="https://github.com/Peakmeter/PeakMeter/releases/latest"><img alt="Download" src="https://img.shields.io/github/v/release/Peakmeter/PeakMeter?label=download&amp;color=7fd8f2&amp;style=for-the-badge"></a>
  <a href="https://ko-fi.com/peakmeter"><img alt="Support on Ko-fi" src="https://img.shields.io/badge/Ko--fi-support%20PeakMeter-ff5e5b?logo=ko-fi&amp;logoColor=white&amp;style=for-the-badge"></a>
  <img alt="Windows 10 / 11" src="https://img.shields.io/badge/Windows-10%20%2F%2011-1e2440?style=for-the-badge">
  <img alt="License GPL-3.0" src="https://img.shields.io/badge/license-GPL--3.0-3fd18b?style=for-the-badge">
</p>

<p align="center">
  <img src="docs/screenshot-overlay.png" width="420" alt="PeakMeter overlay during a boss fight (demo)">
  &nbsp;
  <img src="docs/screenshot-settings.png" width="300" alt="PeakMeter settings">
</p>

<p align="center"><b>English</b> · <a href="#deutsch">Deutsch</a></p>

## Features

- **Live overlay** – DPS, share and combat power per player, boss HP, fight clock. Solo it shows only you, in a party only your party (like the game's own frames).
- **DPS / HEAL / TANK** – switch between damage dealt, healing done and damage taken.
- **Better than last time?** – in a boss fight you see your DPS against your last attempt, plus your best kill time.
- **Party** – level, gear score and combat power of every member as soon as you join a party.
- **Fight logs** – every boss fight is saved: ranking, every skill with hits, crit and max hit; best kills are marked.
- **Event timers** – Shugo Festival, air raid / dimensional invasion, Spacetime Rift, sieges, Nahma, resets … with a soft chime that never hurts your ears.
- **World boss timers** – all 48 field bosses of Verteron and Altgard. Open *Map → Exploration → Field monsters* once and PeakMeter takes the exact respawn times from the server.
- **Odyle energy** – read straight from the game whenever it changes, counted forward in between; chimes before regeneration is wasted.
- **Alerts at the right edge** – events, world bosses and full energy slide in as small cards and slide out again.
- **Tickets window** – all your characters with their Odyle energy, estimated forward since you last played each.
- **Settings like the big meters** – eDPS / aDPS, combat power or gear score next to names, total damage, hide player names (for streams), UI scale, refresh rate, focus mode, fade when idle, only over the game, region EU / NA.
- **Light on your PC** – no GPU use, low priority; ~0.2 % CPU idle, ~65 MB RAM.
- **German &amp; English**, settings window, custom hotkeys, start with Windows, **automatic updates**.

## Install

1. Install **[Npcap](https://npcap.com/#download)** (free capture driver, keep the default options).
2. Download the newest **`PeakMeter-v…-win-x64.zip`** from [Releases](https://github.com/Peakmeter/PeakMeter/releases/latest), unzip it.
3. Double-click **`PeakMeter installieren.cmd`** – it installs PeakMeter for your user (no admin), adds a desktop shortcut and starts it.
   If Windows says *"Windows protected your PC"*: *More info → Run anyway* (PeakMeter is not code-signed yet).
4. Start PeakMeter **before** entering a dungeon (player names arrive on loading screens).

| Key | |
|---|---|
| Ctrl+Shift+D | show / hide |
| Ctrl+Shift+R | reset |
| Ctrl+Shift+L | click-through |
| Ctrl + mouse wheel | background transparency |

Language, hotkeys and more: ⚙ in the overlay footer or right-click the tray icon.

## Is it safe?

PeakMeter only **reads your own game's network traffic** (like Wireshark) and only shows what already happened or what the game tells you anyway – no radar, no hidden information, no automation. It never touches game memory, never injects anything and never sends anything to the game. Still, any third-party tool may be against the game's terms of service – use it at your own risk. PeakMeter is a community project and not affiliated with NCSOFT.

The only thing PeakMeter itself sends is the update check to `api.github.com`.

## Support

PeakMeter is free and stays free. If it helps you, a coffee on **[Ko-fi](https://ko-fi.com/peakmeter)** keeps it going. ☕
Bugs and ideas: [Issues](https://github.com/Peakmeter/PeakMeter/issues). The file `%AppData%\PeakMeter\peakmeter.log` helps (states and numbers only, no names or chat).

---

<a id="deutsch"></a>

## Deutsch

**PeakMeter** ist ein kostenloser, schlanker DPS-Meter für AION 2 (EU/NA).

- **Live-Overlay** mit DPS, Anteil und Combat Power – allein nur du, in der Gruppe nur deine Gruppe.
- **DPS / HEAL / TANK** umschaltbar, **Vergleich mit deinem letzten Versuch** und **Bestzeiten** bei Bossen.
- **Gruppe** mit Level, Gearscore und Combat Power, **Logs** aller Boss-Kämpfe.
- **Event-Timer** (Shugo, Luftangriff, Riss, Belagerung, Nahma …) und **alle 48 Weltbosse** mit sanftem Klingelton.
- **Odyle-Energie** automatisch aus dem Spiel.
- **Hinweise rechts am Bildschirmrand**, **Tickets-Fenster** mit Odyle pro Charakter, **Einstellungen** wie bei den großen Metern (aDPS, GS/CP, Namen ausblenden, Skalierung, Fokusmodus …).
- **Schont den PC**, Deutsch &amp; Englisch, Einstellungen, eigene Tasten, **automatische Updates**.

**Installieren:** [Npcap](https://npcap.com/#download) installieren → neueste ZIP unter [Releases](https://github.com/Peakmeter/PeakMeter/releases/latest) laden und entpacken → `PeakMeter installieren.cmd` doppelklicken. Bei „Der Computer wurde durch Windows geschützt“: *Weitere Informationen → Trotzdem ausführen*.

PeakMeter liest nur den Netzwerkverkehr deines eigenen Spiels – kein Speicherzugriff, nichts wird ans Spiel gesendet. Nutzung auf eigenes Risiko; kein offizielles Produkt von NCSOFT.

Gefällt dir PeakMeter? Unterstütze es auf **[Ko-fi](https://ko-fi.com/peakmeter)** ☕

---

## Build it yourself

.NET 10 SDK, then `powershell -ExecutionPolicy Bypass -File build.ps1` → `dist\PeakMeter-v<version>-win-x64.zip` (+ SHA-256).

## Credits &amp; license

Network decoding, combat tracking and game data (`src/AionMeter.Core`, `data/`) come from
[cyberbadger6969/aion2-dps-meter](https://github.com/cyberbadger6969/aion2-dps-meter) (GPL-3.0), whose data tables come from
[taengu/A2Tools-DPS-Meter](https://github.com/taengu/A2Tools-DPS-Meter) and
[Kuroukihime/AIon2-Dps-Meter](https://github.com/Kuroukihime/AIon2-Dps-Meter) (see `data/NOTICE.md`).
The interface, logs, timers, Odyle energy decoding, updater and installer are PeakMeter's own.
PeakMeter is therefore licensed under **GPL-3.0** (`LICENSE`). Game data © NCSOFT.
