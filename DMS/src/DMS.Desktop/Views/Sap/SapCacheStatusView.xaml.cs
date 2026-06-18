using DMS.Core.Sap.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.Sap;

public partial class SapCacheStatusView : UserControl
{
    private readonly SapCacheStatusService _service;
    private SapCacheStatusOverview? _overview;

    public SapCacheStatusView()
    {
        InitializeComponent();

        var basePath = @"Z:\SAP\DMS-db\DEV";
        var configPath = System.IO.Path.Combine(basePath, "Config");

        _service = new SapCacheStatusService(basePath, configPath);

        LoadOverview();
    }

    private void LoadOverview()
    {
        _overview = _service.BuildOverview();

        GridCacheStatus.ItemsSource = _overview.Rows;

        TxtSummary.Text =
            $"DMS pracuje nad lokální read-only SAP cache: {_overview.BasePath}\n" +
            $"Souborů OK: {_overview.ExistingFiles}, chybí: {_overview.MissingFiles}, " +
            $"vygenerováno: {_overview.CreatedAt:dd.MM.yyyy HH:mm:ss}";
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        LoadOverview();
    }

    private void BtnCopy_Click(object sender, RoutedEventArgs e)
    {
        if (_overview is null)
        {
            return;
        }

        var lines = _overview.Rows.Select(row =>
            $"{row.Area}\t{row.Status}\t{row.CountText}\t{row.LastChangedText}\t{row.Path}");

        Clipboard.SetText(string.Join(Environment.NewLine, lines));
    }
}