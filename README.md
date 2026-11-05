# Waktu Solat

A minimal Malaysian prayer-times widget for Windows 10/11.

- One small `.exe`, about 200 KB. No install, no admin rights, no Python. It uses .NET Framework 4.8, which is already part of Windows.
- **Official JAKIM e-Solat times** for every zone. It downloads a whole year at once and caches it, so it keeps working offline.

## Download
Get `WaktuSolat.exe` from [Releases](../../releases/latest). Put it somewhere permanent, e.g. `Documents\WaktuSolat\`, and run it. If SmartScreen appears: **More info → Run anyway** (the exe isn't code-signed).

## What it does

**Widget**
- A countdown to the next waktu.
- Today's five waktu, with the current one highlighted.
- Click the circle next to a waktu to tick it **✓ dah solat**.
- Resizable: drag any edge. Make it wide and it becomes a horizontal bar.

**Popups**, using Windows' own notification sounds:
- **Masuk waktu:** when each waktu begins.
- **Peringatan:** 30 minutes before the **current** waktu ends, unless you've ticked it. Subuh ends at Syuruk, Zohor at Asar, Asar at Maghrib, Maghrib at Isyak, and Isyak at the next Subuh.
- **Dhuha:** a small notice when Dhuha starts, and another before it ends.

**How the popups behave**
- They never steal focus.
- They're held back while you're presenting or running a fullscreen app.
- They still show when Windows notifications are disabled by IT.

**Location and settings**
- **Auto zone** from Windows Location, matched to a JAKIM zone on your PC.
- **Tema:** follow Windows, Gelap or Terang, plus 5 accent colours.
- **Starts with Windows**; turn it off in the menu.
- **Update check** once a week. It shows a small "Versi X tersedia" link and never installs anything by itself.

Right-click the widget or the tray icon for the menu: Zon, Peringatan, Bunyi, Tema, Susun atur, and so on. The location and hide buttons appear when you hover over the widget.

If the footer says **"≈ Anggaran"**, JAKIM data couldn't be downloaded yet and the times are calculated. Don't rely on those for Subuh. The menu shows the reason.

## Security & privacy
- **Network:** HTTPS only, to two places:
  - `e-solat.gov.my` for prayer times.
  - `api.github.com`, once a week, to read this repo's latest release number (can be turned off).
- **Connections:** certificates are always checked, redirects are re-checked against the allow-list, and responses are size- and time-limited and validated.
- **Location:** read through Windows, matched to a zone in memory, then discarded. Only the zone code is saved.
- **Files:** `%APPDATA%\WaktuSolat\` (settings, cache, ticks). The only registry write is `HKCU\…\Run\WaktuSolat`, for autostart.
- **No tracking:** no telemetry, no accounts, no tokens, no admin rights.

## How Dhuha is timed
- **Start = Syuruk + 25 minutes.** This comes from Pejabat Mufti Wilayah Persekutuan's timetables, which cite JAKIM's Almanak Falak Syarie: *"Awal waktu Dhuha: 25 minit selepas waktu Syuruk"*. Syuruk to Dhuha is *waktu tahrim* (Mufti Perak). Perak's own timetables use about 15 minutes.
- **End = 10 minutes before Zohor.** Dhuha ends at istiwa', just before Zohor. No official Malaysian minute figure was found. A Jabatan Mufti Selangor (JUFFAS) study measured istiwa' at about 1–2 minutes, so the 10 minutes is a **precaution, not an official JAKIM figure**.

## Design
The design follows Windows 11 (Fluent) guidance:
- The type ramp: Caption 12, Body 14, Title Large 40.
- A 4 px spacing grid.
- WinUI surface and text colours.
- Accent used sparingly.
- WCAG 2.2 AA contrast for every theme.
- Fluent motion timings, respecting the Windows "animation effects" setting.

## Credits
- Prayer times: [JAKIM e-Solat](https://www.e-solat.gov.my).
- Zone boundaries: [mptwaktusolat/malaysia.geojson](https://github.com/mptwaktusolat/malaysia.geojson). These are district polygons with JAKIM codes, simplified to about 200 m. Kinta, Kerian and Hilir Perak were corrected to JAKIM's zone list. The source repository has no licence file; credit is given here, and the data will be removed on request.
- Six special zones can't be detected from district boundaries; pick them in the menu. They are Gunung Jerai, Bukit Tinggi, Rompin (PHG07), Bukit Larut, Gunung Kinabalu and Kg Patarikan.

## Build
```
dotnet build -c Release          # → bin/Release/net48/WaktuSolat.exe
cd tests && dotnet run -c Release
```
