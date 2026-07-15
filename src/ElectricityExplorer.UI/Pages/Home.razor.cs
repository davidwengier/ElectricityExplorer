using System.Globalization;
using ElectricityExplorer.Core.Analysis;
using ElectricityExplorer.Core.Models;
using ElectricityExplorer.Core.Nem12;
using ElectricityExplorer.Core.Storage;
using ElectricityExplorer.UI.Components.Charts;
using ElectricityExplorer.UI.Models;
using ElectricityExplorer.UI.Services;
using Microsoft.AspNetCore.Components;

namespace ElectricityExplorer.UI.Pages;

public partial class Home
{
    private const long MaximumUploadBytes = 100 * 1024 * 1024;

    private readonly Nem12Parser _parser = new();
    private readonly AnalysisSettings _settings = new();
    private readonly BatterySurvivalSettings _survivalSettings = new();
    private readonly List<BillOfferSettings> _billOffers = [BillOfferSettings.Create(1)];
    private IReadOnlyList<DatasetSummary> _summaries = [];
    private ElectricityDataset? _dataset;
    private string? _selectedNmi;
    private IReadOnlyList<SiteInterval> _profile = [];
    private EnergySimulationResult? _simulation;
    private BatterySizingResult? _sizing;
    private BatterySurvivalResult? _batterySurvival;
    private IReadOnlyList<ChartSeries> _dailyEnergySeries = [];
    private IReadOnlyList<ChartSeries> _batterySizingSeries = [];
    private IReadOnlyList<ChartSeries> _detailEnergySeries = [];
    private IReadOnlyList<ChartSeries> _batterySocSeries = [];
    private DateTime? _detailDate;
    private string? _pendingDeleteId;
    private string? _error;
    private string? _notice;
    private ApplicationUpdate? _availableUpdate;
    private string? _updateStatus;
    private string? _updateFailure;
    private string _busyMessage = "Working locally...";
    private DatasetStorageStatus _storageStatus = new(
        "Local storage",
        true,
        "NEM12 data and calculations remain on this device.");
    private bool _initializing = true;
    private bool _busy;
    private bool _checkingForUpdates;
    private bool _installingUpdate;
    private bool _showUpdateFailure;
    private ScenarioTab _activeScenarioTab = ScenarioTab.BatterySurvival;

    [Inject]
    private IDatasetStore DatasetStore { get; set; } = null!;

    [Inject]
    private INem12FilePicker FilePicker { get; set; } = null!;

    [Inject]
    private IApplicationUpdateService ApplicationUpdateService { get; set; } = null!;

    private string ApplicationNameAndVersion =>
        string.IsNullOrWhiteSpace(ApplicationUpdateService.CurrentVersion)
            ? "Electricity Explorer"
            : $"Electricity Explorer v{ApplicationUpdateService.CurrentVersion}";

    private IReadOnlyList<Nem12Channel> SelectedChannels =>
        _dataset?.Channels
            .Where(channel => string.Equals(
                channel.Nmi,
                _selectedNmi,
                StringComparison.OrdinalIgnoreCase))
            .ToArray()
        ?? [];

    private string ResultPeriodLabel =>
        _simulation is null
            ? string.Empty
            : $"{_simulation.CoveredDays:N0} day{(_simulation.CoveredDays == 1 ? string.Empty : "s")} of meter data";

    private string RecommendedBatteryText =>
        _sizing is null || _sizing.RecommendedCapacityKwh <= 0
            ? "No useful size"
            : $"{_sizing.RecommendedCapacityKwh:N0} kWh";

    private string GridImportReductionText
    {
        get
        {
            if (_simulation is null || _simulation.OriginalImportKwh <= 0)
            {
                return "0%";
            }

            var reduction = 1 - _simulation.GridImportAfterBatteryKwh / _simulation.OriginalImportKwh;
            return $"{reduction:P0}";
        }
    }

