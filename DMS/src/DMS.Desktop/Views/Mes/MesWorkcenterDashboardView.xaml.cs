using DMS.Desktop.Logging;
using DMS.Desktop.UI;
using DMS.Integration.Mes.Database;
using DMS.Integration.Mes.Reporting;
using DMS.Integration.Mes.Workcenters;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.IO;

namespace DMS.Desktop.Views.Mes;

public partial class MesWorkcenterDashboardView : UserControl
{
    private readonly string _configurationRootPath;
    private readonly DmsLogger _logger;
    private readonly string _user;
    private readonly string _workcenterCode;
    private readonly Func<string, string> _translate;
    private readonly MesDatabaseSettingsService _settingsService = new();
    private readonly DispatcherTimer _timer;

    private MesDatabaseConnectionSettings _settings = new();
    private MesWorkcenterDashboardService? _service;
    private MesWorkcenterDashboardSnapshot? _snapshot;
    private bool _loaded;
    private bool _refreshing;

    public MesWorkcenterDashboardView(
        string configurationRootPath,
        DmsLogger logger,
        string user,
        string workcenterCode,
        Func<string, string>? translate = null)
    {
        InitializeComponent();

        _configurationRootPath = configurationRootPath
            ?? throw new ArgumentNullException(nameof(configurationRootPath));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _user = user ?? string.Empty;
        _workcenterCode = workcenterCode?.Trim().ToUpperInvariant() ?? string.Empty;
        _translate = translate ?? (key => key);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += Timer_Tick;

        ApplyLocalization();
        Loaded += View_Loaded;
        Unloaded += View_Unloaded;
    }

