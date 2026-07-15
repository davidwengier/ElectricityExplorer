# Electricity Explorer repository instructions

## Product constraints

Electricity Explorer is a Windows-only, local-only Blazor Hybrid desktop application. It imports NEM12 CSV files, stores them in SQLite, and models the recorded site data with hypothetical battery, solar, and tariff changes.

- Do not introduce a server, browser-hosted WASM application, cloud storage, authentication, syncing, or telemetry unless explicitly requested.
- Preserve user data locally. The default database is `%LocalAppData%\ElectricityExplorer\electricity-explorer.db`; window state is `%LocalAppData%\ElectricityExplorer\window-state.json`.
- The packaged application is framework-dependent. Its VeloPack installer checks for the .NET 10 Desktop Runtime and Microsoft Edge WebView2 Evergreen Runtime and offers to install missing prerequisites.

## Toolchain and commands

Run commands from the repository root on Windows. `global.json` pins the .NET SDK to `10.0.301`.

```powershell
# Run the desktop application
dotnet run --project .\src\ElectricityExplorer.Desktop

# Build the complete solution
dotnet build .\ElectricityExplorer.slnx

# Run all Core and SQLite tests
dotnet test .\ElectricityExplorer.slnx

# Run one test class
dotnet test .\tests\ElectricityExplorer.Core.Tests\ElectricityExplorer.Core.Tests.csproj --filter "FullyQualifiedName~ElectricityExplorer.Core.Tests.BillEstimatorTests"

# Run one test
dotnet test .\tests\ElectricityExplorer.Core.Tests\ElectricityExplorer.Core.Tests.csproj --filter "FullyQualifiedName=ElectricityExplorer.Core.Tests.BillEstimatorTests.Calculate_GroupsUsageSupplyAndFeedInByMonth"
```

Publish the same single-file Windows executable produced by CI with:

```powershell
dotnet publish .\src\ElectricityExplorer.Desktop\ElectricityExplorer.Desktop.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -p:PublishDir=.\artifacts\publish\
```

Restore the pinned VeloPack CLI with `dotnet tool restore`, then package that publish output with `dotnet vpk pack`. `.github\workflows\windows-release.yml` runs tests, publishes, creates a VeloPack installer/update feed, and publishes an immutable versioned GitHub Release after every successful push to `main`.

## Project boundaries

The dependency direction is Core <- UI/Storage <- Desktop:

- `ElectricityExplorer.Core` contains domain models, NEM12 parsing, storage abstractions, and all calculations. It must remain independent of WinForms, Razor, JavaScript, and SQLite.
- `ElectricityExplorer.Storage.Sqlite` implements Core's `IDatasetStore`. It owns schema creation and serialized SQLite access.
- `ElectricityExplorer.UI` contains reusable Razor components, mutable UI settings, SVG charts, and the small JavaScript bridge used for chart navigation.
- `ElectricityExplorer.Desktop` is the composition root and WinForms host. It supplies the native file picker, SQLite store, window persistence, VeloPack updater, and embedded WebView content.
- Tests mirror the pure Core and SQLite projects. There is no server project.

The main data flow is:

1. `INem12FilePicker` supplies a stream and file name to `Nem12Parser`.
2. The parser produces an `ElectricityDataset`, which is persisted through `IDatasetStore`.
3. User-editable `ChannelMapping` values classify channels as import, export, or ignored.
4. `SiteProfile.Build` is the boundary between raw NEM12 data and analysis. It aggregates selected channels into chronological `SiteInterval` values.
5. Core analyzers consume `SiteProfile`; `Home.razor.cs` coordinates state and turns results into chart/table models.

Do not calculate scenarios directly from raw readings or inside Razor markup. Add reusable calculations to Core, validate their option record, and test them there.

## C# and component conventions

