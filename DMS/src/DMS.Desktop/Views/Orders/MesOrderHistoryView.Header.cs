using DMS.Integration.Mes.Orders;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DMS.Desktop.Views.Orders;

public sealed partial class MesOrderHistoryView
{
    private readonly TextBlock _plantInfo = new()
    {
        FontSize = 16, FontWeight = FontWeights.SemiBold,
        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6)
    };
    private readonly StackPanel _quantityCard = new() { Margin = new Thickness(0,8,0,10) };

    private static Brush QuantityColor(byte r, byte g, byte b) =>
        new SolidColorBrush(Color.FromRgb(r,g,b));

    private static string UserStatus(MesProductionOrderRecord order) => order.StatusCode switch
    {
        "CRTD" => "Založeno",
        "REL" => "Uvolněno",
        "REL PROD" => "Ve výrobě",
        "PREL" => "Částečně uvolněno",
        "RWDN" => "Uvolnění zrušeno",
        "CCLD" => "Zrušeno",
        "UCPL" => "Nedokončeno",
        "CLSD" => "Uzavřeno",
        _ => "Stav: " + order.StatusCode
    };

    private void ApplyTodoHeader(MesProductionOrderRecord order)
    {
        _mesordExportOrderV63 = order;
        // No DMS/SAP 9200 relationship has been verified. Do not invent one.
        _plantInfo.Text = $"{MesordText("Závod 2000")}: {order.OrderCode}\n" +
            $"{MesordText("Závod 9200")}: {Qa9200ForMes()}";
        _status.Text = $"{order.StatusIcon} {MesordText(UserStatus(order))}";
        _summary.Text =
            $"{MesordText("Artikl")}: {order.ProductCode}  |  SAP: {order.SapArticleNumber}\n" +
            $"{MesordText("Popis")}: {(string.IsNullOrWhiteSpace(order.ProductDescription) ? MesordText("Není dostupný v MES") : order.ProductDescription)}\n" +
            $"{MesordText("Plán")}: {order.TargetQuantity:N0}  |  OK: {order.FinishedQuantity:N0}  |  NOK: {order.ScrapQuantity:N0}\n" +
            $"{MesordText("Skutečný začátek")}: {order.ActualStart:dd.MM.yyyy HH:mm}  |  {MesordText("Konec")}: {order.ActualEnd:dd.MM.yyyy HH:mm}";
        _quantityCard.Children.Clear();
        _quantityCard.Children.Add(new TextBlock
        {
            Text=MesordText("Plnění zakázky"), FontSize=16, FontWeight=FontWeights.SemiBold,
            Margin=new Thickness(0,0,0,5)
        });
        var ok=Math.Max(0m,order.FinishedQuantity);
        var nok=Math.Max(0m,order.ScrapQuantity);
        var pending=Math.Max(0m,order.TargetQuantity-ok-nok);
        var total=Math.Max(1m,ok+nok+pending);
        // The colors are fixed display categories, not FASTEC machine-state colors.
        var progress=new Grid { Height=21,Margin=new Thickness(0,0,0,5) };
        var amounts=new[] {ok,nok,pending};
        var colors=new[] {QuantityColor(36,180,102),QuantityColor(230,72,72),QuantityColor(76,142,225)};
        var labels=new[] {MesordText("OK"),MesordText("NOK"),MesordText("Zbývá")};
        for (var i=0;i<3;i++)
        {
            progress.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength((double)(amounts[i]/total),GridUnitType.Star) });
            var segment=new Border { Background=colors[i],ToolTip=$"{labels[i]}: {amounts[i]:N0}" };
            Grid.SetColumn(segment,i);
            progress.Children.Add(segment);
        }
        _quantityCard.Children.Add(progress);
        var legend=new WrapPanel();
        for (var i=0;i<3;i++)
        {
            var legendItem=new StackPanel { Orientation=Orientation.Horizontal, Margin=new Thickness(0,0,18,4) };
            legendItem.Children.Add(new Border { Width=13,Height=13,Background=colors[i],Margin=new Thickness(0,0,6,0) });
            legendItem.Children.Add(new TextBlock { Text=$"{labels[i]} {amounts[i]:N0}" });
            legend.Children.Add(legendItem);
        }
        _quantityCard.Children.Add(legend);
        _operations.MinHeight=0;
        _operations.MaxHeight=Math.Min(500,60+_operations.Items.Count*30);
    }
}
