using DMS.Core.Sap;
using DMS.Desktop.Logging;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace DMS.Desktop.Views.Sap;

public partial class SapRecipeSimilarityView : UserControl
{
    private readonly SapStoragePaths _storagePaths;
    private readonly DmsLogger? _logger;
    private readonly string _currentUserName;
    private readonly Func<string, string>? _translate;
    private readonly Func<string, object[], string>? _translateFormat;

    private SapRecipeSimilarityService? _service;
    private SapRecipeSimilarityAnalysis? _analysis;
    private IReadOnlyList<PairRow> _allRows = Array.Empty<PairRow>();
    private PairRow? _selectedRow;
    private bool _loaded;
    private bool _busy;

    public event Action<string>? TransactionRequested;

    public SapRecipeSimilarityView(
        SapStoragePaths storagePaths,
        DmsLogger? logger = null,
        string? currentUserName = null,
        Func<string, string>? translate = null,
        Func<string, object[], string>? translateFormat = null)
    {
        InitializeComponent();

        _storagePaths = storagePaths ?? throw new ArgumentNullException(nameof(storagePaths));
        _logger = logger;
        _currentUserName = string.IsNullOrWhiteSpace(currentUserName) ? "UNKNOWN" : currentUserName;
        _translate = translate;
        _translateFormat = translateFormat;

        InitializeOptions();
        ApplyLocalization();
        ConfigureColumns();

        Loaded += SapRecipeSimilarityView_Loaded;
    }

