using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DMS.Desktop.Behaviors;

/// <summary>
/// Zobrazí ToolTip s plným textem DataGrid buňky pouze tehdy,
/// když se text do aktuální šířky buňky nevejde.
/// </summary>
public static class DmsDataGridCellToolTip
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(DmsDataGridCellToolTip),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    private static void OnIsEnabledChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not DataGridCell cell)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            cell.MouseEnter -= Cell_MouseEnter;
            cell.MouseEnter += Cell_MouseEnter;
            cell.MouseLeave -= Cell_MouseLeave;
            cell.MouseLeave += Cell_MouseLeave;
        }
        else
        {
            cell.MouseEnter -= Cell_MouseEnter;
            cell.MouseLeave -= Cell_MouseLeave;
            cell.ClearValue(FrameworkElement.ToolTipProperty);
        }
    }

    private static void Cell_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is not DataGridCell cell)
        {
            return;
        }

        var textBlock = FindVisualChild<TextBlock>(cell);
        var text = textBlock?.Text;

        if (textBlock is null ||
            string.IsNullOrWhiteSpace(text) ||
            !IsTextClipped(textBlock))
        {
            cell.ToolTip = null;
            return;
        }

        cell.ToolTip = text;
    }

    private static void Cell_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is DataGridCell cell)
        {
            cell.ToolTip = null;
        }
    }

    private static bool IsTextClipped(TextBlock textBlock)
    {
        if (textBlock.ActualWidth <= 0)
        {
            return false;
        }

        var dpi = VisualTreeHelper.GetDpi(textBlock);
        var formatted = new FormattedText(
            textBlock.Text ?? string.Empty,
            CultureInfo.CurrentUICulture,
            textBlock.FlowDirection,
            new Typeface(
                textBlock.FontFamily,
                textBlock.FontStyle,
                textBlock.FontWeight,
                textBlock.FontStretch),
            textBlock.FontSize,
            textBlock.Foreground,
            dpi.PixelsPerDip);

        return formatted.WidthIncludingTrailingWhitespace >
               textBlock.ActualWidth + 1.0;
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(parent);

        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);

            if (child is T typed)
            {
                return typed;
            }

            var nested = FindVisualChild<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