    private string T(string key, string fallback)
    {
        var translated = _translate(key);
        return string.IsNullOrWhiteSpace(translated)
               || string.Equals(translated, key, StringComparison.Ordinal)
               || string.Equals(translated, $"[[{key}]]", StringComparison.Ordinal)
            ? fallback
            : translated;
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text =
            $"MESWC – {_workcenterCode}";

        TxtSubtitle.Text =
            T(
                "MESWC.Subtitle",
                "Živý profil pracoviště – směna, zakázka, operátoři, OEE a výkon.");

        BtnRefresh.Content =
            T(
                "MESWC.Refresh",
                "Obnovit");

        TxtShiftPanelTitle.Text =
            T(
                "MESWC.Panel.Shift",
                "Směna");

        TxtOrderPanelTitle.Text =
            T(
                "MESWC.Panel.Order",
                "Zakázka");

        LblShift.Text =
            T(
                "MESWC.Shift",
                "Název směny");

        LblShiftPeriod.Text =
            T(
                "MESWC.ShiftPeriod",
                "Profil směny");

        LblState.Text =
            T(
                "MESWC.State",
                "Aktuální stav");

        LblOrder.Text =
            T(
                "MESWC.Order",
                "Zakázka");

        LblArticle.Text =
            T(
                "MESWC.Article",
                "Artikl");

        LblSapNumber.Text =
            T(
                "MESWC.SapNumber",
                "SAP číslo");

        LblOperation.Text =
            T(
                "MESWC.Operation",
                "Operace");

        LblAveragePerformance.Text =
            T(
                "MESWC.AveragePerformance",
                "Průměrný aktuální výkon");

        LblGross.Text =
            T(
                "MESWC.Gross",
                "Hrubého");

        LblNet.Text =
            T(
                "MESWC.Net",
                "Čistého");

        LblShiftScrap.Text =
            T(
                "MESWC.ShiftScrap",
                "Odpad směny");

        LblExpected.Text =
            T(
                "MESWC.Expected",
                "Má být vyrobeno");

        LblCounterScrapProduction.Text =
            T(
                "MESWC.Counter.ScrapProduction",
                "Odpad produkce");

        LblCounterScrapGlass.Text =
            T(
                "MESWC.Counter.ScrapGlass",
                "Odpad sklo");

        LblCounterWashedBottles.Text =
            T(
                "MESWC.Counter.WashedBottles",
                "Myté flakony");

        LblPlannedPerformance.Text =
            T(
                "MESWC.PlannedPerformance",
                "Plánovaný výkon");

        LblOrderSize.Text =
            T(
                "MESWC.OrderSize",
                "Velikost zakázky");

        LblOrderFinished.Text =
            T(
                "MESWC.OrderFinished",
                "Celkem vyrobeno na zakázku");

        LblOrderScrap.Text =
            T(
                "MESWC.OrderScrap",
                "Celkový odpad zakázky");

        LblOrderRemaining.Text =
            T(
                "MESWC.OrderRemaining",
                "Zbývá do zakázky");

        LblOrderProgress.Text =
            T(
                "MESWC.OrderProgress",
                "% splnění zakázky");

        LblCounterDevelopment.Text =
            T(
                "MESWC.Counter.Development",
                "Odpad oddělení vývoje");

        LblCounterQuality.Text =
            T(
                "MESWC.Counter.Quality",
                "Odpad oddělení kvality");

        LblCounterSetup.Text =
            T(
                "MESWC.Counter.Setup",
                "Odpad seřizování");

        LblCounterTransport.Text =
            T(
                "MESWC.Counter.Transport",
                "Odpad transport / logistika");

        TxtOeeTitle.Text =
            T(
                "MESWC.Oee",
                "OEE – aktuální směna");

        TxtStatesTitle.Text =
            T(
                "MESWC.StateSummary",
                "Stavy / prostoje – aktuální směna");

        TxtOperatorsTitle.Text =
            T(
                "MESWC.Operators",
                "Aktuálně přihlášení operátoři");

        TxtGraphTitle.Text =
            T(
                "MESWC.PerformanceGraph",
                "Strojní takt a stavy – aktuální směna");

        LblAvailability.Text =
            T(
                "MESWC.Availability",
                "Dostupnost");

        LblPerformance.Text =
            T(
                "MESWC.Performance",
                "Výkon");

        LblQuality.Text =
            T(
                "MESWC.Quality",
                "Kvalita");

        ColOperatorLogin.Header =
            T(
                "MESWC.Column.LoginTime",
                "Čas přihlášení");

        ColOperatorName.Header =
            T(
                "MESWC.Column.Personnel",
                "Jméno");

        ColStateName.Header =
            T(
                "MESWC.Column.State",
                "Stav");

        ColStateCount.Header =
            T(
                "MESWC.Column.Count",
                "Počet");

        ColStateDuration.Header =
            T(
                "MESWC.Column.Duration",
                "Doba");
    }

    private async void View_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;

