using DMS.Core.Quality;
using DMS.Desktop.Logging;
using DMS.Integration.Mes.Orders;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace DMS.Desktop.Views.QualityOrders;

/// <summary>MES status columns are presentation-only; no MES values are written into QA JSON.</summary>
public partial class QualityOrderListView
{
    private string _mesLiveConfigRoot = string.Empty;
    private Dictionary<string, MesProductionOrderRecord> _mesLiveRows =
        new(StringComparer.OrdinalIgnoreCase);
    private TextBlock? _mesLiveUpdated;
    private bool _mesLiveReady;
    private bool _mesLiveRefreshing;
    private bool _mesLiveFresh;
    private int _mesLiveColumnIndex = 4;

    public void ConfigureMesLive(string configurationRoot)
    {
        if (_mesLiveReady) return;
        _mesLiveReady = true;
        _mesLiveConfigRoot = configurationRoot;
        AddLiveColumn("QO.MES.LiveStatus", "Stav MES", "status", 105);
        AddLiveColumn("QO.MES.LiveTarget", "Plán MES", "target", 100);
        AddLiveColumn("QO.MES.LiveGood", "OK MES", "good", 90);
        AddLiveColumn("QO.MES.LiveScrap", "NOK MES", "scrap", 90);
        _mesLiveUpdated = new TextBlock { Margin = new Thickness(0, 0, 0, 6),
            Text = LiveText("QO.MES.LiveLoading", "Načítám stav výroby z MES…") };
        _mesLiveUpdated.SetResourceReference(TextBlock.ForegroundProperty, "DmsMutedForegroundBrush");
        if (TxtHint.Parent is StackPanel parent) parent.Children.Add(_mesLiveUpdated);
        Loaded += async (_, _) => await RefreshMesLiveAsync();
        BtnRefresh.Click += async (_, _) => await RefreshMesLiveAsync();
    }

    private string LiveText(string key, string fallback)
    {
        var translated = _translate?.Invoke(key);
        return string.IsNullOrWhiteSpace(translated) || translated == key || translated == "[[" + key + "]]"
            ? fallback : translated;
    }

    private void AddLiveColumn(string key, string fallback, string field, double width)
    {
        var column = new DataGridTextColumn
        {
            Header = LiveText(key, fallback),
            Width = new DataGridLength(width),
            MinWidth = width - 8,
            IsReadOnly = true,
            Binding = new Binding(nameof(QualityOrderListRow.MesOrderNumber2000))
            {
                Converter = new MesLiveCellConverter(this, field)
            }
        };
        // Immediately after the linked MES/9200 identification columns.
        GridOrders.Columns.Insert(Math.Min(_mesLiveColumnIndex++, GridOrders.Columns.Count), column);
    }

    private async System.Threading.Tasks.Task RefreshMesLiveAsync()
    {
        if (_mesLiveRefreshing) return;
        var codes = _allRows.Select(x => x.MesOrderNumber2000)
            .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (codes.Length == 0)
        {
            if (_mesLiveUpdated is not null) _mesLiveUpdated.Text = LiveText("QO.MES.LiveNoLink",
                "V seznamu nejsou zakázky propojené s MES.");
            return;
        }
        _mesLiveRefreshing = true;
        if (_mesLiveUpdated is not null) _mesLiveUpdated.Text =
            LiveText("QO.MES.LiveLoading", "Načítám stav výroby z MES…");
        try
        {
            var result = await new QualityMesLiveReadService(_mesLiveConfigRoot).ReadAsync(codes);
            _mesLiveRows = result.Rows.ToDictionary(x => x.Key, x => x.Value,
                StringComparer.OrdinalIgnoreCase);
            _mesLiveFresh = result.IsFresh;
            GridOrders.Items.Refresh();
            if (_mesLiveUpdated is not null)
                _mesLiveUpdated.Text = result.IsFresh
                    ? $"{LiveText("QO.MES.LiveUpdated", "MES načteno")}: {result.RetrievedAt:dd.MM.yyyy HH:mm:ss} · " +
                      $"{result.Rows.Count:N0}/{codes.Length:N0}"
                    : LiveText("QO.MES.LiveStale", "Poslední známá data – nejsou aktuální") +
                      (result.RetrievedAt.HasValue ? $" ({result.RetrievedAt:dd.MM.yyyy HH:mm:ss})" : "");
            if (result.IsFresh)
                _logger?.AdminAction("QO05", "MES_LIVE_LOADED", _currentUserName,
                    $"Requested={codes.Length}; Found={result.Rows.Count}");
            else
                _logger?.Warning($"QO05 MES live refresh failed. Reason={result.Error}");
        }
        catch (Exception ex)
        {
            if (_mesLiveUpdated is not null) _mesLiveUpdated.Text =
                LiveText("QO.MES.LiveOffline", "MES není dostupný; data QA zůstávají dostupná.");
            _logger?.Error("QO05 MES live refresh failed.", ex);
        }
        finally { _mesLiveRefreshing = false; }
    }

    private sealed class MesLiveCellConverter : IValueConverter
    {
        private readonly QualityOrderListView _parent;
        private readonly string _field;
        internal MesLiveCellConverter(QualityOrderListView parent, string field)
        { _parent = parent; _field = field; }
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var code = value as string;
            if (string.IsNullOrWhiteSpace(code) || !_parent._mesLiveRows.TryGetValue(code, out var mes))
                return "—";
            var display = _field switch
            {
                "status" => mes.StatusCode,
                "target" => mes.TargetQuantity.ToString("N0", culture),
                "good" => mes.FinishedQuantity.ToString("N0", culture),
                "scrap" => mes.ScrapQuantity.ToString("N0", culture),
                _ => "—"
            };
            return _parent._mesLiveFresh ? display : "⚠ " + display;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
