using DMS.Core.Sap;
using DMS.Desktop.Logging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DMS.Desktop.Views.Sap;

public partial class SapMaterialDisplayView : UserControl
{
    private readonly string _materialNumber;
    private readonly SapStoragePaths _storagePaths;
    private readonly SapDecorationRuleService _decorationRuleService;
    private readonly SapMaterialStatusRuleService _statusRuleService;
    private readonly DmsLogger? _logger;
    private readonly string _currentUserName;
    private readonly Func<string, string>? _translate;
    private readonly Func<string, object[], string>? _translateFormat;

    public SapMaterialDisplayView(
        string materialNumber,
        SapStoragePaths storagePaths,
        SapDecorationRuleService decorationRuleService,
        SapMaterialStatusRuleService statusRuleService,
        DmsLogger? logger = null,
        string? currentUserName = null,
        Func<string, string>? translate = null,
        Func<string, object[], string>? translateFormat = null)
    {
        InitializeComponent();

        _materialNumber = materialNumber;
        _storagePaths = storagePaths;
        _decorationRuleService = decorationRuleService;
        _statusRuleService = statusRuleService;
        _logger = logger;
        _currentUserName = string.IsNullOrWhiteSpace(currentUserName) ? "UNKNOWN" : currentUserName;
        _translate = translate;
        _translateFormat = translateFormat;

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Render();
    }

    // ── Render ────────────────────────────────────────────────────────────────

    private void Render()
    {
        RootPanel.Children.Clear();

        RootPanel.Children.Add(CreateTitle(T("SAP03.Title")));
        RootPanel.Children.Add(CreateInfoBar(TF("SAP03.InfoBar", _materialNumber)));

        try
        {
            var repository = new JsonSapMaterialRepository(_storagePaths.SapMaterialsFilePath);
            var material = repository.FindByMaterialNumber(_materialNumber);

            if (material is null)
            {
                _logger?.Warning(
                    $"SAP03: material not found; MaterialNumber={_materialNumber}; User={_currentUserName}; File={_storagePaths.SapMaterialsFilePath}");

                RootPanel.Children.Add(CreateBodyText(
                    TF("SAP03.MaterialNotFound",
                       _materialNumber,
                       _storagePaths.SapMaterialsFilePath)));
                return;
            }

            _logger?.Info(
                $"SAP03: material displayed; MaterialNumber={material.MaterialNumber}; Kind={material.MaterialKind}; User={_currentUserName}");

            RenderBasicData(material);
            RenderGlassInfo(material);
            RenderTechnicalInfo(material);
        }
        catch (Exception ex)
        {
            _logger?.Error(
                $"SAP03: render failed; MaterialNumber={_materialNumber}; User={_currentUserName}",
                ex);

            RootPanel.Children.Add(CreateBodyText(TF("SAP03.RenderFailed", ex.Message)));
        }
    }

    private void RenderBasicData(SapMaterial material)
    {
        RootPanel.Children.Add(CreateSectionHeader(T("SAP03.Section.BasicData")));

        RootPanel.Children.Add(CreateLine(T("SAP03.Field.Material"), material.MaterialNumber));
        RootPanel.Children.Add(CreateLine(T("SAP03.Field.Description"), material.Description));
        RootPanel.Children.Add(CreateLine(T("SAP03.Field.OldNumber"), NullDash(material.OldMaterialNumber)));
        RootPanel.Children.Add(CreateLine(T("SAP03.Field.Status"), FormatStatus(material.MaterialStatus)));
        RootPanel.Children.Add(CreateLine(T("SAP03.Field.Kind"), material.MaterialKind));
        RootPanel.Children.Add(CreateLine(T("SAP03.Field.Prefix"), NullDash(material.TransactionPrefix)));

        if (!string.IsNullOrWhiteSpace(material.ToolFixtureKind))
        {
            RootPanel.Children.Add(CreateLine(T("SAP03.Field.ToolFixtureKind"), material.ToolFixtureKind));
        }
    }