        try
        {
            var settingsPath = ResolveMesDatabaseSettingsPath(_configurationRootPath);
            _settings = _settingsService.Load(settingsPath);

            if (!_settings.IsEnabled)
            {
                TxtStatus.Text = T("MESWC.Disabled", "MES SQL připojení je v MESSET vypnuté.");
                return;
            }

            _service = new MesWorkcenterDashboardService(_settings);
            await RefreshAsync(showDialogOnError: true, logAction: true);
            _timer.Start();
        }
        catch (Exception ex)
        {
            HandleError("MESWC initialization failed.", ex, true);
        }
    }

    private void View_Unloaded(object sender, RoutedEventArgs e) => _timer.Stop();

    private async void Timer_Tick(object? sender, EventArgs e) =>
        await RefreshAsync(showDialogOnError: false, logAction: false);

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e) =>
        await RefreshAsync(showDialogOnError: true, logAction: true);

    private async Task RefreshAsync(bool showDialogOnError, bool logAction)
    {
        if (_refreshing || _service is null) return;
        _refreshing = true;

        try
        {
            TxtStatus.Text = T("MESWC.Loading", "Načítám aktuální data pracoviště...");
            var snapshot = await _service.GetDashboardAsync(_workcenterCode);
            _snapshot = snapshot;
            ApplySnapshot(snapshot);

            if (logAction)
            {
                _logger.AdminAction(
                    "MESWC",
                    "LoadWorkcenterDashboard",
                    _user,
                    $"Workcenter={_workcenterCode}; Order={snapshot.ActiveOrder?.OrderCode}; Operators={snapshot.Operators.Count}; AssignedOrders={snapshot.AssignedOrders.Count}");
            }

            TxtStatus.Text = string.Format(
                CultureInfo.CurrentCulture,
                T("MESWC.Loaded", "Načteno {0:dd.MM.yyyy HH:mm:ss}."),
                snapshot.LoadedAt);
        }
        catch (Exception ex)
        {
            HandleError("MESWC refresh failed.", ex, showDialogOnError);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void HandleError(string technicalMessage, Exception ex, bool showDialog)
    {
        _logger.Error(technicalMessage, ex);
        TxtStatus.Text = ex.Message;

        if (showDialog)
        {
            DmsMessage.Show(
                $"{T("MESWC.LoadFailed", "Načtení MESWC selhalo.")}\n\n{ex.Message}",
                "MESWC",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ApplySnapshot(
        MesWorkcenterDashboardSnapshot snapshot)
    {
        TxtLastRefresh.Text =
            snapshot.LoadedAt.ToString(
                "dd.MM.yyyy HH:mm:ss",
                CultureInfo.CurrentCulture);

        TxtShift.Text =
            ValueOrDash(
                snapshot.ShiftName);

        TxtShiftPeriod.Text =
            snapshot.ShiftStart.HasValue
            && snapshot.ShiftEnd.HasValue
                ? $"{snapshot.ShiftStart:dd.MM HH:mm} – {snapshot.ShiftEnd:dd.MM HH:mm}"
                : "—";

        var live =
            snapshot.Live;

        TxtState.Text =
            ValueOrDash(
                live?.StateName);

        TxtStateDuration.Text =
            live is null
                ? string.Empty
                : FormatDuration(
                    live.CurrentStateDuration(
                        snapshot.LoadedAt));

        ApplyStateBadge(
            live?.StateColor,
            live?.StateCategoryColor);

        var active =
            snapshot.ActiveOrder;

        TxtOrder.Text =
            ValueOrDash(
                active?.OrderCode
                ?? live?.OrderCode);

        TxtArticle.Text =
            ValueOrDash(
                active?.ProductCode
                ?? live?.ProductCode);

        TxtSapNumber.Text =
            ValueOrDash(
                active?.SapArticleNumber);

        TxtOperation.Text =
            ValueOrDash(
                active?.OperationCode
                ?? snapshot.CurrentContext?.OperationCode);

        var shift =
            snapshot.ShiftMetrics;

        TxtAveragePerformance.Text =
            FormatRateRounded(
                shift.ActualPerformance);

        TxtGross.Text =
            FormatAmount(
                shift.GrossAmount);

        TxtNet.Text =
            FormatAmount(
                shift.NetAmount);

        TxtShiftScrap.Text =
            FormatAmount(
                shift.PerformanceBad);

        TxtExpected.Text =
            snapshot.ExpectedByNow.HasValue
                ? FormatAmount(
                    snapshot.ExpectedByNow.Value)
                : "—";

        TxtPlannedPerformance.Text =
            FormatRateRounded(
                snapshot.PlannedPerformance);

        TxtOrderSize.Text =
            active is null
                ? "—"
                : FormatAmount(
                    active.TargetQuantity);

        TxtOrderFinished.Text =
            active is null
                ? "—"
                : FormatAmount(
                    active.FinishedQuantity);

        TxtOrderScrap.Text =
            active is null
                ? "—"
                : FormatAmount(
                    active.ScrapQuantity);

        TxtOrderRemaining.Text =
            active is null
                ? "—"
                : FormatAmount(
                    active.RemainingQuantity);

        var progress =
            active?.ProgressPercent
            ?? 0d;

        PbOrderProgress.Value =
            progress;

        TxtOrderProgress.Text =
            active is null
                ? "—"
                : $"{progress:0}%";

        GridOperators.ItemsSource =
            snapshot.Operators;

        GridStates.ItemsSource =
            snapshot.StateSummary;

        ApplyOrderCounters(
            snapshot.OrderCounters);

        ApplyOee(
            shift);

        BuildGraph();
    }

    private void ApplyOrderCounters(
        MesWorkcenterOrderCounterSummary counters)
    {
        TxtCounterScrapProduction.Text =
            FormatCounterValue(
                counters.ScrapProduction);

        TxtCounterScrapGlass.Text =
            FormatCounterValue(
                counters.ScrapGlass);

        TxtCounterWashedBottles.Text =
            FormatCounterValue(
                counters.WashedBottles);

        TxtCounterDevelopment.Text =
            FormatCounterValue(
                counters.DevelopmentDepartment);

        TxtCounterQuality.Text =
            FormatCounterValue(
                counters.QualityDepartment);

        TxtCounterSetup.Text =
            FormatCounterValue(
                counters.Setup);

        TxtCounterTransport.Text =
            FormatCounterValue(
                counters.TransportLogistics);
    }

    private static string FormatCounterValue(
        decimal value) =>
        $"{value:N0} ks";

    private static string FormatAmount(
        decimal value) =>
        $"{value:N0} ks";

    private static string FormatRateRounded(
        decimal? value) =>
        value.HasValue
            ? $"{value.Value:0} ks/min"
            : "—";

    private static string FormatRateRounded(
        double value) =>
        $"{value:0} ks/min";

    private void ApplyOee(
        MesWorkcenterShiftMetrics shift)
    {
        PbAvailability.Value =
            Math.Clamp(
                shift.AvailabilityOee,
                0d,
                100d);

        PbPerformance.Value =
            Math.Clamp(
                shift.PerformanceOee,
                0d,
                150d);

        PbQuality.Value =
            Math.Clamp(
                shift.QualityOee,
                0d,
                100d);

        PbOee.Value =
            Math.Clamp(
                shift.Oee,
                0d,
                130d);

        TxtAvailability.Text =
            $"{shift.AvailabilityOee:0.00}%";

        TxtPerformance.Text =
            $"{shift.PerformanceOee:0.00}%";

        TxtQuality.Text =
            $"{shift.QualityOee:0.00}%";

        TxtOee.Text =
            $"{shift.Oee:0.00}%";
    }

    private void GraphHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_snapshot is not null) BuildGraph();
    }

    private void BuildGraph()
    {
        GraphHost.Children.Clear();
        var snapshot = _snapshot;

        if (snapshot is null || !snapshot.ShiftStart.HasValue || !snapshot.ShiftEnd.HasValue) return;

        var from = snapshot.ShiftStart.Value;
        var to = snapshot.LoadedAt < snapshot.ShiftEnd.Value ? snapshot.LoadedAt : snapshot.ShiftEnd.Value;
        if (to <= from) return;

        var width = Math.Max(700d, GraphHost.ActualWidth > 20d ? GraphHost.ActualWidth : 1100d);
        const double height = 285d;
        const double left = 48d;
        const double rightPad = 14d;
        const double top = 14d;
        const double plotBottom = 215d;
        const double stateTop = 238d;
        const double stateHeight = 22d;
        var plotWidth = width - left - rightPad;

        var canvas = new Canvas { Width = width, Height = height, Background = Brushes.White };
        GraphHost.Children.Add(canvas);

        var rows = snapshot.GraphRows
            .Where(row => row.Endtime > from && row.Starttime < to)
            .OrderBy(row => row.Starttime)
            .ToList();

        var points = BuildPerformancePoints(rows, from, to, TimeSpan.FromMinutes(1));
        var planned = snapshot.PlannedPerformance.HasValue ? (double)snapshot.PlannedPerformance.Value : 0d;
        var maxActual = points.Count > 0 ? points.Max(point => point.Value) : 0d;
        var yMax = Math.Max(10d, Math.Ceiling(Math.Max(maxActual, planned) * 1.15d / 5d) * 5d);

        double X(DateTime value) => left + Math.Clamp((value - from).TotalSeconds / (to - from).TotalSeconds, 0d, 1d) * plotWidth;
        double Y(double value) => plotBottom - Math.Clamp(Math.Max(0d, value) / yMax, 0d, 1d) * (plotBottom - top);

        DrawGrid(canvas, from, to, left, plotWidth, top, plotBottom, yMax, X, Y);

        if (planned > 0d)
        {
            canvas.Children.Add(new Line
            {
                X1 = left,
                X2 = left + plotWidth,
                Y1 = Y(planned),
                Y2 = Y(planned),
                Stroke = Brushes.DimGray,
                StrokeThickness = 1.3d,
                StrokeDashArray = new DoubleCollection { 5d, 4d }
            });
        }

        if (points.Count > 0)
        {
            var polyline = new Polyline
            {
                Stroke = new SolidColorBrush(Color.FromRgb(232, 77, 24)),
                StrokeThickness = 1.8d
            };

            foreach (var point in points)
            {
                polyline.Points.Add(new Point(X(point.Time), Y(point.Value)));
            }

            canvas.Children.Add(polyline);

            foreach (var point in points)
            {
                var dot = new Ellipse { Width = 4d, Height = 4d, Fill = Brushes.Red };
                Canvas.SetLeft(dot, X(point.Time) - 2d);
                Canvas.SetTop(dot, Y(point.Value) - 2d);
                canvas.Children.Add(dot);
            }
        }

        DrawStateBand(canvas, snapshot, rows, from, to, left, plotWidth, stateTop, stateHeight, X);

        var legend = new TextBlock
        {
            Text = T("MESWC.ActualPerformance", "Strojní takt [ks/min]"),
            Foreground = new SolidColorBrush(Color.FromRgb(232, 77, 24)),
            FontWeight = FontWeights.SemiBold,
            FontSize = 10d
        };
        Canvas.SetLeft(legend, left);
        Canvas.SetTop(legend, 266d);
        canvas.Children.Add(legend);
    }

    private static void DrawGrid(
        Canvas canvas,
        DateTime from,
        DateTime to,
        double left,
        double plotWidth,
        double top,
        double bottom,
        double yMax,
        Func<DateTime, double> x,
        Func<double, double> y)
    {
        for (var index = 0; index <= 4; index++)
        {
            var value = yMax * index / 4d;
            var py = y(value);
            canvas.Children.Add(new Line { X1 = left, X2 = left + plotWidth, Y1 = py, Y2 = py, Stroke = Brushes.LightGray, StrokeThickness = 0.7d });

            var label = new TextBlock { Text = value.ToString("0", CultureInfo.CurrentCulture), Foreground = Brushes.Black, FontSize = 9d };
            Canvas.SetLeft(label, 4d);
            Canvas.SetTop(label, py - 7d);
            canvas.Children.Add(label);
        }

        var tick = new DateTime(from.Year, from.Month, from.Day, from.Hour, from.Minute < 30 ? 0 : 30, 0);
        if (tick < from) tick = tick.AddMinutes(30);

        while (tick <= to)
        {
            var px = x(tick);
            canvas.Children.Add(new Line
            {
                X1 = px,
                X2 = px,
                Y1 = top,
                Y2 = bottom,
                Stroke = tick.Minute == 0 ? Brushes.Gray : Brushes.LightGray,
                StrokeThickness = tick.Minute == 0 ? 0.9d : 0.55d
            });

            var label = new TextBlock { Text = tick.ToString("HH:mm", CultureInfo.CurrentCulture), Foreground = Brushes.Black, FontSize = 8.5d };
            Canvas.SetLeft(label, px - 15d);
            Canvas.SetTop(label, bottom + 3d);
            canvas.Children.Add(label);
            tick = tick.AddMinutes(30);
        }
    }

    private void DrawStateBand(
        Canvas canvas,
        MesWorkcenterDashboardSnapshot snapshot,
        IReadOnlyList<Mes06ProductionGraphRecord> rows,
        DateTime from,
        DateTime to,
        double left,
        double plotWidth,
        double stateTop,
        double stateHeight,
        Func<DateTime, double> x)
    {
        foreach (var row in rows)
        {
            var start = row.Starttime < from ? from : row.Starttime;
            var end = row.Endtime > to ? to : row.Endtime;
            if (end <= start) continue;

            var rectangle = new Rectangle
            {
                Width = Math.Max(1d, x(end) - x(start)),
                Height = stateHeight,
                Fill = ResolveStateBrush(snapshot, row),
                Stroke = Brushes.White,
                StrokeThickness = 0.35d,
                ToolTip = BuildStateToolTip(snapshot, row, start, end)
            };
            Canvas.SetLeft(rectangle, x(start));
            Canvas.SetTop(rectangle, stateTop);
            canvas.Children.Add(rectangle);
        }

        var border = new Rectangle { Width = plotWidth, Height = stateHeight, Stroke = Brushes.Gray, StrokeThickness = 0.7d, IsHitTestVisible = false };
        Canvas.SetLeft(border, left);
        Canvas.SetTop(border, stateTop);
        canvas.Children.Add(border);
    }

    private ToolTip BuildStateToolTip(
        MesWorkcenterDashboardSnapshot snapshot,
        Mes06ProductionGraphRecord row,
        DateTime start,
        DateTime end)
    {
        var matchingOrder = snapshot.AssignedOrders.FirstOrDefault(order =>
            string.Equals(order.OrderCode, row.OrderCode, StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(row.OperationCode)
                || string.Equals(order.OperationCode, row.OperationCode, StringComparison.OrdinalIgnoreCase)));

        var panel = new StackPanel { Margin = new Thickness(4) };
        panel.Children.Add(new TextBlock { Text = ValueOrDash(row.StateName), FontWeight = FontWeights.Bold, FontSize = 13d, Margin = new Thickness(0, 0, 0, 5) });
        AddTip(panel, T("MESWC.Tooltip.Start", "Start"), start.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture));
        AddTip(panel, T("MESWC.Tooltip.End", "Konec"), end.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture));
        AddTip(panel, T("MESWC.Tooltip.Duration", "Doba"), FormatDuration(end - start));
        AddTip(panel, T("MESWC.Tooltip.Order", "Zakázka"), ValueOrDash(row.OrderCode));
        AddTip(panel, T("MESWC.Tooltip.Operation", "Operace"), ValueOrDash(row.OperationCode));
        AddTip(panel, T("MESWC.Tooltip.Article", "Artikl"), ValueOrDash(row.ProductCode));
        AddTip(panel, T("MESWC.Tooltip.Description", "Popis artiklu"), ValueOrDash(matchingOrder?.ProductDescription));

        return new ToolTip { Content = panel, Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse };
    }

    private static void AddTip(Panel panel, string caption, string value) =>
        panel.Children.Add(new TextBlock { Text = $"{caption}: {value}", Margin = new Thickness(0, 1, 0, 1) });

    private static IReadOnlyList<RatePoint> BuildPerformancePoints(
        IReadOnlyList<Mes06ProductionGraphRecord> rows,
        DateTime from,
        DateTime to,
        TimeSpan step)
    {
        if (rows.Count == 0 || to <= from || step <= TimeSpan.Zero) return Array.Empty<RatePoint>();

        var productiveAvailability = ResolveProductiveAvailability(rows);
        var productiveState = productiveAvailability.HasValue ? string.Empty : ResolveProductiveStateName(rows);
        var intervals = rows
            .Where(row => IsProductive(row, productiveAvailability, productiveState))
            .Where(row => row.MachineRateKsMin.HasValue && row.MachineRateKsMin.Value >= 0m)
            .Select(row => new RateInterval
            {
                From = row.Starttime < from ? from : row.Starttime,
                To = row.Endtime > to ? to : row.Endtime,
                Value = (double)row.MachineRateKsMin!.Value
            })
            .Where(interval => interval.To > interval.From)
            .ToList();

        var points = new List<RatePoint>();
        var bucket = from;

        while (bucket < to)
        {
            var bucketEnd = bucket + step;
            if (bucketEnd > to) bucketEnd = to;
            var seconds = (bucketEnd - bucket).TotalSeconds;
            if (seconds <= 0d) break;

            double weighted = 0d;
            foreach (var interval in intervals)
            {
                var overlapStart = interval.From > bucket ? interval.From : bucket;
                var overlapEnd = interval.To < bucketEnd ? interval.To : bucketEnd;
                if (overlapEnd <= overlapStart) continue;
                weighted += interval.Value * (overlapEnd - overlapStart).TotalSeconds;
            }

            points.Add(new RatePoint
            {
                Time = bucket + TimeSpan.FromTicks((bucketEnd - bucket).Ticks / 2),
                Value = Math.Max(0d, weighted / seconds)
            });
            bucket = bucketEnd;
        }

        return points;
    }

    private static int? ResolveProductiveAvailability(IReadOnlyList<Mes06ProductionGraphRecord> rows)
    {
        var candidates = rows.Where(row => row.Availability.HasValue).Select(row => row.Availability!.Value).Distinct().ToList();
        var groups = rows.Where(row => row.MesId != Guid.Empty)
            .GroupBy(row => row.MesId)
            .Where(group => group.First().DurationUtilizationSeconds.HasValue && group.First().DurationUtilizationSeconds!.Value > 0m)
            .ToList();

        int? best = null;
        var bestScore = double.MaxValue;

        foreach (var candidate in candidates)
        {
            double score = 0d;
            var samples = 0;
            foreach (var group in groups)
            {
                var target = (double)group.First().DurationUtilizationSeconds!.Value;
                var actual = group.Where(row => row.Availability == candidate).Sum(StateSeconds);
                score += Math.Abs(actual - target) / Math.Max(target, 1d);
                samples++;
            }

            if (samples > 0)
            {
                score /= samples;
                if (score < bestScore) { bestScore = score; best = candidate; }
            }
        }

        return best;
    }

    private static string ResolveProductiveStateName(IReadOnlyList<Mes06ProductionGraphRecord> rows)
    {
        var names = rows.Select(row => row.StateName).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
        var groups = rows.Where(row => row.MesId != Guid.Empty)
            .GroupBy(row => row.MesId)
            .Where(group => group.First().DurationUtilizationSeconds.HasValue && group.First().DurationUtilizationSeconds!.Value > 0m)
            .ToList();

        var bestName = string.Empty;
        var bestScore = double.MaxValue;

        foreach (var name in names)
        {
            double score = 0d;
            var samples = 0;
            foreach (var group in groups)
            {
                var target = (double)group.First().DurationUtilizationSeconds!.Value;
                var actual = group.Where(row => string.Equals(row.StateName, name, StringComparison.CurrentCultureIgnoreCase)).Sum(StateSeconds);
                score += Math.Abs(actual - target) / Math.Max(target, 1d);
                samples++;
            }

            if (samples > 0)
            {
                score /= samples;
                if (score < bestScore) { bestScore = score; bestName = name; }
            }
        }

        return bestName;
    }

    private static bool IsProductive(Mes06ProductionGraphRecord row, int? productiveAvailability, string productiveState)
    {
        if (productiveAvailability.HasValue) return row.Availability == productiveAvailability;
        if (!string.IsNullOrWhiteSpace(productiveState)) return string.Equals(row.StateName, productiveState, StringComparison.CurrentCultureIgnoreCase);
        return true;
    }

    private static double StateSeconds(Mes06ProductionGraphRecord row) =>
        row.StateDurationSeconds.HasValue && row.StateDurationSeconds.Value >= 0m
            ? (double)row.StateDurationSeconds.Value
            : Math.Max(0d, (row.Endtime - row.Starttime).TotalSeconds);

    private Brush ResolveStateBrush(MesWorkcenterDashboardSnapshot snapshot, Mes06ProductionGraphRecord row)
    {
        var definition = snapshot.StateColors.FirstOrDefault(item =>
                string.Equals(item.WorkcenterCode, snapshot.WorkcenterCode, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.StateName, row.StateName, StringComparison.CurrentCultureIgnoreCase))
            ?? snapshot.StateColors.FirstOrDefault(item =>
                string.Equals(item.StateName, row.StateName, StringComparison.CurrentCultureIgnoreCase));

        return new SolidColorBrush(ParseColor(definition?.StateColor) ?? ParseColor(definition?.CategoryColor) ?? Color.FromRgb(190, 190, 190));
    }

    private void ApplyStateBadge(string? stateColor, string? categoryColor) =>
        StateBadge.Background = new SolidColorBrush(ParseColor(stateColor) ?? ParseColor(categoryColor) ?? Color.FromRgb(190, 190, 190));

    private static Color? ParseColor(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        try
        {
            if (ColorConverter.ConvertFromString(raw.Trim()) is Color color) return color;
        }
        catch { }

        var parts = raw.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is 3 or 4 && parts.All(part => byte.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
        {
            var bytes = parts.Select(part => byte.Parse(part, CultureInfo.InvariantCulture)).ToArray();
            return parts.Length == 3
                ? Color.FromRgb(bytes[0], bytes[1], bytes[2])
                : Color.FromArgb(bytes[0], bytes[1], bytes[2], bytes[3]);
        }

        return null;
    }

    private static string ValueOrDash(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
    private static string FormatRate(decimal? value) => value.HasValue ? $"{value.Value:0.#} ks/min" : "—";

    private static string FormatDuration(TimeSpan value) =>
        value.TotalDays >= 1d
            ? $"{(int)value.TotalDays}d {value:hh\\:mm\\:ss}"
            : value.ToString("hh\\:mm\\:ss", CultureInfo.InvariantCulture);

    private static string ResolveMesDatabaseSettingsPath(string configurationRootPath)
    {
        foreach (var name in new[] { "mes-database-settings.json", "mes-reporting-settings.json", "mes-sql-settings.json", "mes-database.json", "mes-reporting.json" })
        {
            var candidate = System.IO.Path.Combine(configurationRootPath, name);
            if (File.Exists(candidate)) return candidate;
        }

        if (Directory.Exists(configurationRootPath))
        {
            foreach (var file in Directory.EnumerateFiles(configurationRootPath, "*.json", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(file));
                    if (document.RootElement.ValueKind != JsonValueKind.Object) continue;

                    var names = document.RootElement.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var hasServer = names.Overlaps(new[] { "Server", "SqlServer", "ServerName", "DataSource", "Host", "Address", "ServerAddress" });
                    var hasDatabase = names.Overlaps(new[] { "Database", "DatabaseName", "InitialCatalog", "Catalog" });
                    var hasConnectionString = names.Overlaps(new[] { "ConnectionString", "SqlConnectionString" });
                    var looksMes = System.IO.Path.GetFileName(file).Contains("mes", StringComparison.OrdinalIgnoreCase);
                    if (looksMes && ((hasServer && hasDatabase) || hasConnectionString)) return file;
                }
                catch { }
            }
        }

        throw new FileNotFoundException("MES SQL settings file was not found. Open MESSET and save the connection first.");
    }

    private sealed class RateInterval
    {
        public DateTime From { get; init; }
        public DateTime To { get; init; }
        public double Value { get; init; }
    }

    private sealed class RatePoint
    {
        public DateTime Time { get; init; }
        public double Value { get; init; }
    }
}
