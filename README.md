# Electricity Explorer

Electricity Explorer is a Windows desktop application for analysing Australian
NEM12 interval meter data. It uses Blazor Hybrid inside a WinForms host, so the
Razor interface runs in-process on native .NET. There is
no server, account, authentication, backup or sync.

## Features

- Imports NEM12 `100`, `200` and `300` records from CSV files up to 100 MB.
- Accepts retailer exports that omit the standard `100` header or `900` trailer,
  with import notes explaining the missing envelope records.
- Stores desktop datasets in a local SQLite database.
- Supports multiple NMIs and configurable import/export channel mapping.
- Replays each complete historical day to show when a full battery would run out
  before the next configurable free-tariff period.
- Provides sliders for additional battery capacity and solar, with successful
  days capped at the free-power target on the chart.
- Supports mouse-wheel chart zooming, drag-to-pan, and a full-history overview
  brush for selecting and resizing the visible range.
- Simulates battery capacity, power, reserve, initial charge and efficiency.
- Estimates the effect of an additional solar array.
- Compares battery sizes and identifies a diminishing-returns recommendation.
- Charts daily grid energy, interval energy, battery state of charge and battery
  size savings.

## Run the desktop app

The repository uses the .NET 10 SDK selected by `global.json`.

```powershell
dotnet run --project src\ElectricityExplorer.Desktop
```

The SQLite database is stored at
`%LocalAppData%\ElectricityExplorer\electricity-explorer.db`.

## Install the Windows release

1. Download `ElectricityExplorer.exe` from the
   [`latest` GitHub Release](https://github.com/davidwengier/ElectricityExplorer/releases/tag/latest).
2. Install the
   [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
   for Windows x64 if the app prompts for it.
3. Install the
   [Microsoft Edge WebView2 Evergreen Runtime](https://go.microsoft.com/fwlink/p/?LinkId=2124703).
4. Run `ElectricityExplorer.exe`.

Windows 11 and most updated Windows 10 installations already include WebView2,
but Windows Sandbox may require it to be installed explicitly.

## Test and build

```powershell
dotnet test ElectricityExplorer.slnx
dotnet build ElectricityExplorer.slnx
```

## Publish the desktop app

```powershell
dotnet publish src\ElectricityExplorer.Desktop -c Release -r win-x64 --self-contained false `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The output is written beneath
`src\ElectricityExplorer.Desktop\bin\Release\net10.0-windows10.0.17763.0\win-x64\publish`.
It contains a single `ElectricityExplorer.exe` and requires the .NET 10 Desktop
Runtime and Microsoft Edge WebView2 Runtime described above.

Every push to `main` also runs the `Publish Windows release` GitHub Actions
workflow. After its tests pass, the workflow updates the
[`latest` GitHub Release](https://github.com/davidwengier/ElectricityExplorer/releases/tag/latest)
with the single-file Windows executable.

## Project structure

- `ElectricityExplorer.Core` contains NEM12 parsing and energy simulation.
- `ElectricityExplorer.UI` contains the shared Razor pages, controls and charts.
- `ElectricityExplorer.Storage.Sqlite` contains native desktop persistence.
- `ElectricityExplorer.Desktop` is the WinForms Blazor Hybrid host.

## Modelling assumptions

- E-prefixed NMI suffixes are initially treated as grid import and B-prefixed
  suffixes as grid export. The mapping can be changed before analysis.
- A battery charges only from interval surplus and discharges only to reduce
  interval grid imports in the detailed analysis. Grid tariff arbitrage is not
  modelled there.
- The battery-survival scenario treats 80% of the entered nominal battery size
  as usable, starts it full at the selected time, and does not apply an inverter
  power limit or conversion losses. Imports during the configured free tariff
  are not taken from the battery.
- Existing solar and battery behaviour is already reflected in NEM12 grid
  import and export. The survival scenario models only the battery and solar
  added on top of that recorded baseline.
- Additional solar is an estimate. It uses a daylight-shaped profile totalling
  the configured average daily yield; it is not a location-specific irradiance
  forecast.
- The suggested battery is the smallest tested capacity that reaches 90% of the
  battery-only savings available at the configured maximum comparison size. It
  is a diminishing-returns guide, not a quote or financial recommendation.
- Desktop data remains only in the local SQLite database. The app deliberately
  provides no remote backup or synchronisation.
