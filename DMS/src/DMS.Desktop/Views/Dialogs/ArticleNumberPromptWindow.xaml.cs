using DMS.Core.Sap;
using DMS.Desktop.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DMS.Desktop.Views.Dialogs;

public partial class ArticleNumberPromptWindow : Window
{
    private readonly List<ArticleSelectionRow> _allRows = new();

    private readonly string? _materialKindFilter;
    private readonly string _windowTitle;
    private readonly string _subtitle;

    public string? ArticleNumber { get; private set; }

    public ArticleNumberPromptWindow()
        : this(
            nameof(SapMaterialKind.GlassArticle),
            "Výběr artiklu",
            "Zobrazují se pouze skleněné artikly / flakony.")
    {
    }

    public ArticleNumberPromptWindow(
        string? materialKindFilter,
        string windowTitle,
        string subtitle)
    {
        InitializeComponent();

        _materialKindFilter = materialKindFilter;
        _windowTitle = windowTitle;
        _subtitle = subtitle;

        Title = windowTitle;
        TxtTitle.Text = windowTitle;
        TxtSubtitle.Text = subtitle;

        LoadArticles();
        ApplyFilter();

        TxtSapNumber.Focus();
    }

    private void LoadArticles()
    {
        try
        {
            var storagePaths = new SapStoragePaths(@"Z:\SAP\DMS-db\DEV");
            var repository = new JsonSapMaterialRepository(storagePaths.SapMaterialsFilePath);

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
                .OrderBy(item => item.MaterialNumber)
                .Select(item => new ArticleSelectionRow
                {
                    SapNumber = item.MaterialNumber,
                    Description = item.Description,
                    OldNumber = item.OldMaterialNumber ?? string.Empty,
                    Status = item.MaterialStatus ?? string.Empty,
                    MaterialKind = item.MaterialKind,
                    TransactionPrefix = item.TransactionPrefix,
                    Decoration = item.GlassInfo?.DecorationChain ?? string.Empty,
                    ExtraInfo = BuildExtraInfo(item)
                }));

            TxtStatus.Text = $"Načteno: {_allRows.Count}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Nepodařilo se načíst SAP materiály ze SAP mirror cache.\n\n" +
                ex.Message,
                "Výběr SAP materiálu",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            TxtStatus.Text = "Materiály se nepodařilo načíst.";
        }
    }

    private void ApplyFilter()
    {
        var sapNumber = TxtSapNumber.Text.Trim();
        var description = TxtDescription.Text.Trim();
        var oldNumber = TxtOldNumber.Text.Trim();
        var decoration = TxtDecoration.Text.Trim();

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

        if (!string.IsNullOrWhiteSpace(decoration))
        {
            filtered = filtered.Where(item =>
                item.Decoration.Contains(decoration, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(decoration))
        {
            filtered = filtered.Where(item =>
                item.Decoration.Contains(decoration, StringComparison.OrdinalIgnoreCase)
                || item.ExtraInfo.Contains(decoration, StringComparison.OrdinalIgnoreCase)
                || item.MaterialKind.Contains(decoration, StringComparison.OrdinalIgnoreCase)
                || item.TransactionPrefix.Contains(decoration, StringComparison.OrdinalIgnoreCase));
        }

        var rows = filtered
            .Take(500)
            .ToList();

        DgArticles.ItemsSource = rows;

        TxtStatus.Text =
            $"Zobrazeno: {rows.Count} / celkem: {_allRows.Count}" +
            (_allRows.Count > 500 ? " (omezeno na prvních 500 výsledků)" : string.Empty);

        if (rows.Count > 0)
        {
            DgArticles.SelectedIndex = 0;
        }
    }

    private void Filter_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    private void BtnSearch_Click(object sender, RoutedEventArgs e)
    {
        ApplyFilter();
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        ConfirmSelection();
    }

    private void DgArticles_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ConfirmSelection();
    }

    private void ConfirmSelection()
    {
        if (DgArticles.SelectedItem is not ArticleSelectionRow selectedRow)
        {
            MessageBox.Show(
                "Vyber artikl ze seznamu.",
                "Výběr artiklu",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        ArticleNumber = selectedRow.SapNumber;
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Filter_KeyDown(object sender, KeyEventArgs e)
    {
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

    private static string BuildExtraInfo(SapMaterial item)
    {
        if (item.GlassInfo is not null)
        {
            return item.GlassInfo.DecorationChain ?? string.Empty;
        }

        if (item.PackagingInfo is not null)
        {
            return item.PackagingInfo.PackagingKind switch
            {
                "PackagingSetOldReference" => $"Sada → staré č. {item.PackagingInfo.LinkedArticleOldNumber}",
                "PackagingSetSapReference" => $"Sada → SAP {item.PackagingInfo.LinkedArticleSapNumber}",
                "PackagingComponent" => "Komponenta balení",
                _ => item.PackagingInfo.PackagingKind
            };
        }

        if (!string.IsNullOrWhiteSpace(item.ToolFixtureKind))
        {
            return item.ToolFixtureKind;
        }

        return item.TransactionPrefix;
    }
}