    private void RenderGlassInfo(SapMaterial material)
    {
        if (material.GlassInfo is null)
        {
            return;
        }

        RootPanel.Children.Add(CreateSeparator());
        RootPanel.Children.Add(CreateSectionHeader(T("SAP03.Section.GlassBreakdown")));

        RootPanel.Children.Add(CreateLine(T("SAP03.Field.MoldNumber"), NullDash(material.GlassInfo.MoldNumber)));
        RootPanel.Children.Add(CreateLine(T("SAP03.Field.GlassType"), NullDash(material.GlassInfo.GlassTypeNumber)));
        RootPanel.Children.Add(CreateLine(T("SAP03.Field.Volume"), FormatVolume(material.GlassInfo.VolumeMl)));
        RootPanel.Children.Add(CreateLine(T("SAP03.Field.DecorationChain"), NullDash(material.GlassInfo.DecorationChain)));
        RootPanel.Children.Add(CreateLine(T("SAP03.Field.RemainingDesc"), NullDash(material.GlassInfo.RemainingDescription)));

        RootPanel.Children.Add(CreateSeparator());
        RootPanel.Children.Add(CreateSectionHeader(T("SAP03.Section.DecorationSteps")));

        if (material.GlassInfo.DecorationSteps.Count == 0)
        {
            RootPanel.Children.Add(CreateLine(T("SAP03.Field.Steps"), T("SAP03.DecorationNotRecognized")));
        }
        else
        {
            foreach (var step in material.GlassInfo.DecorationSteps)
            {
                var name = _decorationRuleService.GetName(step);
                RootPanel.Children.Add(CreateLine(step, name));
            }
        }
    }

    private void RenderTechnicalInfo(SapMaterial material)
    {
        RootPanel.Children.Add(CreateSeparator());
        RootPanel.Children.Add(CreateSectionHeader(T("SAP03.Section.TechnicalInfo")));

        RootPanel.Children.Add(CreateLine(T("SAP03.Field.ImportedAt"), material.ImportedAt.ToString("dd.MM.yyyy HH:mm:ss")));
        RootPanel.Children.Add(CreateLine(T("SAP03.Field.CacheFile"), _storagePaths.SapMaterialsFilePath));
    }

    // ── Helpers – formátování ─────────────────────────────────────────────────

    private string FormatStatus(string? code)
    {
        return string.IsNullOrWhiteSpace(code)
            ? "-"
            : _statusRuleService.FormatStatus(code);
    }

    private static string FormatVolume(int? volumeMl)
    {
        return volumeMl.HasValue ? $"{volumeMl.Value} ml" : "-";
    }

    private static string NullDash(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value;
    }

    // ── Helpers – lokalizace ──────────────────────────────────────────────────

    private string T(string key)
    {
        var value = _translate?.Invoke(key) ?? key;
        return IsMissing(value, key) ? key : value;
    }

    private string TF(string key, params object[] args)
    {
        var value = _translateFormat?.Invoke(key, args);
        if (!string.IsNullOrWhiteSpace(value) && !IsMissing(value, key))
        {
            return value;
        }

        var pattern = T(key);
        try { return string.Format(pattern, args); }
        catch { return pattern; }
    }

    private static bool IsMissing(string? value, string key)
    {
        return string.IsNullOrWhiteSpace(value)
               || string.Equals(value, key, StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, $"[[{key}]]", StringComparison.OrdinalIgnoreCase);
    }

    // ── Helpers – UI stavební bloky ───────────────────────────────────────────

    private TextBlock CreateTitle(string text)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 16)
        };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "DmsForegroundBrush");
        return tb;
    }

    private static Border CreateInfoBar(string text)
    {
        var border = new Border
        {
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 0, 12),
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(Color.FromRgb(50, 73, 94)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(92, 125, 155)),
            BorderThickness = new Thickness(1)
        };
        border.Child = new TextBlock
        {
            Text = text,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(230, 242, 255))
        };
        return border;
    }

    private TextBlock CreateBodyText(string text)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 4)
        };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "DmsForegroundBrush");
        return tb;
    }

    private static Border CreateSectionHeader(string text)
    {
        var border = new Border
        {
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 16, 0, 8),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(Color.FromRgb(42, 57, 72)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(86, 112, 137))
        };
        border.Child = new TextBlock
        {
            Text = text,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(207, 230, 255))
        };
        return border;
    }

    private static Grid CreateLine(string label, string value)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var labelBlock = new TextBlock
        {
            Text = label + ":",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(8, 4, 12, 4),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(190, 205, 220))
        };

        var valueBorder = new Border
        {
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(0, 1, 8, 1),
            MinHeight = 24,
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromRgb(31, 42, 53)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(73, 94, 115))
        };
        valueBorder.Child = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(value) ? "-" : value,
            FontSize = 15,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(245, 248, 252))
        };

        Grid.SetColumn(labelBlock, 0);
        Grid.SetColumn(valueBorder, 1);
        grid.Children.Add(labelBlock);
        grid.Children.Add(valueBorder);

        return grid;
    }

    private static Border CreateSeparator()
    {
        return new Border
        {
            Height = 1,
            Margin = new Thickness(0, 14, 0, 10),
            Background = new SolidColorBrush(Color.FromRgb(78, 96, 116))
        };
    }
}