    private string BatteryDepletionText
    {
        get
        {
            if (_settings.BatteryCapacityKwh <= 0)
            {
                return "No battery selected";
            }

            return _simulation?.FirstBatteryDepletedAt is { } timestamp
                ? timestamp.ToString("dd MMM, h:mm tt", CultureInfo.CurrentCulture)
                : "Not during this period";
        }
    }

    private string FreeTariffPeriodText =>
        $"{FormatTime(_survivalSettings.FreeTariffStart)} to {FormatTime(_survivalSettings.FreeTariffEnd)}";

    private TimeOnly BatteryFullTimeValue
    {
        get => _survivalSettings.BatteryFullAt;
        set
        {
            _survivalSettings.BatteryFullAt = AlignToProfileInterval(value);
            RecalculateBatterySurvival();
        }
    }

    private int ProfileIntervalMinutes =>
        _profile.Count == 0
            ? 5
            : Math.Max(1, (int)Math.Round(_profile[0].DurationHours * 60d));

    private int ProfileIntervalSeconds => ProfileIntervalMinutes * 60;

    private string SurvivalGoalText
    {
        get
        {
            var (target, nextDay) = GetSurvivalTarget();
            return $"{FormatTime(target)}{(nextDay ? " the next day" : string.Empty)}";
        }
    }

    private string SurvivalDurationText
    {
        get
        {
            var hours = GetSurvivalTargetHours();
            var duration = TimeSpan.FromHours(hours);
            return duration.Minutes == 0
                ? $"{(int)duration.TotalHours} hours"
                : $"{(int)duration.TotalHours} hr {duration.Minutes} min";
        }
    }

    private string SurvivalTypicalOutcomeText
    {
        get
        {
            if (_batterySurvival is null || _batterySurvival.Days.Count == 0)
            {
                return "No complete days";
            }

            if (_batterySurvival.MedianRuntimeHours >= _batterySurvival.TargetRuntimeHours - 0.001)
            {
                return $"At least {FormatTime(_survivalSettings.FreeTariffStart)}";
            }

            return FormatOutcomeTime(_batterySurvival.MedianRuntimeHours);
        }
    }

    private string SurvivalWorstOutcomeText
    {
        get
        {
            var worstDay = _batterySurvival?.WorstDay;
            if (worstDay is null)
            {
                return "No complete days";
            }

            return worstDay.ReachedTarget
                ? $"At least {worstDay.TargetAt:h:mm tt}"
                : worstDay.DepletedAt?.ToString("h:mm tt", CultureInfo.CurrentCulture)
                    ?? "Unknown";
        }
    }

    private string SurvivalWorstDayText
    {
        get
        {
            var worstDay = _batterySurvival?.WorstDay;
            return worstDay is null
                ? "A complete interval window is required."
                : worstDay.ReachedTarget
                    ? "The battery reached free power on every modelled day."
                    : $"{worstDay.BatteryFullAt:dd MMM yyyy}, after {FormatDuration(worstDay.RuntimeHours)}.";
        }
    }