    private async void SapRecipeSimilarityView_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await AnalyzeAsync(rebuildProfiles: true);
    }

    private void InitializeOptions()
    {
        CmbMinSimilarity.ItemsSource = new[]
        {
            new NumberOption(70m, "70 %"),
            new NumberOption(80m, "80 %"),
            new NumberOption(90m, "90 %"),
            new NumberOption(95m, "95 %"),
            new NumberOption(100m, "100 %")
        };
        CmbMinSimilarity.DisplayMemberPath = nameof(NumberOption.Text);
        CmbMinSimilarity.SelectedIndex = 1;

        CmbRatioTolerance.ItemsSource = new[]
        {
            new NumberOption(0.1m, "0,1 %"),
            new NumberOption(0.5m, "0,5 %"),
            new NumberOption(1m, "1,0 %"),
            new NumberOption(2m, "2,0 %")
        };
        CmbRatioTolerance.DisplayMemberPath = nameof(NumberOption.Text);
        CmbRatioTolerance.SelectedIndex = 1;
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text = T("REC05.Title", "REC05 - Analýza podobnosti receptur");
        TxtSubtitle.Text = T(
            "REC05.Subtitle",
            "Porovnává receptury 17* podle skutečného složení BOM závodu 2000. Identitu komponent určuje SAP číslo; text je pouze informativní.");
        LblMinSimilarity.Text = T("REC05.Filter.MinSimilarity", "Min. shoda složení:");
        LblRatioTolerance.Text = T("REC05.Filter.RatioTolerance", "Tolerance poměru:");
        LblSearch.Text = T("REC05.Filter.Search", "Hledat:");
        ChkIdentical.Content = T("REC05.Filter.Identical", "Identické");
        ChkSameComponents.Content = T("REC05.Filter.SameComponents", "Stejné komponenty");
        ChkSimilar.Content = T("REC05.Filter.Similar", "Podobné");
        BtnAnalyze.Content = T("REC05.Action.Analyze", "Analyzovat znovu");
        TxtDetailTitle.Text = T("REC05.Detail.Title", "Detail porovnání");
        TxtDetailSubtitle.Text = T("REC05.Detail.Empty", "Vyber dvojici receptur v horním přehledu.");
        BtnOpenA.Content = T("REC05.Action.OpenA", "Otevřít A v REC03");
        BtnOpenB.Content = T("REC05.Action.OpenB", "Otevřít B v REC03");
    }

    private void ConfigureColumns()
    {
        GridPairs.Columns.Clear();
        GridPairs.Columns.Add(Col(T("REC05.Col.Result", "Hodnocení"), nameof(PairRow.KindText), 160));
        GridPairs.Columns.Add(Col(T("REC05.Col.RecipeA", "Receptura A"), nameof(PairRow.RecipeANumber), 125));
        GridPairs.Columns.Add(Col(T("REC05.Col.DescriptionA", "Popis A"), nameof(PairRow.RecipeADescription), 240));
        GridPairs.Columns.Add(Col(T("REC05.Col.RecipeB", "Receptura B"), nameof(PairRow.RecipeBNumber), 125));
        GridPairs.Columns.Add(Col(T("REC05.Col.DescriptionB", "Popis B"), nameof(PairRow.RecipeBDescription), 240));
        GridPairs.Columns.Add(Col(T("REC05.Col.ComponentSimilarity", "Shoda složení"), nameof(PairRow.ComponentSimilarityText), 110));
        GridPairs.Columns.Add(Col(T("REC05.Col.Common", "Společné"), nameof(PairRow.CommonText), 85));
        GridPairs.Columns.Add(Col(T("REC05.Col.RatioDifference", "Odchylka poměru"), nameof(PairRow.RatioDifferenceText), 120));
        GridPairs.Columns.Add(Col(T("REC05.Col.AlternativeA", "Alt. A"), nameof(PairRow.AlternativeA), 70));
        GridPairs.Columns.Add(Col(T("REC05.Col.AlternativeB", "Alt. B"), nameof(PairRow.AlternativeB), 70));

        GridComponents.Columns.Clear();
        GridComponents.Columns.Add(Col(T("REC05.Detail.Col.State", "Rozdíl"), nameof(ComponentRow.StateText), 100));
        GridComponents.Columns.Add(Col(T("REC05.Detail.Col.Component", "Komponenta"), nameof(ComponentRow.ComponentNumber), 130));
        GridComponents.Columns.Add(Col(T("REC05.Detail.Col.Description", "Popis"), nameof(ComponentRow.Description), 280));
        GridComponents.Columns.Add(Col(T("REC05.Detail.Col.QuantityA", "Množství A"), nameof(ComponentRow.QuantityAText), 110));
        GridComponents.Columns.Add(Col(T("REC05.Detail.Col.ShareA", "Podíl A"), nameof(ComponentRow.ShareAText), 90));
        GridComponents.Columns.Add(Col(T("REC05.Detail.Col.FixedA", "Pevné A"), nameof(ComponentRow.FixedAText), 75));
        GridComponents.Columns.Add(Col(T("REC05.Detail.Col.QuantityB", "Množství B"), nameof(ComponentRow.QuantityBText), 110));
        GridComponents.Columns.Add(Col(T("REC05.Detail.Col.ShareB", "Podíl B"), nameof(ComponentRow.ShareBText), 90));
        GridComponents.Columns.Add(Col(T("REC05.Detail.Col.FixedB", "Pevné B"), nameof(ComponentRow.FixedBText), 75));
        GridComponents.Columns.Add(Col(T("REC05.Detail.Col.Difference", "Odchylka"), nameof(ComponentRow.DifferenceText), 95));
    }

    private async Task AnalyzeAsync(bool rebuildProfiles)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        SetControlsEnabled(false);
        TxtWarning.Visibility = Visibility.Collapsed;
        TxtWarning.Text = string.Empty;
        TxtStatus.Text = T("REC05.Status.Loading", "Načítám SAP cache a analyzuji receptury…");

        var minSimilarity = (double)((CmbMinSimilarity.SelectedItem as NumberOption)?.Value ?? 80m);
        var tolerance = (CmbRatioTolerance.SelectedItem as NumberOption)?.Value ?? 0.5m;

        try
        {
            _logger?.Info(
                $"TX_START REC05; MinSimilarity={minSimilarity:0.###}; RatioTolerance={tolerance:0.###}; User={_currentUserName}");

            if (rebuildProfiles || _service is null)
            {
                _service = await Task.Run(() => new SapRecipeSimilarityService(_storagePaths));
            }

            _analysis = await Task.Run(() => _service.Analyze(minSimilarity, tolerance));
            _allRows = _analysis.Pairs.Select(ToPairRow).ToList();

            TxtSummary.Text = TF(
                "REC05.Summary",
                "Receptur: {0:N0} | BOM variant: {1:N0} | Identických dvojic: {2:N0} | Stejné komponenty / jiný poměr: {3:N0} | Podobných: {4:N0}",
                _analysis.RecipeCount,
                _analysis.RecipeVariantCount,
                _analysis.IdenticalPairCount,
                _analysis.SameComponentsPairCount,
                _analysis.SimilarPairCount);

            if (_analysis.Warnings.Count > 0)
            {
                TxtWarning.Text = string.Join("  |  ", _analysis.Warnings);
                TxtWarning.Visibility = Visibility.Visible;
            }

            ApplyDisplayFilter();

            _logger?.Info(
                $"TX_OK REC05; Recipes={_analysis.RecipeCount}; Variants={_analysis.RecipeVariantCount}; " +
                $"Pairs={_analysis.Pairs.Count}; Identical={_analysis.IdenticalPairCount}; " +
                $"SameComponents={_analysis.SameComponentsPairCount}; Similar={_analysis.SimilarPairCount}; User={_currentUserName}");
        }
        catch (Exception ex)
        {
            _analysis = null;
            _allRows = Array.Empty<PairRow>();
            GridPairs.ItemsSource = null;
            GridComponents.ItemsSource = null;
            TxtSummary.Text = string.Empty;
            TxtStatus.Text = T("REC05.Status.Failed", "Analýzu receptur se nepodařilo dokončit.");
            TxtWarning.Text = ex.Message;
            TxtWarning.Visibility = Visibility.Visible;

            _logger?.Info($"TX_FAIL REC05; Error={ex.Message}; User={_currentUserName}");
        }
        finally
        {
            _busy = false;
            SetControlsEnabled(true);
        }
    }

    private void ApplyDisplayFilter()
    {
        var search = (TxtSearch.Text ?? string.Empty).Trim();

        var rows = _allRows.Where(row =>
        {
            if (row.Kind == SapRecipeSimilarityKind.Identical && ChkIdentical.IsChecked != true)
                return false;
            if (row.Kind == SapRecipeSimilarityKind.SameComponentsDifferentRatio && ChkSameComponents.IsChecked != true)
                return false;
            if (row.Kind == SapRecipeSimilarityKind.SimilarComponents && ChkSimilar.IsChecked != true)
                return false;

            if (string.IsNullOrWhiteSpace(search))
                return true;

            return row.RecipeANumber.Contains(search, StringComparison.OrdinalIgnoreCase)
                   || row.RecipeBNumber.Contains(search, StringComparison.OrdinalIgnoreCase)
                   || row.RecipeADescription.Contains(search, StringComparison.OrdinalIgnoreCase)
                   || row.RecipeBDescription.Contains(search, StringComparison.OrdinalIgnoreCase);
        }).ToList();

        GridPairs.ItemsSource = rows;
        TxtStatus.Text = TF(
            "REC05.Status.Filtered",
            "Zobrazeno dvojic: {0:N0} / nalezeno: {1:N0}. Dvojklik otevře recepturu A v REC03; detail umožňuje otevřít obě receptury.",
            rows.Count,
            _allRows.Count);

        if (rows.Count > 0)
        {
            GridPairs.SelectedIndex = 0;
        }
        else
        {
            ClearDetail();
        }
    }

    private PairRow ToPairRow(SapRecipeSimilarityPair pair)
    {
        return new PairRow
        {
            Source = pair,
            Kind = pair.Kind,
            KindText = KindText(pair.Kind),
            RecipeANumber = pair.RecipeANumber,
            RecipeADescription = pair.RecipeADescription,
            RecipeBNumber = pair.RecipeBNumber,
            RecipeBDescription = pair.RecipeBDescription,
            ComponentSimilarityText = pair.ComponentSimilarityPercent.ToString("0.#", CultureInfo.CurrentCulture) + " %",
            CommonText = $"{pair.CommonComponentCount}/{pair.UnionComponentCount}",
            RatioDifferenceText = pair.MaxRatioDifferencePercentagePoints.HasValue
                ? pair.MaxRatioDifferencePercentagePoints.Value.ToString("0.###", CultureInfo.CurrentCulture) + " %"
                : "—",
            AlternativeA = string.IsNullOrWhiteSpace(pair.RecipeAAlternative) ? "—" : pair.RecipeAAlternative,
            AlternativeB = string.IsNullOrWhiteSpace(pair.RecipeBAlternative) ? "—" : pair.RecipeBAlternative
        };
    }

    private string KindText(SapRecipeSimilarityKind kind) => kind switch
    {
        SapRecipeSimilarityKind.Identical => T("REC05.Kind.Identical", "Identické"),
        SapRecipeSimilarityKind.SameComponentsDifferentRatio => T("REC05.Kind.SameComponents", "Stejné komponenty / jiný poměr"),
        _ => T("REC05.Kind.Similar", "Podobné složení")
    };

    private void ShowDetail(PairRow row)
    {
        _selectedRow = row;
        BtnOpenA.IsEnabled = true;
        BtnOpenB.IsEnabled = true;

        TxtDetailTitle.Text = TF(
            "REC05.Detail.PairTitle",
            "{0} ↔ {1}",
            row.RecipeANumber,
            row.RecipeBNumber);

        TxtDetailSubtitle.Text = TF(
            "REC05.Detail.PairSubtitle",
            "{0} | shoda složení {1} | společných komponent {2}",
            row.KindText,
            row.ComponentSimilarityText,
            row.CommonText);

        GridComponents.ItemsSource = row.Source.Components
            .Select(ToComponentRow)
            .OrderBy(x => DifferenceRank(x.DifferenceCode))
            .ThenBy(x => x.ComponentNumber, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private ComponentRow ToComponentRow(SapRecipeComponentComparison source)
    {
        return new ComponentRow
        {
            DifferenceCode = source.DifferenceCode,
            StateText = source.DifferenceCode switch
            {
                "ONLY_A" => T("REC05.Detail.State.OnlyA", "Jen A"),
                "ONLY_B" => T("REC05.Detail.State.OnlyB", "Jen B"),
                _ => T("REC05.Detail.State.Common", "Společná")
            },
            ComponentNumber = source.ComponentNumber,
            Description = source.Description,
            QuantityAText = FormatQuantity(source.QuantityA, source.UnitA),
            ShareAText = FormatPercent(source.ShareA),
            FixedAText = FormatNullableBool(source.IsFixedA),
            QuantityBText = FormatQuantity(source.QuantityB, source.UnitB),
            ShareBText = FormatPercent(source.ShareB),
            FixedBText = FormatNullableBool(source.IsFixedB),
            DifferenceText = FormatPercent(source.DifferencePercentagePoints)
        };
    }

    private void ClearDetail()
    {
        _selectedRow = null;
        GridComponents.ItemsSource = null;
        TxtDetailTitle.Text = T("REC05.Detail.Title", "Detail porovnání");
        TxtDetailSubtitle.Text = T("REC05.Detail.Empty", "Vyber dvojici receptur v horním přehledu.");
        BtnOpenA.IsEnabled = false;
        BtnOpenB.IsEnabled = false;
    }

    private void SetControlsEnabled(bool enabled)
    {
        CmbMinSimilarity.IsEnabled = enabled;
        CmbRatioTolerance.IsEnabled = enabled;
        TxtSearch.IsEnabled = enabled;
        ChkIdentical.IsEnabled = enabled;
        ChkSameComponents.IsEnabled = enabled;
        ChkSimilar.IsEnabled = enabled;
        BtnAnalyze.IsEnabled = enabled;
    }

    private async void BtnAnalyze_Click(object sender, RoutedEventArgs e)
    {
        await AnalyzeAsync(rebuildProfiles: true);
    }

    private async void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loaded && !_busy && _service is not null)
        {
            await AnalyzeAsync(rebuildProfiles: false);
        }
    }

    private void DisplayFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (_loaded && !_busy)
        {
            ApplyDisplayFilter();
        }
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loaded && !_busy)
        {
            ApplyDisplayFilter();
        }
    }

    private void GridPairs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GridPairs.SelectedItem is PairRow row)
        {
            ShowDetail(row);
        }
        else
        {
            ClearDetail();
        }
    }

    private void GridPairs_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (GridPairs.SelectedItem is not PairRow row)
        {
            return;
        }

        e.Handled = true;
        TransactionRequested?.Invoke($"REC03 {row.RecipeANumber}");
    }

    private void BtnOpenA_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedRow is not null)
        {
            TransactionRequested?.Invoke($"REC03 {_selectedRow.RecipeANumber}");
        }
    }

    private void BtnOpenB_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedRow is not null)
        {
            TransactionRequested?.Invoke($"REC03 {_selectedRow.RecipeBNumber}");
        }
    }

    private static DataGridTextColumn Col(string header, string binding, double width) =>
        new()
        {
            Header = header,
            Binding = new Binding(binding),
            Width = new DataGridLength(width)
        };

    private string FormatNullableBool(bool? value)
    {
        if (!value.HasValue)
            return "—";
        return value.Value ? T("Common.Yes", "Ano") : T("Common.No", "Ne");
    }

    private static string FormatQuantity(decimal? value, string unit)
    {
        if (!value.HasValue)
            return "—";

        var number = value.Value.ToString("0.###", CultureInfo.CurrentCulture);
        return string.IsNullOrWhiteSpace(unit) ? number : $"{number} {unit}";
    }

    private static string FormatPercent(decimal? value) =>
        value.HasValue
            ? value.Value.ToString("0.###", CultureInfo.CurrentCulture) + " %"
            : "—";

    private static int DifferenceRank(string code) => code switch
    {
        "ONLY_A" => 0,
        "ONLY_B" => 1,
        _ => 2
    };

    private string T(string key, string fallback)
    {
        var value = _translate?.Invoke(key);
        return IsMissing(value, key) ? fallback : value!;
    }

    private string TF(string key, string fallback, params object[] args)
    {
        if (_translateFormat is not null)
        {
            var translated = _translateFormat(key, args);
            if (!IsMissing(translated, key))
            {
                return translated;
            }
        }

        var pattern = T(key, fallback);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, pattern, args);
        }
        catch
        {
            return pattern;
        }
    }

    private static bool IsMissing(string? value, string key) =>
        string.IsNullOrWhiteSpace(value)
        || string.Equals(value, key, StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, $"[[{key}]]", StringComparison.OrdinalIgnoreCase);

    private sealed record NumberOption(decimal Value, string Text);

    private sealed class PairRow
    {
        public required SapRecipeSimilarityPair Source { get; init; }
        public SapRecipeSimilarityKind Kind { get; init; }
        public string KindText { get; init; } = string.Empty;
        public string RecipeANumber { get; init; } = string.Empty;
        public string RecipeADescription { get; init; } = string.Empty;
        public string RecipeBNumber { get; init; } = string.Empty;
        public string RecipeBDescription { get; init; } = string.Empty;
        public string ComponentSimilarityText { get; init; } = string.Empty;
        public string CommonText { get; init; } = string.Empty;
        public string RatioDifferenceText { get; init; } = string.Empty;
        public string AlternativeA { get; init; } = string.Empty;
        public string AlternativeB { get; init; } = string.Empty;
    }

    private sealed class ComponentRow
    {
        public string DifferenceCode { get; init; } = string.Empty;
        public string StateText { get; init; } = string.Empty;
        public string ComponentNumber { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string QuantityAText { get; init; } = string.Empty;
        public string ShareAText { get; init; } = string.Empty;
        public string FixedAText { get; init; } = string.Empty;
        public string QuantityBText { get; init; } = string.Empty;
        public string ShareBText { get; init; } = string.Empty;
        public string FixedBText { get; init; } = string.Empty;
        public string DifferenceText { get; init; } = string.Empty;
    }
}
