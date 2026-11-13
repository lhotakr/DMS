using DMS.Core.Quality;
using DMS.Desktop.Logging;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DMS.Desktop.Views.QualityOrders;

/// <summary>Standalone live-data panel shared by QO02, QO03 and QO06.</summary>
internal static class QualityMesLivePanel
{
    internal static Border Create(string order9200, string qaDataRoot,
        string mesConfigurationRoot, DmsLogger? logger, string user,
        Func<string, string> translate)
    {
        string L(string key, string fallback)
        {
            var result = translate(key);
            return string.IsNullOrWhiteSpace(result) || result == key || result == "[[" + key + "]]"
                ? fallback : result;
        }
        var label = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13 };
        label.SetResourceReference(TextBlock.ForegroundProperty, "DmsForegroundBrush");
        var caption = new TextBlock
        {
            Text = L("QO.MES.LiveTitle", "Živá výrobní data MES"),
            FontSize = 16, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 5)
        };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "DmsForegroundBrush");
        var content = new StackPanel();
        content.Children.Add(caption);
        content.Children.Add(label);
        var panel = new Border { Child = content, Padding = new Thickness(12),
            Margin = new Thickness(18, 6, 18, 8), CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(1) };
        panel.SetResourceReference(Border.BackgroundProperty, "DmsBackgroundBrush");
        panel.SetResourceReference(Border.BorderBrushProperty, "DmsBorderBrush");
        var started = false;
        panel.Loaded += async (_, _) =>
        {
            if (started) return;
            started = true;
            try
            {
                var repository = new JsonQualityRepository(new QualityStoragePaths(qaDataRoot));
                var order = repository.LoadOrders().FirstOrDefault(x =>
                    string.Equals(x.OrderNumber, order9200, StringComparison.OrdinalIgnoreCase));
                var mes2000 = order?.MesOrderNumber2000?.Trim() ?? string.Empty;
                if (mes2000.Length == 0)
                {
                    label.Text = L("QO.MES.LiveNoLink", "Zakázka nemá přiřazenou MES zakázku 2000.");
                    return;
                }
                label.Text = L("QO.MES.LiveLoading", "Načítám stav výroby z MES…");
                var data = await new QualityMesLiveReadService(mesConfigurationRoot)
                    .ReadAsync(new[] { mes2000 });
                if (data.Rows.TryGetValue(mes2000, out var mes))
                {
                    var freshness = data.IsFresh
                        ? L("QO.MES.LiveCurrent", "Aktuální")
                        : L("QO.MES.LiveStale", "Poslední známá data – nejsou aktuální");
                    label.Text = $"{freshness} · {L("QO.MES.Order2000", "Zakázka 2000")}: {mes2000} · " +
                        $"{L("QO.MES.LiveStatus", "Stav")}: {mes.StatusCode} · " +
                        $"{L("QO.MES.LiveTarget", "Plán")}: {mes.TargetQuantity:N0} · " +
                        $"OK: {mes.FinishedQuantity:N0} · NOK: {mes.ScrapQuantity:N0} · " +
                        $"{L("QO.MES.LiveUpdated", "Načteno")}: {data.RetrievedAt:dd.MM.yyyy HH:mm:ss}";
                }
                else
                {
                    label.Text = data.IsFresh
                        ? L("QO.MES.LiveNotFound", "Zakázka 2000 nebyla v MES nalezena.")
                        : L("QO.MES.LiveOffline", "MES je nedostupný; uložená data kvality zůstávají dostupná.");
                }
                if (!data.IsFresh)
                    logger?.Warning($"QA MES live fetch failed. Order2000={mes2000}; Reason={data.Error}");
                else
                    logger?.AdminAction("QUALITY", "MES_LIVE_LOADED", user,
                        $"Order2000={mes2000}; Found={data.Rows.ContainsKey(mes2000)}");
            }
            catch (Exception ex)
            {
                label.Text = L("QO.MES.LiveOffline", "MES je nedostupný; uložená data kvality zůstávají dostupná.");
                logger?.Error($"QA MES live panel failed. Order9200={order9200}", ex);
            }
        };
        return panel;
    }
}
