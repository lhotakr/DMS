using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DMS.Desktop.UI;

public static class DmsDisplayGridFactory
{
    public static UIElement CreateSection(
        string title,
        params UIElement[] children)
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 8)
        };

        panel.Children.Add(CreateSectionTitle(title));

        foreach (var child in children)
        {
            panel.Children.Add(child);
        }

        return panel;
    }

    public static UIElement CreateSectionTitle(string text)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 18, 0, 10)
        };

        block.SetResourceReference(
            TextBlock.ForegroundProperty,
            "DmsForegroundBrush");

        return block;
    }

    public static UIElement CreateMutedText(string text)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };

        block.SetResourceReference(
            TextBlock.ForegroundProperty,
            "DmsMutedForegroundBrush");

        return block;
    }

    public static UIElement CreateInfoCard(
        string title,
        string body)
    {
        var border = CreateCardBorder(
            "DmsPanelBrush");

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

        var bodyBlock = new TextBlock
        {
            Text = body,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap
        };

        bodyBlock.SetResourceReference(
            TextBlock.ForegroundProperty,
            "DmsMutedForegroundBrush");

        panel.Children.Add(titleBlock);
        panel.Children.Add(bodyBlock);

        border.Child = panel;

        return border;
    }

    public static UIElement CreateWarning(
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
            Foreground = new SolidColorBrush(Color.FromRgb(255, 225, 190)),
            Margin = new Thickness(0, 0, 0, 6)
        });

        panel.Children.Add(new TextBlock
        {
            Text = body,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(255, 240, 220))
        });

        border.Child = panel;

        return border;
    }

    public static UIElement CreateField(
        string label,
        string? value)
    {
        return CreateFieldCard(
            new DmsDisplayField(label, value));
    }

    public static UIElement CreateFieldGrid(
        int columnCount,
        IEnumerable<DmsDisplayField> fields)
    {
        var safeColumnCount = columnCount < 1
            ? 1
            : columnCount;

        var grid = new Grid
        {
            Margin = new Thickness(0, 0, 0, 8)
        };

        for (var i = 0; i < safeColumnCount; i++)
        {
            grid.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star)
                });
        }

        var row = 0;
        var column = 0;

        foreach (var field in fields)
        {
            while (grid.RowDefinitions.Count <= row)
            {
                grid.RowDefinitions.Add(
                    new RowDefinition
                    {
                        Height = GridLength.Auto
                    });
            }

            var columnSpan = Math.Min(
                field.ColumnSpan,
                safeColumnCount);

            if (column + columnSpan > safeColumnCount)
            {
                row++;
                column = 0;

                grid.RowDefinitions.Add(
                    new RowDefinition
                    {
                        Height = GridLength.Auto
                    });
            }

            var element = CreateFieldCard(field);

            if (element is FrameworkElement frameworkElement)
            {
                frameworkElement.Margin = new Thickness(
                    column == 0 ? 0 : 5,
                    0,
                    column + columnSpan >= safeColumnCount ? 0 : 5,
                    8);
            }

            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            Grid.SetColumnSpan(element, columnSpan);

            grid.Children.Add(element);

            column += columnSpan;

            if (column >= safeColumnCount)
            {
                row++;
                column = 0;
            }
        }

        return grid;
    }

    public static UIElement CreateExpanderCard(
        string header,
        UIElement content,
        bool isExpanded = true)
    {
        var expander = new Expander
        {
            Header = header,
            IsExpanded = isExpanded,
            Margin = new Thickness(0, 0, 0, 12)
        };

        expander.SetResourceReference(
            Control.ForegroundProperty,
            "DmsForegroundBrush");

        var border = CreateCardBorder(
            "DmsBackgroundBrush");

        border.Padding = new Thickness(14);
        border.Child = content;

        expander.Content = border;

        return expander;
    }

    private static UIElement CreateFieldCard(
        DmsDisplayField field)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 7, 10, 7),
            BorderThickness = new Thickness(1),
            MinHeight = 44
        };

        border.SetResourceReference(
            Border.BackgroundProperty,
            "DmsPanelBrush");

        border.SetResourceReference(
            Border.BorderBrushProperty,
            "DmsBorderBrush");

        var panel = new StackPanel();

        var labelBlock = new TextBlock
        {
            Text = field.Label,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 3)
        };

        labelBlock.SetResourceReference(
            TextBlock.ForegroundProperty,
            "DmsMutedForegroundBrush");

        var valueBox = new TextBox
        {
            Text = NullDash(field.Value),
            IsReadOnly = true,
            IsReadOnlyCaretVisible = true,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            TextWrapping = TextWrapping.Wrap,
            VerticalContentAlignment = VerticalAlignment.Center,
            FontSize = 14
        };

        valueBox.SetResourceReference(
            TextBox.ForegroundProperty,
            "DmsForegroundBrush");

        panel.Children.Add(labelBlock);
        panel.Children.Add(valueBox);

        border.Child = panel;

        return border;
    }

    private static Border CreateCardBorder(
        string backgroundResourceKey)
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
            backgroundResourceKey);

        border.SetResourceReference(
            Border.BorderBrushProperty,
            "DmsBorderBrush");

        return border;
    }

    public static string NullDash(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "-"
            : value;
    }
}