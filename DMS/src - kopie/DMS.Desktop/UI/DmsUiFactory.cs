using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace DMS.Desktop.UI;

public static class DmsUiFactory
{
    public static TextBox CreateTitle(string text)
    {
        var control = new TextBox
        {
            Text = text,
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 14),
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            IsReadOnly = true,
            IsReadOnlyCaretVisible = true,
            Background = Brushes.Transparent,
            TextWrapping = TextWrapping.Wrap
        };

        control.SetResourceReference(
            TextBox.ForegroundProperty,
            "DmsForegroundBrush");

        return control;
    }

    public static TextBlock CreateSectionTitle(string text)
    {
        var control = new TextBlock
        {
            Text = text,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 18, 0, 10)
        };

        control.SetResourceReference(
            TextBlock.ForegroundProperty,
            "DmsForegroundBrush");

        return control;
    }

    public static StackPanel CreateSection(string title)
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 8)
        };

        panel.Children.Add(CreateSectionTitle(title));

        return panel;
    }

    public static TextBlock CreateMutedText(string text)
    {
        var control = new TextBlock
        {
            Text = text,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };

        control.SetResourceReference(
            TextBlock.ForegroundProperty,
            "DmsMutedForegroundBrush");

        return control;
    }

    public static TextBlock CreateSmallHint(string text)
    {
        var control = new TextBlock
        {
            Text = text,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6)
        };

        control.SetResourceReference(
            TextBlock.ForegroundProperty,
            "DmsMutedForegroundBrush");

        return control;
    }

    public static Border CreateField(
        string label,
        string? value,
        Thickness? margin = null)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = margin ?? new Thickness(0, 0, 0, 8),
            BorderThickness = new Thickness(1)
        };

        border.SetResourceReference(
            Border.BackgroundProperty,
            "DmsBackgroundBrush");

        border.SetResourceReference(
            Border.BorderBrushProperty,
            "DmsBorderBrush");

        var panel = new StackPanel();

        var labelBlock = new TextBlock
        {
            Text = label,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };

        labelBlock.SetResourceReference(
            TextBlock.ForegroundProperty,
            "DmsMutedForegroundBrush");

        var valueBox = CreateReadOnlyText(value);

        panel.Children.Add(labelBlock);
        panel.Children.Add(valueBox);

        border.Child = panel;

        return border;
    }

    public static StackPanel CreateCompactField(
        string label,
        string? value,
        Thickness? margin = null)
    {
        var panel = new StackPanel
        {
            Margin = margin ?? new Thickness(0)
        };

        var labelBlock = new TextBlock
        {
            Text = label,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 3)
        };

        labelBlock.SetResourceReference(
            TextBlock.ForegroundProperty,
            "DmsMutedForegroundBrush");

        panel.Children.Add(labelBlock);
        panel.Children.Add(CreateReadOnlyText(value));

        return panel;
    }

    public static TextBox CreateReadOnlyText(string? value)
    {
        var control = new TextBox
        {
            Text = NullDash(value),
            IsReadOnly = true,
            IsReadOnlyCaretVisible = true,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true
        };

        control.SetResourceReference(
            TextBox.ForegroundProperty,
            "DmsForegroundBrush");

        return control;
    }

    public static Border CreateInfoCard(
        string title,
        string body)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 12),
            BorderThickness = new Thickness(1)
        };

        border.SetResourceReference(
            Border.BackgroundProperty,
            "DmsPanelBrush");

        border.SetResourceReference(
            Border.BorderBrushProperty,
            "DmsBorderBrush");

        var panel = new StackPanel();

        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 6)
        };

        titleBlock.SetResourceReference(
            TextBlock.ForegroundProperty,
            "DmsForegroundBrush");

        panel.Children.Add(titleBlock);
        panel.Children.Add(CreateReadOnlyText(body));

        border.Child = panel;

        return border;
    }

    public static Border CreateWarning(
        string title,
        string body)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 12),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromRgb(68, 48, 35)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(150, 105, 70))
        };

        var panel = new StackPanel();

        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(
                Color.FromRgb(255, 225, 190)),
            Margin = new Thickness(0, 0, 0, 6)
        });

        panel.Children.Add(new TextBlock
        {
            Text = body,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(
                Color.FromRgb(255, 240, 220))
        });

        border.Child = panel;

        return border;
    }

    public static Button CreateActionButton(
        string text,
        Action action)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 0)
        };

        button.SetResourceReference(
            Control.BackgroundProperty,
            "DmsAccentBrush");

        button.SetResourceReference(
            Control.ForegroundProperty,
            "DmsOnAccentBrush");

        button.Click += (_, _) => action();

        return button;
    }

    public static DataGrid CreateDataGrid(
        MouseWheelEventHandler? mouseWheelHandler = null)
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            CanUserResizeColumns = true,
            CanUserReorderColumns = true,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            Margin = new Thickness(0, 0, 0, 16),
            MinHeight = 120,
            MaxHeight = 320,
            EnableRowVirtualization = true,
            EnableColumnVirtualization = true,
            SelectionMode = DataGridSelectionMode.Extended,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            ClipboardCopyMode = DataGridClipboardCopyMode.IncludeHeader,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        grid.SetResourceReference(
            Control.BackgroundProperty,
            "DmsBackgroundBrush");

        grid.SetResourceReference(
            Control.ForegroundProperty,
            "DmsForegroundBrush");

        grid.SetResourceReference(
            Control.BorderBrushProperty,
            "DmsBorderBrush");

        if (mouseWheelHandler is not null)
        {
            grid.PreviewMouseWheel += mouseWheelHandler;
        }

        return grid;
    }

    public static DataGridTextColumn CreateTextColumn(
        string header,
        string binding,
        double width,
        string? stringFormat = null)
    {
        var columnBinding = new Binding(binding);

        if (!string.IsNullOrWhiteSpace(stringFormat))
        {
            columnBinding.StringFormat = stringFormat;
        }

        return new DataGridTextColumn
        {
            Header = header,
            Binding = columnBinding,
            Width = new DataGridLength(width),
            MinWidth = Math.Min(width, 120)
        };
    }

    public static string NullDash(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "-"
            : value;
    }
}