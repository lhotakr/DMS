using DMS.Core.Sap;
using DMS.Desktop.Logging;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.Sap;

public partial class SapCockpitView : UserControl
{
    private readonly SapStoragePaths _storagePaths;
    private readonly string _materialRulesPath;
    private readonly DmsLogger? _logger;
    private readonly string _currentUserName;
    private readonly Func<string, string>? _translate;
    private readonly Func<string, object[], string>? _translateFormat;

    public SapCockpitView(
        SapStoragePaths storagePaths,
        string materialRulesPath,
        DmsLogger? logger = null,
        string? currentUserName = null,
        Func<string, string>? translate = null,
        Func<string, object[], string>? translateFormat = null)
    {
        InitializeComponent();

        _storagePaths = storagePaths;
        _materialRulesPath = materialRulesPath;
        _logger = logger;
        _currentUserName = string.IsNullOrWhiteSpace(currentUserName)
            ? "UNKNOWN"
            : currentUserName;
        _translate = translate;
        _translateFormat = translateFormat;

        ApplyLocalization();
        BuildTabs();
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text = T("SAP00.Title");
        TxtSubtitle.Text = T("SAP00.Subtitle");
        TxtInfo.Text = T("SAP00.Info");
        TxtPaths.Text = TF(
            "SAP00.Paths",
            _storagePaths.RootDirectory,
            _storagePaths.ConfigDirectory,
            _storagePaths.SapMirrorDirectory);
    }

    private void BuildTabs()
    {
        TabsSapCockpit.Items.Clear();

        TabsSapCockpit.Items.Add(CreateTab(
            T("SAP00.Tab.Materials"),
            () => new SapImportView(
                _storagePaths,
                _materialRulesPath,
                translate: key => T(key),
                translateFormat: (key, args) => TF(key, args),
                logAction: LogAction)));

        TabsSapCockpit.Items.Add(CreateTab(
            T("SAP00.Tab.Boms"),
            () => new SapBomImportView(
                _storagePaths,
                translate: key => T(key),
                translateFormat: (key, args) => TF(key, args),
                logAction: LogAction)));

        TabsSapCockpit.Items.Add(CreateTab(
            T("SAP00.Tab.Routings"),
            () => new SapRoutingImportView(
                _storagePaths,
                translate: key => T(key),
                translateFormat: (key, args) => TF(key, args),
                logAction: LogAction)));

        TabsSapCockpit.Items.Add(CreateTab(
            T("SAP00.Tab.WorkCenters"),
            () => new SapWorkCenterImportView(
                _storagePaths,
                translate: key => T(key),
                translateFormat: (key, args) => TF(key, args),
                logAction: LogAction)));

        TabsSapCockpit.Items.Add(CreateTab(
            T("SAP00.Tab.Cache"),
            () => new SapCacheStatusView(
                _storagePaths.RootDirectory,
                _storagePaths.ConfigDirectory,
                translate: key => T(key),
                translateFormat: (key, args) => TF(key, args),
                logAction: LogAction)));
    }

    private TabItem CreateTab(string header, Func<UIElement> contentFactory)
    {
        return new TabItem
        {
            Header = header,
            Content = CreateContentSafely(header, contentFactory)
        };
    }

    private UIElement CreateContentSafely(string title, Func<UIElement> contentFactory)
    {
        try
        {
            return contentFactory();
        }
        catch (Exception ex)
        {
            LogAction("OpenSapTabFailed", $"Tab={title}; Error={ex.Message}");
            return CreatePlaceholder(title, ex.Message);
        }
    }

    private UIElement CreatePlaceholder(string title, string errorMessage)
    {
        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        var panel = new StackPanel
        {
            Margin = new Thickness(16)
        };

        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "DmsForegroundBrush");

        var bodyBlock = new TextBlock
        {
            Text = TF("SAP00.TabOpenFailed", errorMessage),
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap
        };
        bodyBlock.SetResourceReference(TextBlock.ForegroundProperty, "DmsMutedForegroundBrush");

        panel.Children.Add(titleBlock);
        panel.Children.Add(bodyBlock);

        scrollViewer.Content = panel;
        return scrollViewer;
    }

    private void LogAction(string action, string details)
    {
        _logger?.AdminAction(
            "SAP00",
            action,
            _currentUserName,
            details);
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
