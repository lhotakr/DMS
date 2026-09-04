using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DMS.Desktop.UI;

public static class DmsDisplayFactory
{
    public static StackPanel CreateSection(
        string title)
    {
        var section = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 8)
        };

        section.Children.Add(
            DmsUiFactory.CreateSectionTitle(title));

        return section;
    }

    public static UIElement CreateFieldGrid(
        int columnCount,
        IEnumerable<DmsDisplayField> fields)
    {
        var safeColumnCount = Math.Max(1, columnCount);

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
            var span = Math.Min(
                Math.Max(1, field.ColumnSpan),
                safeColumnCount);

            if (column + span > safeColumnCount)
            {
                row++;
                column = 0;
            }

            while (grid.RowDefinitions.Count <= row)
            {
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
                    column + span >= safeColumnCount ? 0 : 5,
                    8);
            }

            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            Grid.SetColumnSpan(element, span);

            grid.Children.Add(element);

            column += span;

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

    public static UIElement CreateCard(
        UIElement content,
        string backgroundResourceKey = "DmsBackgroundBrush")
    {
        var border = CreateCardBorder(backgroundResourceKey);
        border.Child = content;

        return border;
    }

    public static string NullDash(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "-"
            : value;
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
}