- Keep one explicit C# type per file, including records, enums, and exceptions.
- Use file-scoped namespaces, nullable reference types, implicit usings, collection expressions, and the existing modern C# style.
- Warnings are treated as errors. Keep builds warning-free rather than suppressing warnings broadly.
- Prefer immutable records for Core options and result values. Put range and relationship checks in an explicit `Validate()` method and invoke it at the analyzer boundary.
- UI settings are mutable classes because they are data-bound. Convert them to validated Core options with `ToOptions()`; do not make Core options depend on UI state.
- Keep energy quantities as `double` kWh/kW values. Use `decimal` for tariff rates and monetary calculations.
- Reuse `TimeWindow.Contains` for daily and overnight time windows instead of duplicating time-of-day logic.
- Keep `Home.razor` focused on markup and `Home.razor.cs` focused on application state/orchestration. Extract substantial scenario controls into `Components` rather than growing the page indefinitely.
- Shared styling lives in `ElectricityExplorer.UI\wwwroot\css\app.css`. Match the existing class-based visual language.
- Charts are custom SVG components. `ChartMouseNavigator.razor` and `wwwroot\js\chartNavigator.js` form one interop contract for wheel zoom, drag pan, and overview selection; update both sides together and preserve connect/disconnect disposal.

## NEM12 and modelling invariants

- Meter timestamps represent interval starts in local meter time and use `DateTimeKind.Unspecified`. Do not convert them to UTC.
- Normalize Wh, kWh, and MWh readings to kWh during parsing.
- Files missing `100` or `900` records remain usable with warnings. Accept but ignore `400` and `500` records. Preserve warnings for recoverable irregularities rather than rejecting an otherwise usable file.
- A repeated daily `300` record for the same channel and date replaces the earlier readings and emits a warning.
- E-prefixed channels default to import and B-prefixed channels default to export, but mappings remain user-editable.
- The imported history is the baseline and may already include solar or a battery. Model additions against recorded grid import/export rather than trying to reconstruct household load that is not present in NEM12.
- Battery-survival additions use 80% of nominal added battery capacity. Free-period imports do not drain the modelled battery, and existing solar export plus modelled extra solar may charge it.
- The bill estimator prices each interval by its start time. One default import rate plus at most two non-overlapping timed overrides gives three rates total; a configured free period overrides paid rates. Windows repeat daily and may cross midnight.
- Daily supply charges apply once per represented calendar date. Feed-in credits use all recorded exports. Discounts and demand charges are deliberately not modelled.

When changing parser or analysis behavior, add focused xUnit coverage for boundary conditions such as midnight-spanning windows, duplicate records, unit conversion, partial months/days, missing records, and channel direction.

## SQLite and desktop packaging

- Keep persistence behind `IDatasetStore`; UI and analyzers must not reference `Microsoft.Data.Sqlite`.
- `SqliteDatasetStore` serializes access with a semaphore, enables foreign keys/WAL mode, and replaces a dataset atomically in a transaction. Preserve those guarantees when changing the schema or save path.
- Tests must use temporary database files and clean them up. Never point tests at the user's LocalAppData database. `ELECTRICITY_EXPLORER_DATABASE` is available as a diagnostic/test override.
- `EmbeddedBlazorWebView` serves manifest resources so the published application can be one executable.
- `ElectricityExplorer.Desktop.csproj` explicitly embeds `index.html`, `app.css`, `chartNavigator.js`, and the generated Blazor WebView assets. When adding any runtime UI asset, also add it to this embedded-resource list; otherwise it can work from source but be missing from the released EXE.
- The VeloPack package ID is `DavidWengier.ElectricityExplorer`, intentionally distinct from the `%LocalAppData%\ElectricityExplorer` data directory so install, update, and uninstall operations cannot remove user data.
- The `RemoveSingleFilePublishExtras` target intentionally leaves one `ElectricityExplorer.exe` as the VeloPack package input. Runtime release assets belong in the VeloPack output directory rather than beside the published application executable.
