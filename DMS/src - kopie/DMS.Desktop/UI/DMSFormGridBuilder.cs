using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.UI;

public sealed class DmsFormGridBuilder
{
    private readonly Grid _grid;

    public DmsFormGridBuilder(int columnCount)
    {
        if (columnCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(columnCount));
        }

        _grid = new Grid();

        for (var i = 0; i < columnCount; i++)
        {
            _grid.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(
                        1,
                        GridUnitType.Star)
                });
        }
    }

    public DmsFormGridBuilder AddField(
        int row,
        int column,
        string label,
        string? value)
    {
        EnsureRow(row);

        var margin = new Thickness(
            column == 0 ? 0 : 5,
            0,
            column == _grid.ColumnDefinitions.Count - 1 ? 0 : 5,
            8);

        var field = DmsUiFactory.CreateField(
            label,
            value,
            margin);

        Grid.SetRow(field, row);
        Grid.SetColumn(field, column);

        _grid.Children.Add(field);

        return this;
    }

    public Grid Build()
    {
        return _grid;
    }

    private void EnsureRow(int row)
    {
        while (_grid.RowDefinitions.Count <= row)
        {
            _grid.RowDefinitions.Add(
                new RowDefinition
                {
                    Height = GridLength.Auto
                });
        }
    }
}