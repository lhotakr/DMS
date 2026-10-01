using DMS.Integration.Mes.Reporting.Models;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.Mes;

public sealed class Mes06CounterSelectionWindow : Window
{
    private readonly IReadOnlyList<Mes06CounterCatalogItem> _catalog;
    private readonly HashSet<string> _selected;
    private readonly StackPanel _itemsPanel = new();
    private readonly TextBox _search = new();
    private readonly TextBlock _count = new();
    private bool _updating;

    public IReadOnlyList<string> SelectedCounterNames =>
        _selected
            .OrderBy(
                value => value,
                StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public Mes06CounterSelectionWindow(
        Window? owner,
        IReadOnlyList<Mes06CounterCatalogItem> catalog,
        IEnumerable<string> selectedCounterNames)
    {
        _catalog =
            catalog
            ?? Array.Empty<Mes06CounterCatalogItem>();

        _selected =
            new HashSet<string>(
                selectedCounterNames
                ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

        Owner = owner;
        Title = "MES06 - Výběr čítačů";
        Width = 720;
        Height = 760;
        MinWidth = 580;
        MinHeight = 500;
        WindowStartupLocation =
            WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        SetResourceReference(
            BackgroundProperty,
            "DmsPanelBrush");

        Content =
            BuildContent();

        RebuildTree();
    }

    private UIElement BuildContent()
    {
        var root =
            new Grid
            {
                Margin =
                    new Thickness(12)
            };

        root.RowDefinitions.Add(
            new RowDefinition
            {
                Height = GridLength.Auto
            });

        root.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        root.RowDefinitions.Add(
            new RowDefinition
            {
                Height = GridLength.Auto
            });

        var searchGrid =
            new Grid
            {
                Margin =
                    new Thickness(
                        0,
                        0,
                        0,
                        10)
            };

        searchGrid.ColumnDefinitions.Add(
            new ColumnDefinition());

        searchGrid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = GridLength.Auto
            });

        _search.Height = 30;
        _search.Margin =
            new Thickness(
                0,
                0,
                8,
                0);

        _search.VerticalContentAlignment =
            VerticalAlignment.Center;

        _search.ToolTip =
            "Hledat čítač nebo skupinu";

        _search.TextChanged +=
            (_, _) =>
                RebuildTree();

        searchGrid.Children.Add(
            _search);

        var clear =
            new Button
            {
                Content = "Vymazat",
                MinWidth = 80,
                Height = 30
            };

        clear.Click +=
            (_, _) =>
            {
                _search.Clear();
                _search.Focus();
            };

        Grid.SetColumn(
            clear,
            1);

        searchGrid.Children.Add(
            clear);

        Grid.SetRow(
            searchGrid,
            0);

        root.Children.Add(
            searchGrid);

        var border =
            new Border
            {
                BorderThickness =
                    new Thickness(1),
                Padding =
                    new Thickness(8)
            };

        border.SetResourceReference(
            Border.BorderBrushProperty,
            "DmsBorderBrush");

        border.Child =
            new ScrollViewer
            {
                VerticalScrollBarVisibility =
                    ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility =
                    ScrollBarVisibility.Disabled,
                Content =
                    _itemsPanel
            };

        Grid.SetRow(
            border,
            1);

        root.Children.Add(
            border);

        var footer =
            new StackPanel
            {
                Margin =
                    new Thickness(
                        0,
                        10,
                        0,
                        0)
            };

        var actions =
            new Grid();

        actions.ColumnDefinitions.Add(
            new ColumnDefinition());

        actions.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = GridLength.Auto
            });

        actions.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = GridLength.Auto
            });

        _count.VerticalAlignment =
            VerticalAlignment.Center;

        actions.Children.Add(
            _count);

        var selectAll =
            new Button
            {
                Content = "Vybrat vše",
                MinWidth = 100,
                Height = 30,
                Margin =
                    new Thickness(
                        0,
                        0,
                        6,
                        0)
            };

        selectAll.Click +=
            (_, _) =>
            {
                foreach (var item
                         in _catalog)
                {
                    _selected.Add(
                        item.Name);
                }

                RebuildTree();
            };

        Grid.SetColumn(
            selectAll,
            1);

        actions.Children.Add(
            selectAll);

        var clearAll =
            new Button
            {
                Content = "Zrušit výběr",
                MinWidth = 110,
                Height = 30
            };

        clearAll.Click +=
            (_, _) =>
            {
                _selected.Clear();
                RebuildTree();
            };

        Grid.SetColumn(
            clearAll,
            2);

        actions.Children.Add(
            clearAll);

        footer.Children.Add(
            actions);

        var dialogButtons =
            new StackPanel
            {
                Orientation =
                    Orientation.Horizontal,
                HorizontalAlignment =
                    HorizontalAlignment.Right,
                Margin =
                    new Thickness(
                        0,
                        10,
                        0,
                        0)
            };

        var ok =
            new Button
            {
                Content = "Použít",
                Width = 95,
                Height = 32,
                Margin =
                    new Thickness(
                        0,
                        0,
                        8,
                        0),
                IsDefault = true
            };

        ok.Click +=
            (_, _) =>
                DialogResult = true;

        var cancel =
            new Button
            {
                Content = "Storno",
                Width = 95,
                Height = 32,
                IsCancel = true
            };

        dialogButtons.Children.Add(
            ok);

        dialogButtons.Children.Add(
            cancel);

        footer.Children.Add(
            dialogButtons);

        Grid.SetRow(
            footer,
            2);

        root.Children.Add(
            footer);

        return root;
    }

    private void RebuildTree()
    {
        _updating = true;

        try
        {
            _itemsPanel.Children.Clear();

            var filter =
                _search.Text?.Trim()
                ?? string.Empty;

            foreach (var group
                     in _catalog
                         .GroupBy(
                             item =>
                                 string.IsNullOrWhiteSpace(
                                     item.GroupName)
                                     ? "Ostatní"
                                     : item.GroupName,
                             StringComparer.CurrentCultureIgnoreCase)
                         .OrderBy(
                             item => item.Key,
                             StringComparer.CurrentCultureIgnoreCase))
            {
                var allGroupItems =
                    group.ToList();

                var visibleItems =
                    allGroupItems
                        .Where(item =>
                            string.IsNullOrWhiteSpace(
                                filter)
                            || group.Key.Contains(
                                filter,
                                StringComparison.CurrentCultureIgnoreCase)
                            || item.Name.Contains(
                                filter,
                                StringComparison.CurrentCultureIgnoreCase)
                            || item.Description.Contains(
                                filter,
                                StringComparison.CurrentCultureIgnoreCase))
                        .OrderBy(
                            item => item.Name,
                            StringComparer.CurrentCultureIgnoreCase)
                        .ToList();

                if (visibleItems.Count == 0)
                {
                    continue;
                }

                var selectedCount =
                    allGroupItems.Count(item =>
                        _selected.Contains(
                            item.Name));

                var groupCheck =
                    new CheckBox
                    {
                        Content = group.Key,
                        IsThreeState = true,
                        IsChecked =
                            selectedCount == 0
                                ? false
                                : selectedCount
                                  == allGroupItems.Count
                                    ? true
                                    : null,
                        FontWeight =
                            FontWeights.SemiBold,
                        Margin =
                            new Thickness(
                                0,
                                2,
                                0,
                                4)
                    };

                groupCheck.Click +=
                    (_, _) =>
                    {
                        if (_updating)
                        {
                            return;
                        }

                        var select =
                            groupCheck.IsChecked
                            != false;

                        foreach (var item
                                 in allGroupItems)
                        {
                            if (select)
                            {
                                _selected.Add(
                                    item.Name);
                            }
                            else
                            {
                                _selected.Remove(
                                    item.Name);
                            }
                        }

                        RebuildTree();
                    };

                var panel =
                    new StackPanel();

                foreach (var item
                         in visibleItems)
                {
                    var child =
                        new CheckBox
                        {
                            Content = item.DisplayText,
                            IsChecked =
                                _selected.Contains(
                                    item.Name),
                            Margin =
                                new Thickness(
                                    22,
                                    2,
                                    0,
                                    2)
                        };

                    child.Checked +=
                        (_, _) =>
                        {
                            if (!_updating)
                            {
                                _selected.Add(
                                    item.Name);

                                UpdateCount();
                            }
                        };

                    child.Unchecked +=
                        (_, _) =>
                        {
                            if (!_updating)
                            {
                                _selected.Remove(
                                    item.Name);

                                UpdateCount();
                            }
                        };

                    panel.Children.Add(
                        child);
                }

                _itemsPanel.Children.Add(
                    new Expander
                    {
                        Header = groupCheck,
                        Content = panel,
                        IsExpanded =
                            !string.IsNullOrWhiteSpace(
                                filter),
                        Margin =
                            new Thickness(
                                0,
                                0,
                                0,
                                6)
                    });
            }

            UpdateCount();
        }
        finally
        {
            _updating = false;
        }
    }

    private void UpdateCount()
    {
        _count.Text =
            $"Vybráno {_selected.Count:N0} čítačů";
    }
}
