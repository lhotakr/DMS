using DMS.Desktop.Configuration;
using DMS.Core.Sap.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.Sap;

public partial class SapCacheStatusView : UserControl
{
    private readonly SapCacheStatusService _service;
    private readonly Func<string, string>? _translate;
    private readonly Func<string, object[], string>? _translateFormat;
    private readonly Action<string, string>? _logAction;
    private SapCacheStatusOverview? _overview;

    public SapCacheStatusView()
        : this(
            DmsStoragePathPolicy.GetEnvironmentRoot("DEV"),
            System.IO.Path.Combine(DmsStoragePathPolicy.GetEnvironmentRoot("DEV"), "Config"))
    {
    }

    public SapCacheStatusView(
        string basePath,
        string configPath,
        Func<string, string>? translate = null,
        Func<string, object[], string>? translateFormat = null,
        Action<string, string>? logAction = null)
    {
        InitializeComponent();

        _translate = translate;
        _translateFormat = translateFormat;
        _logAction = logAction;
        _service = new SapCacheStatusService(basePath, configPath);

        ApplyLocalization();
        LoadOverview();
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text = T("SAP00.Cache.Title");
        BtnRefresh.Content = T("SAP00.Cache.Refresh");
        BtnCopy.Content = T("SAP00.Cache.Copy");

        ColArea.Header = T("SAP00.Cache.Column.Area");
        ColFile.Header = T("SAP00.Cache.Column.File");
        ColStatus.Header = T("SAP00.Cache.Column.Status");
        ColCount.Header = T("SAP00.Cache.Column.Count");
        ColLastChanged.Header = T("SAP00.Cache.Column.LastChanged");
        ColPath.Header = T("SAP00.Cache.Column.Path");
    }

    private void LoadOverview()
    {
        _overview = _service.BuildOverview();

        GridCacheStatus.ItemsSource = _overview.Rows;

        TxtSummary.Text = TF(
            "SAP00.Cache.Summary",
            _overview.BasePath,
            _overview.ExistingFiles,
            _overview.MissingFiles,
            _overview.CreatedAt.ToString("dd.MM.yyyy HH:mm:ss"));

        _logAction?.Invoke(
            "RefreshCacheOverview",
            $"BasePath={_overview.BasePath}; ExistingFiles={_overview.ExistingFiles}; MissingFiles={_overview.MissingFiles}");
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

        _logAction?.Invoke(
            "CopyCacheOverview",
            $"Rows={_overview.Rows.Count}");
    }

    private string T(string key)
    {
        var value = _translate?.Invoke(key) ?? key;

        return IsMissing(value, key)
            ? key
            : value;
    }

    private string TF(string key, params object[] args)
    {
        var value = _translateFormat?.Invoke(key, args);

        if (!string.IsNullOrWhiteSpace(value) && !IsMissing(value, key))
        {
            return value;
        }

        var pattern = T(key);

        try
        {
            return string.Format(pattern, args);
        }
        catch
        {
            return pattern;
        }
    }

    private static bool IsMissing(string? value, string key)
    {
        return string.IsNullOrWhiteSpace(value)
               || string.Equals(value, key, StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, $"[[{key}]]", StringComparison.OrdinalIgnoreCase);
    }
}
