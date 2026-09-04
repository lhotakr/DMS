using System.Windows;

namespace DMS.Desktop.Views.Mes;

public partial class MesReportingView
{
    private bool _mes06FilterPanelCollapsed;
    private double _mes06FilterPanelExpandedWidth =
        315d;

    private void BtnToggleFilters_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        UpdateFilterPanelToggleText();
    }

    private void BtnToggleFilters_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!_mes06FilterPanelCollapsed
            && FilterColumn.ActualWidth > 40d)
        {
            _mes06FilterPanelExpandedWidth =
                FilterColumn.ActualWidth;
        }

        _mes06FilterPanelCollapsed =
            !_mes06FilterPanelCollapsed;

        if (_mes06FilterPanelCollapsed)
        {
            FilterPanel.Visibility =
                Visibility.Collapsed;

            FilterSplitter.Visibility =
                Visibility.Collapsed;

            FilterColumn.Width =
                new GridLength(
                    0d);

            FilterSplitterColumn.Width =
                new GridLength(
                    0d);
        }
        else
        {
            FilterColumn.Width =
                new GridLength(
                    Math.Max(
                        250d,
                        _mes06FilterPanelExpandedWidth));

            FilterSplitterColumn.Width =
                new GridLength(
                    8d);

            FilterPanel.Visibility =
                Visibility.Visible;

            FilterSplitter.Visibility =
                Visibility.Visible;
        }

        UpdateFilterPanelToggleText();
    }

    private void UpdateFilterPanelToggleText()
    {
        TxtToggleFilters.Text =
            _mes06FilterPanelCollapsed
                ? T(
                    "MES06.FilterPanel.Show",
                    "Show filters")
                : T(
                    "MES06.FilterPanel.Hide",
                    "Hide filters");
    }
}