    private string DetailMinimumDate =>
        _profile.Count == 0
            ? string.Empty
            : _profile[0].Timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private string DetailMaximumDate =>
        _profile.Count == 0
            ? string.Empty
            : _profile[^1].Timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private string DetailDateValue =>
        _detailDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            await RefreshSummariesAsync();
            _storageStatus = await DatasetStore.GetStatusAsync();
        }
        catch (Exception exception)
        {
            _error = $"Local storage could not be opened: {exception.Message}";
            _initializing = false;
            return;
        }

        try
        {
            if (_summaries.Count > 0)
            {
                await LoadDatasetAsync(_summaries[0].Id);
            }
        }
        catch (Exception exception)
        {
            _error = $"A saved dataset could not be loaded: {exception.Message}";
        }
        finally
        {
            _initializing = false;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && ApplicationUpdateService.CanUpdate)
        {
            await CheckForUpdatesAsync(showFailure: false);
        }
    }

    private Task CheckForUpdatesManuallyAsync() =>
        CheckForUpdatesAsync(showFailure: true);

    private async Task CheckForUpdatesAsync(bool showFailure)
    {
        if (!ApplicationUpdateService.CanUpdate
            || _checkingForUpdates
            || _installingUpdate
            || _busy)
        {
            return;
        }

        _checkingForUpdates = true;
        _updateFailure = null;
        _showUpdateFailure = false;
        _updateStatus = "Checking for updates...";
        await InvokeAsync(StateHasChanged);

        try
        {
            _availableUpdate = await ApplicationUpdateService.CheckForUpdateAsync();
            _updateStatus = _availableUpdate is null
                ? "Up to date"
                : $"Version {_availableUpdate.Version} available";
        }
        catch (ApplicationUpdateException exception)
        {
            SetUpdateFailure(exception.Message, showFailure);
        }
        catch (HttpRequestException exception)
        {
            SetUpdateFailure(exception.Message, showFailure);
        }
        catch (IOException exception)
        {
            SetUpdateFailure(exception.Message, showFailure);
        }
        catch (System.Text.Json.JsonException exception)
        {
            SetUpdateFailure(exception.Message, showFailure);
        }
        catch (UnauthorizedAccessException exception)
        {
            SetUpdateFailure(exception.Message, showFailure);
        }
        catch (OperationCanceledException exception)
        {
            SetUpdateFailure(exception.Message, showFailure);
        }
        finally
        {
            _checkingForUpdates = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task InstallUpdateAsync()
    {
        if (_availableUpdate is null || _installingUpdate || _busy)
        {
            return;
        }

        _installingUpdate = true;
        _busy = true;
        _updateFailure = null;
        _showUpdateFailure = false;
        _busyMessage = "Downloading application update...";
        _updateStatus = $"Downloading version {_availableUpdate.Version}...";
        await InvokeAsync(StateHasChanged);

        try
        {
            await ApplicationUpdateService.DownloadUpdateAsync();
            _updateStatus = "Restarting to install the update...";
            await InvokeAsync(StateHasChanged);
            ApplicationUpdateService.ApplyUpdateAndRestart();
        }
        catch (ApplicationUpdateException exception)
        {
            SetUpdateFailure(exception.Message, showFailure: true);
        }
        catch (HttpRequestException exception)
        {
            SetUpdateFailure(exception.Message, showFailure: true);
        }
        catch (IOException exception)
        {
            SetUpdateFailure(exception.Message, showFailure: true);
        }
        catch (System.Text.Json.JsonException exception)
        {
            SetUpdateFailure(exception.Message, showFailure: true);
        }
        catch (UnauthorizedAccessException exception)
        {
            SetUpdateFailure(exception.Message, showFailure: true);
        }
        catch (OperationCanceledException exception)
        {
            SetUpdateFailure(exception.Message, showFailure: true);
        }
        finally
        {
            _installingUpdate = false;
            _busy = false;
            _busyMessage = "Working locally...";
            await InvokeAsync(StateHasChanged);
        }
    }

    private void SetUpdateFailure(string message, bool showFailure)
    {
        _updateFailure = message;
        _showUpdateFailure = showFailure;
        _updateStatus = "Update check unavailable";
    }

    private void DismissUpdateFailure() => _showUpdateFailure = false;

    private async Task ImportPickedFileAsync()
    {
        if (_busy || _installingUpdate)
        {
            return;
        }

        await using var file = await FilePicker.PickAsync();
        if (file is null)
        {
            return;
        }

        await ImportFileAsync(file.Name, file.Size, file.Stream);
    }

    private async Task ImportFileAsync(string fileName, long fileSize, Stream stream)
    {
        if (_busy || _installingUpdate)
        {
            return;
        }

        _busy = true;
        _error = null;
        _notice = null;

        try
        {
            await SetBusyMessageAsync("Reading NEM12 records...");

            if (fileSize > MaximumUploadBytes)
            {
                throw new InvalidOperationException("The selected file is larger than the 100 MB local import limit.");
            }

            var dataset = await _parser.ParseAsync(stream, fileName);

            await SetBusyMessageAsync($"Saving {dataset.Readings.Count:N0} readings locally...");
            await DatasetStore.SaveAsync(dataset);
            await RefreshSummariesAsync();

            _dataset = dataset;
            _selectedNmi = dataset.Nmis.FirstOrDefault();
            _pendingDeleteId = null;

            await SetBusyMessageAsync("Calculating battery and solar scenarios...");
            await TryCalculateLoadedDatasetAsync();

            _notice =
                $"Imported {dataset.Readings.Count:N0} interval readings from {fileName} and saved them locally.";
        }
        catch (Nem12ParseException exception)
        {
            _error = $"The file could not be imported. {exception.Message}";
        }
        catch (Exception exception)
        {
            _error = $"The file could not be imported: {exception.Message}";
        }
        finally
        {
            _busy = false;
            _busyMessage = "Working locally...";
        }
    }

    private async Task DatasetChangedAsync(ChangeEventArgs args)
    {
        if (_busy || _installingUpdate)
        {
            return;
        }

        var id = args.Value?.ToString();
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        _busy = true;
        _error = null;
        _notice = null;

        try
        {
            await SetBusyMessageAsync("Loading saved readings...");
            await LoadDatasetAsync(id);
        }
        catch (Exception exception)
        {
            _error = $"The saved dataset could not be loaded: {exception.Message}";
        }
        finally
        {
            _busy = false;
            _busyMessage = "Working locally...";
        }
    }

    private async Task NmiChangedAsync(ChangeEventArgs args)
    {
        if (_busy || _installingUpdate)
        {
            return;
        }

        var nmi = args.Value?.ToString();
        if (string.IsNullOrWhiteSpace(nmi))
        {
            return;
        }

        _busy = true;
        _selectedNmi = nmi;
        _error = null;

        try
        {
            await SetBusyMessageAsync("Calculating battery and solar scenarios...");
            await TryCalculateLoadedDatasetAsync();
        }
        finally
        {
            _busy = false;
            _busyMessage = "Working locally...";
        }
    }

    private async Task RunAnalysisAsync()
    {
        if (_busy || _installingUpdate)
        {
            return;
        }

        _busy = true;
        _error = null;
        _notice = null;

        try
        {
            await SetBusyMessageAsync("Calculating battery and solar scenarios...");
            await CalculateAnalysisAsync(saveDataset: true);
            _notice = "Analysis updated and the channel mapping was saved locally.";
        }
        catch (Exception exception)
        {
            _error = $"The scenario could not be calculated: {exception.Message}";
        }
        finally
        {
            _busy = false;
            _busyMessage = "Working locally...";
        }
    }

    private async Task CalculateAnalysisAsync(bool saveDataset)
    {
        if (_dataset is null || string.IsNullOrWhiteSpace(_selectedNmi))
        {
            ResetAnalysis();
            return;
        }

        if (saveDataset)
        {
            await DatasetStore.SaveAsync(_dataset);
        }

        await Task.Yield();

        _profile = SiteProfile.Build(_dataset, _selectedNmi);
        if (_profile.Count == 0)
        {
            throw new InvalidOperationException("The mapped channels contain no interval readings.");
        }

        var options = _settings.ToSimulationOptions();
        _simulation = EnergySimulator.Run(_profile, options);
        _sizing = BatterySizer.Compare(
            _profile,
            options,
            _settings.MaximumComparedBatteryKwh);
        RecalculateBatterySurvival();

        BuildDailyEnergySeries();
        BuildBatterySizingSeries();
        SelectDefaultDetailDate();
        BuildDetailSeries();
    }

    private async Task LoadDatasetAsync(string id)
    {
        var dataset = await DatasetStore.GetAsync(id)
            ?? throw new InvalidOperationException("The selected dataset no longer exists.");

        _dataset = dataset;
        _selectedNmi = dataset.Nmis.FirstOrDefault();
        _pendingDeleteId = null;
        await TryCalculateLoadedDatasetAsync();
    }

    private async Task<bool> TryCalculateLoadedDatasetAsync()
    {
        try
        {
            await CalculateAnalysisAsync(saveDataset: false);
            return true;
        }
        catch (Exception exception)
        {
            ResetAnalysis();
            _error =
                $"The data is loaded, but it cannot be analysed yet: {exception.Message}";
            return false;
        }
    }

    private async Task RefreshSummariesAsync()
    {
        _summaries = await DatasetStore.GetSummariesAsync();
    }

    private void SetChannelDirection(Nem12Channel channel, ChangeEventArgs args)
    {
        if (Enum.TryParse<EnergyFlowDirection>(args.Value?.ToString(), out var direction))
        {
            channel.Direction = direction;
        }
    }

    private void SelectScenarioTab(ScenarioTab tab)
    {
        _activeScenarioTab = tab;
    }

    private void AdditionalBatteryChanged(ChangeEventArgs args)
    {
        _survivalSettings.AdditionalBatteryCapacityKwh = ReadBoundedDouble(
            args,
            _survivalSettings.AdditionalBatteryCapacityKwh,
            0,
            BatterySurvivalSettings.MaximumAdditionalBatteryKwh);
        RecalculateBatterySurvival();
    }

    private void AdditionalSolarChanged(ChangeEventArgs args)
    {
        _survivalSettings.AdditionalSolarKw = ReadBoundedDouble(
            args,
            _survivalSettings.AdditionalSolarKw,
            0,
            BatterySurvivalSettings.MaximumAdditionalSolarKw);
        RecalculateBatterySurvival();
    }

    private void FreeTariffStartChanged(ChangeEventArgs args)
    {
        _survivalSettings.FreeTariffStartHour = ReadBoundedDouble(
            args,
            _survivalSettings.FreeTariffStartHour,
            0,
            _survivalSettings.FreeTariffEndHour - 0.5);
        RecalculateBatterySurvival();
    }

    private void FreeTariffEndChanged(ChangeEventArgs args)
    {
        _survivalSettings.FreeTariffEndHour = ReadBoundedDouble(
            args,
            _survivalSettings.FreeTariffEndHour,
            _survivalSettings.FreeTariffStartHour + 0.5,
            23.5);
        RecalculateBatterySurvival();
    }

    private void RecalculateBatterySurvival()
    {
        _batterySurvival = _profile.Count == 0
            ? null
            : BatterySurvivalAnalyzer.Run(_profile, _survivalSettings.ToOptions());
    }

    private void RequestDelete()
    {
        _pendingDeleteId = _dataset?.Id;
    }

    private void CancelDelete()
    {
        _pendingDeleteId = null;
    }

    private async Task DeleteSelectedDatasetAsync()
    {
        if (_dataset is null || _busy || _installingUpdate)
        {
            return;
        }

        _busy = true;
        _error = null;
        _notice = null;

        try
        {
            await SetBusyMessageAsync("Deleting local dataset...");
            var deletedName = _dataset.Name;
            await DatasetStore.DeleteAsync(_dataset.Id);
            await RefreshSummariesAsync();

            _dataset = null;
            _selectedNmi = null;
            _pendingDeleteId = null;
            ResetAnalysis();

            if (_summaries.Count > 0)
            {
                await LoadDatasetAsync(_summaries[0].Id);
            }

            _notice = $"Deleted {deletedName} from local storage.";
        }
        catch (Exception exception)
        {
            _error = $"The local dataset could not be deleted: {exception.Message}";
        }
        finally
        {
            _busy = false;
            _busyMessage = "Working locally...";
        }
    }

    private void DetailDateChanged(ChangeEventArgs args)
    {
        if (DateTime.TryParseExact(
                args.Value?.ToString(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            _detailDate = date.Date;
            BuildDetailSeries();
        }
    }

    private void BuildDailyEnergySeries()
    {
        if (_simulation is null)
        {
            _dailyEnergySeries = [];
            return;
        }

        var days = _simulation.Intervals
            .GroupBy(interval => interval.Timestamp.Date)
            .Select(group => new
            {
                Date = group.Key,
                OriginalImport = group.Sum(interval => interval.OriginalImportKwh),
                FinalImport = group.Sum(interval => interval.GridImportAfterBatteryKwh),
                FinalExport = group.Sum(interval => interval.GridExportAfterBatteryKwh)
            })
            .OrderBy(day => day.Date)
            .ToArray();

        _dailyEnergySeries =
        [
            new ChartSeries(
                "Original import",
                "#78839a",
                days.Select(day => EnergyPoint(
                    day.Date,
                    day.OriginalImport,
                    "Original import")).ToArray()),
            new ChartSeries(
                "Import after changes",
                "#0f766e",
                days.Select(day => EnergyPoint(
                    day.Date,
                    day.FinalImport,
                    "Import after changes")).ToArray()),
            new ChartSeries(
                "Export after changes",
                "#e29a3b",
                days.Select(day => EnergyPoint(
                    day.Date,
                    day.FinalExport,
                    "Export after changes")).ToArray(),
                "8 7")
        ];
    }

    private void BuildBatterySizingSeries()
    {
        if (_sizing is null)
        {
            _batterySizingSeries = [];
            return;
        }

        var comparison = new ChartSeries(
            "Battery savings",
            "#0f766e",
            _sizing.Points
                .Select(point => new ChartPoint(
                    point.CapacityKwh,
                    point.BatterySavings,
                    $"{point.CapacityKwh:N0} kWh: {FormatSignedCurrency(point.BatterySavings)}"))
                .ToArray());

        if (_sizing.RecommendedCapacityKwh <= 0)
        {
            _batterySizingSeries = [comparison];
            return;
        }

        var recommended = _sizing.Points.MinBy(point =>
            Math.Abs(point.CapacityKwh - _sizing.RecommendedCapacityKwh))!;

        _batterySizingSeries =
        [
            comparison,
            new ChartSeries(
                "Suggested size",
                "#e29a3b",
                [
                    new ChartPoint(
                        recommended.CapacityKwh,
                        recommended.BatterySavings,
                        $"Suggested: {recommended.CapacityKwh:N0} kWh, {FormatSignedCurrency(recommended.BatterySavings)}")
                ])
        ];
    }

    private void SelectDefaultDetailDate()
    {
        if (_simulation is null || _simulation.Intervals.Count == 0)
        {
            _detailDate = null;
            return;
        }

        var minimumDate = _simulation.Intervals[0].Timestamp.Date;
        var maximumDate = _simulation.Intervals[^1].Timestamp.Date;
        if (_detailDate >= minimumDate && _detailDate <= maximumDate)
        {
            return;
        }

        _detailDate = _simulation.FirstBatteryDepletedAt?.Date
            ?? _simulation.Intervals
                .GroupBy(interval => interval.Timestamp.Date)
                .MaxBy(group => group.Sum(interval => interval.OriginalImportKwh))!
                .Key;
    }

    private void BuildDetailSeries()
    {
        if (_simulation is null || _detailDate is null)
        {
            _detailEnergySeries = [];
            _batterySocSeries = [];
            return;
        }

        var intervals = _simulation.Intervals
            .Where(interval => interval.Timestamp.Date == _detailDate.Value.Date)
            .ToArray();

        _detailEnergySeries =
        [
            new ChartSeries(
                "Original import",
                "#78839a",
                intervals.Select(interval => EnergyPoint(
                    interval.Timestamp,
                    interval.OriginalImportKwh,
                    "Original import")).ToArray()),
            new ChartSeries(
                "Import after battery",
                "#0f766e",
                intervals.Select(interval => EnergyPoint(
                    interval.Timestamp,
                    interval.GridImportAfterBatteryKwh,
                    "Import after battery")).ToArray()),
            new ChartSeries(
                "Additional solar",
                "#e29a3b",
                intervals.Select(interval => EnergyPoint(
                    interval.Timestamp,
                    interval.AdditionalSolarKwh,
                    "Additional solar")).ToArray(),
                "8 7")
        ];

        _batterySocSeries =
        [
            new ChartSeries(
                "State of charge",
                "#6d4ed1",
                intervals.Select(interval => new ChartPoint(
                    interval.Timestamp.Ticks,
                    interval.BatterySocPercent,
                    $"{interval.Timestamp:h:mm tt}: {interval.BatterySocPercent:N0}%")).ToArray())
        ];
    }

    private static ChartPoint EnergyPoint(DateTime timestamp, double value, string label) =>
        new(
            timestamp.Ticks,
            value,
            $"{timestamp:dd MMM yyyy, h:mm tt} - {label}: {value:N2} kWh");

    private static string FormatEnergy(double value) => $"{value:N1} kWh";

    private static string FormatTime(TimeOnly time) =>
        new DateTime(2000, 1, 1)
            .Add(time.ToTimeSpan())
            .ToString("h:mm tt", CultureInfo.CurrentCulture);

    private string FormatOutcomeTime(double runtimeHours) =>
        new DateTime(2000, 1, 1)
            .Add(_survivalSettings.BatteryFullAt.ToTimeSpan())
            .AddHours(runtimeHours)
            .ToString("h:mm tt", CultureInfo.CurrentCulture);

    private static string FormatDuration(double hours)
    {
        var duration = TimeSpan.FromHours(hours);
        return duration.Minutes == 0
            ? $"{(int)duration.TotalHours} hr"
            : $"{(int)duration.TotalHours} hr {duration.Minutes} min";
    }

    private static double ReadBoundedDouble(
        ChangeEventArgs args,
        double fallback,
        double minimum,
        double maximum)
    {
        return double.TryParse(
                args.Value?.ToString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var value)
            ? Math.Clamp(value, minimum, maximum)
            : fallback;
    }

    private (TimeOnly Target, bool NextDay) GetSurvivalTarget()
    {
        var nextDay = _survivalSettings.FreeTariffStart <= _survivalSettings.BatteryFullAt;
        return (_survivalSettings.FreeTariffStart, nextDay);
    }

    private double GetSurvivalTargetHours()
    {
        var start = new DateTime(2000, 1, 1)
            .Add(_survivalSettings.BatteryFullAt.ToTimeSpan());
        var target = new DateTime(2000, 1, 1)
            .Add(_survivalSettings.FreeTariffStart.ToTimeSpan());

        if (target <= start)
        {
            target = target.AddDays(1);
        }

        return (target - start).TotalHours;
    }

    private TimeOnly AlignToProfileInterval(TimeOnly time)
    {
        var intervalMinutes = ProfileIntervalMinutes;
        var totalMinutes = time.Hour * 60 + time.Minute;
        var alignedMinutes = (int)Math.Round(totalMinutes / (double)intervalMinutes)
            * intervalMinutes;
        alignedMinutes %= 24 * 60;

        return TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(alignedMinutes));
    }

    private static string FormatSignedCurrency(double value) =>
        value >= 0
            ? $"+${value:N0}"
            : $"-${Math.Abs(value):N0}";

    private async Task SetBusyMessageAsync(string message)
    {
        _busyMessage = message;
        await InvokeAsync(StateHasChanged);
        await Task.Delay(10);
    }

    private void ResetAnalysis()
    {
        _profile = [];
        _simulation = null;
        _sizing = null;
        _batterySurvival = null;
        _detailDate = null;
        _dailyEnergySeries = [];
        _batterySizingSeries = [];
        _detailEnergySeries = [];
        _batterySocSeries = [];
    }

    private void DismissError() => _error = null;

    private void DismissNotice() => _notice = null;
}
