using DMS.Core.Sap;
using DMS.Desktop.Logging;
using DMS.Desktop.UI;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DMS.Desktop.Views.Dialogs;

public partial class ArticleNumberPromptWindow : Window
{
    private const int PreviewLimit = 500;

    private readonly List<ArticleSelectionRow> _allRows = new();
    private readonly SapStoragePaths _storagePaths;
    private readonly DmsLogger? _logger;
    private readonly string _currentUserName;
    private readonly Func<string, string>? _translate;
    private readonly Func<string, object[], string>? _translateFormat;

    private readonly string? _materialKindFilter;
    private readonly string _windowTitleOrKey;
    private readonly string _subtitleOrKey;

    private string _resolvedMaterialsFilePath = string.Empty;
    private bool _isApplyingLocalization;

    public string? ArticleNumber { get; private set; }

    public ArticleNumberPromptWindow()
        : this(
            nameof(SapMaterialKind.GlassArticle),
            "ArticleSelection.Default.Title",
            "ArticleSelection.Default.Subtitle",
            new SapStoragePaths(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."))))
    {
    }

    public ArticleNumberPromptWindow(
        string? materialKindFilter,
        string windowTitle,
        string subtitle)
        : this(
            materialKindFilter,
            windowTitle,
            subtitle,
            new SapStoragePaths(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."))))
    {
    }

    public ArticleNumberPromptWindow(
        string? materialKindFilter,
        string windowTitleOrKey,
        string subtitleOrKey,
        SapStoragePaths storagePaths,
        DmsLogger? logger = null,
        string? currentUserName = null,
        Func<string, string>? translate = null,
        Func<string, object[], string>? translateFormat = null)
    {
        InitializeComponent();

        _materialKindFilter = materialKindFilter;
        _windowTitleOrKey = windowTitleOrKey;
        _subtitleOrKey = subtitleOrKey;
        _storagePaths = storagePaths;
        _logger = logger;
        _currentUserName = string.IsNullOrWhiteSpace(currentUserName)
            ? "UNKNOWN"
            : currentUserName;
        _translate = translate;
        _translateFormat = translateFormat;

        ApplyLocalization();
        LoadArticles();
        ApplyFilter();

        TxtSapNumber.Focus();
    }

    private void ApplyLocalization()
    {
        _isApplyingLocalization = true;

        try
        {
            var title = T(_windowTitleOrKey);
            var subtitle = T(_subtitleOrKey);

            Title = title;
            TxtTitle.Text = title;
            TxtSubtitle.Text = subtitle;

            TxtSapNumberLabel.Text = T("ArticleSelection.Filter.SapNumber");
            TxtOldNumberLabel.Text = T("ArticleSelection.Filter.OldNumber");
            TxtDescriptionLabel.Text = T("ArticleSelection.Filter.Description");
            TxtInfoLabel.Text = T("ArticleSelection.Filter.Info");

            BtnSearch.Content = T("Common.Search");
            BtnClear.Content = T("Common.Clear");
            BtnCancel.Content = T("Common.Cancel");
            BtnOk.Content = T("Common.Select");

            ColSapNumber.Header = T("ArticleSelection.Column.SapNumber");
            ColDescription.Header = T("ArticleSelection.Column.Description");
            ColOldNumber.Header = T("ArticleSelection.Column.OldNumber");
            ColStatus.Header = T("ArticleSelection.Column.Status");
            ColMaterialKind.Header = T("ArticleSelection.Column.MaterialKind");
            ColInfo.Header = T("ArticleSelection.Column.Info");

            UpdateCachePathText();
        }
        finally
        {
            _isApplyingLocalization = false;
        }
    }

    private void LoadArticles()
    {
        try
        {
            _storagePaths.EnsureDirectories();
            _resolvedMaterialsFilePath = ResolveMaterialsFilePath();
            UpdateCachePathText();

            var repository = new JsonSapMaterialRepository(_resolvedMaterialsFilePath);
            var materials = repository.LoadAll().AsEnumerable();

            if (!string.IsNullOrWhiteSpace(_materialKindFilter))
            {
                materials = materials.Where(item =>
                    string.Equals(
                        item.MaterialKind,
                        _materialKindFilter,
                        StringComparison.OrdinalIgnoreCase));
            }

            _allRows.Clear();

            _allRows.AddRange(materials
                .OrderByDescending(item => item.ImportedAt)
                .ThenByDescending(item => item.MaterialNumber)
                .Select(item => new ArticleSelectionRow
                {
                    SapNumber = item.MaterialNumber,
                    Description = item.Description,
                    OldNumber = item.OldMaterialNumber ?? string.Empty,
                    Status = item.MaterialStatus ?? string.Empty,
                    MaterialKind = item.MaterialKind,
                    MaterialKindDisplay = GetMaterialKindDisplayName(item.MaterialKind),
                    TransactionPrefix = item.TransactionPrefix,
                    Decoration = item.GlassInfo?.DecorationChain ?? string.Empty,
                    ExtraInfo = BuildExtraInfo(item)
                }));

            SetStatus(TF("ArticleSelection.Status.Loaded", _allRows.Count));

            _logger?.AdminAction(
                "ArticleSelection",
                "LoadMaterials",
                _currentUserName,
                $"MaterialKind={_materialKindFilter ?? "ALL"}; Count={_allRows.Count}; Path={_resolvedMaterialsFilePath}");
        }
        catch (Exception ex)
        {
            _logger?.Error(
                $"Article selection failed to load SAP materials. Path={_resolvedMaterialsFilePath}",
                ex);

            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("ArticleSelection.Dialog.LoadFailed.Title"),
                TF("ArticleSelection.Dialog.LoadFailed.Message", ex.Message, _resolvedMaterialsFilePath));

            SetStatus(T("ArticleSelection.Status.LoadFailed"));
        }
    }

    private string ResolveMaterialsFilePath()
    {
        var candidates = new[]
            {
                _storagePaths.SapMaterialsFilePath,
                Path.Combine(_storagePaths.RootDirectory, "Data", "sap-materials.json")
            }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var existing = candidates.FirstOrDefault(File.Exists);
        return existing ?? candidates.First();
    }

    private void ApplyFilter()
    {
        if (_isApplyingLocalization)
        {
            return;
        }

        var sapNumber = TxtSapNumber.Text.Trim();
        var description = TxtDescription.Text.Trim();
        var oldNumber = TxtOldNumber.Text.Trim();
        var info = TxtInfo.Text.Trim();

        var filtered = _allRows.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(sapNumber))
        {
            filtered = filtered.Where(item =>
                item.SapNumber.Contains(sapNumber, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(description))
        {
            filtered = filtered.Where(item =>
                item.Description.Contains(description, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(oldNumber))
        {
            filtered = filtered.Where(item =>
                item.OldNumber.Contains(oldNumber, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(info))
        {
            filtered = filtered.Where(item =>
                ContainsAny(item, info));
        }

        var fullCount = filtered.Count();
        var rows = filtered
            .Take(PreviewLimit)
            .ToList();

        DgArticles.ItemsSource = rows;

        SetStatus(fullCount > PreviewLimit
            ? TF("ArticleSelection.Status.FilteredLimited", rows.Count, fullCount, PreviewLimit)
            : TF("ArticleSelection.Status.Filtered", rows.Count, _allRows.Count));

        if (rows.Count > 0)
        {
            DgArticles.SelectedIndex = 0;
            DgArticles.ScrollIntoView(rows[0]);
        }
    }

    private static bool ContainsAny(ArticleSelectionRow item, string filter)
    {
        return item.Decoration.Contains(filter, StringComparison.OrdinalIgnoreCase)
               || item.ExtraInfo.Contains(filter, StringComparison.OrdinalIgnoreCase)
               || item.MaterialKind.Contains(filter, StringComparison.OrdinalIgnoreCase)
               || item.MaterialKindDisplay.Contains(filter, StringComparison.OrdinalIgnoreCase)
               || item.TransactionPrefix.Contains(filter, StringComparison.OrdinalIgnoreCase)
               || item.Status.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private void Filter_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    private void BtnSearch_Click(object sender, RoutedEventArgs e)
    {
        ApplyFilter();
    }

    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        TxtSapNumber.Text = string.Empty;
        TxtOldNumber.Text = string.Empty;
        TxtDescription.Text = string.Empty;
        TxtInfo.Text = string.Empty;
        ApplyFilter();
        TxtSapNumber.Focus();
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        ConfirmSelection();
    }

    private void DgArticles_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var rowEl = FindParent<DataGridRow>((DependencyObject)e.OriginalSource);
        if (rowEl is null)
        {
            return;
        }

        ConfirmSelection();
    }

    private void ConfirmSelection()
    {
        if (DgArticles.SelectedItem is ArticleSelectionRow selectedRow)
        {
            ArticleNumber = selectedRow.SapNumber;

            _logger?.AdminAction(
                "ArticleSelection",
                "SelectMaterial",
                _currentUserName,
                $"Material={selectedRow.SapNumber}; MaterialKind={selectedRow.MaterialKind}");

            DialogResult = true;
            Close();
            return;
        }

        var typedSapNumber = TxtSapNumber.Text.Trim();

        if (LooksLikeSapMaterialNumber(typedSapNumber))
        {
            ArticleNumber = typedSapNumber;

            _logger?.AdminAction(
                "ArticleSelection",
                "SelectTypedMaterial",
                _currentUserName,
                $"Material={typedSapNumber}; MaterialKind={_materialKindFilter ?? "ALL"}");

            DialogResult = true;
            Close();
            return;
        }

        DmsConfirmDialog.ShowInfo(
            Window.GetWindow(this),
            T("ArticleSelection.Dialog.NoSelection.Title"),
            T("ArticleSelection.Dialog.NoSelection.Message"));
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        _logger?.AdminAction(
            "ArticleSelection",
            "CancelMaterialSelection",
            _currentUserName,
            $"MaterialKind={_materialKindFilter ?? "ALL"}");

        DialogResult = false;
        Close();
    }

    private void Filter_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            BtnClear_Click(sender, e);
            return;
        }

        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;

        if (DgArticles.Items.Count == 1)
        {
            DgArticles.SelectedIndex = 0;
            ConfirmSelection();
            return;
        }

        ApplyFilter();
    }

    private void UpdateCachePathText()
    {
        if (TxtCachePath is null)
        {
            return;
        }

        var path = string.IsNullOrWhiteSpace(_resolvedMaterialsFilePath)
            ? _storagePaths.SapMaterialsFilePath
            : _resolvedMaterialsFilePath;

        TxtCachePath.Text = TF("ArticleSelection.CachePath", path);
    }

    private string BuildExtraInfo(SapMaterial item)
    {
        if (item.GlassInfo is not null)
        {
            return item.GlassInfo.DecorationChain ?? string.Empty;
        }

        if (item.PackagingInfo is not null)
        {
            return item.PackagingInfo.PackagingKind switch
            {
                "PackagingSetOldReference" => TF(
                    "ArticleSelection.Extra.PackagingSetOldReference",
                    item.PackagingInfo.LinkedArticleOldNumber ?? string.Empty),

                "PackagingSetSapReference" => TF(
                    "ArticleSelection.Extra.PackagingSetSapReference",
                    item.PackagingInfo.LinkedArticleSapNumber ?? string.Empty),

                "PackagingComponent" => T("ArticleSelection.Extra.PackagingComponent"),
                _ => item.PackagingInfo.PackagingKind
            };
        }

        if (!string.IsNullOrWhiteSpace(item.ToolFixtureKind))
        {
            return item.ToolFixtureKind;
        }

        return item.TransactionPrefix;
    }

    private string GetMaterialKindDisplayName(string? materialKind)
    {
        if (string.IsNullOrWhiteSpace(materialKind))
        {
            return string.Empty;
        }

        var key = $"ArticleSelection.MaterialKind.{materialKind}";
        var translated = T(key);

        return IsMissing(translated, key)
            ? materialKind
            : translated;
    }

    private void SetStatus(string text)
    {
        TxtStatus.Text = text;
    }

    private static bool LooksLikeSapMaterialNumber(string value)
    {
        return value.Length == 10 && value.All(char.IsDigit);
    }

    private string T(string key)
    {
        var value = _translate?.Invoke(key) ?? key;
        return IsMissing(value, key) ? key : value;
    }

    private string TF(string key, params object[] args)
    {
        if (_translateFormat is not null)
        {
            var translated = _translateFormat.Invoke(key, args);
            if (!IsMissing(translated, key))
            {
                return translated;
            }
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

    private static T? FindParent<T>(DependencyObject child)
        where T : DependencyObject
    {
        var parent = System.Windows.Media.VisualTreeHelper.GetParent(child);

        while (parent is not null)
        {
            if (parent is T typedParent)
            {
                return typedParent;
            }

            parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
        }

        return null;
    }

    private sealed class ArticleSelectionRow
    {
        public string SapNumber { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string OldNumber { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string MaterialKind { get; set; } = string.Empty;
        public string MaterialKindDisplay { get; set; } = string.Empty;
        public string TransactionPrefix { get; set; } = string.Empty;
        public string Decoration { get; set; } = string.Empty;
        public string ExtraInfo { get; set; } = string.Empty;
    }
}
