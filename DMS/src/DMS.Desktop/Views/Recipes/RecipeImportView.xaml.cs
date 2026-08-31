using DMS.Core.Recipes;
using DMS.Core.Sap;
using DMS.Desktop.Logging;
using DMS.Desktop.Services.Recipes;
using DMS.Desktop.UI;
using Microsoft.Win32;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.Recipes;

public partial class RecipeImportView : UserControl
{
    private readonly SapStoragePaths _storagePaths;
    private readonly DmsLogger _logger;
    private readonly string _user;
    private readonly bool _canEditSettings;
    private readonly Func<string, string>? _translate;

    private readonly RecipeDocumentParser _parser = new();
    private readonly RecipeNormalizationService _normalizer = new();
    private readonly RecipeComponentMatcher _matcher = new();
    private readonly RecipeImportOutputService _output = new();
    private readonly RecipeImportSettingsService _settingsService;
    private readonly IReadOnlyList<SapMaterial> _sapMaterials;

    private RecipeImportSettings _settings;
    private RecipeImportResult? _sprayResult;
    private RecipeImportResult? _screenResult;
    private RecipeComponent? _selectedComponent;
    private RecipeImportKind _selectedComponentKind;

    private TextBox? _candidateFilter;
    private DataGrid? _candidateGrid;
    private Button? _assignButton;

    public RecipeImportView(
        SapStoragePaths storagePaths,
        DmsLogger logger,
        string user,
        bool canEditSettings,
        Func<string, string>? translate = null)
    {
        InitializeComponent();

        _storagePaths = storagePaths;
        _logger = logger;
        _user = user;
        _canEditSettings = canEditSettings;
        _translate = translate;

        _settingsService = new RecipeImportSettingsService(
            Path.Combine(_storagePaths.ConfigDirectory, "rec04-recipe-import-settings.json"));
        _settings = _settingsService.Load();
        _sapMaterials = new JsonSapMaterialRepository(_storagePaths.SapMaterialsFilePath).LoadAll();

        BuildAssignmentPanels();
        LoadSettingsToUi();
        ApplyLocalization();

        _logger.AdminAction(
            "REC04",
            "OpenRecipeImport",
            _user,
            $"Materials={_sapMaterials.Count}; Settings={_settingsService.Path}; CanEditSettings={_canEditSettings}");
    }

    private string T(string key, string fallback)
    {
        var value = _translate?.Invoke(key);
        return string.IsNullOrWhiteSpace(value) || value == key || value == $"[[{key}]]"
            ? fallback
            : value;
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text = T("REC04.Title", "REC04 — Import receptur");
        TxtSubtitle.Text = T("REC04.Subtitle", "Automatické načtení, SAP přiřazení a normalizace receptur na základní množství 1 kg.");
        TabSpray.Header = T("REC04.Tab.Spray", "Laky / postřik");
        TabScreen.Header = T("REC04.Tab.Screen", "Barvy / sítotisk");
        TabSettings.Header = T("REC04.Tab.Settings", "Nastavení");

        BtnLoadSpray.Content = T("REC04.Button.Load", "Načíst PDF / DOCX");
        BtnLoadScreen.Content = T("REC04.Button.Load", "Načíst PDF / DOCX");
        BtnSaveSpray.Content = T("REC04.Button.Save", "Uložit výsledek");
        BtnSaveScreen.Content = T("REC04.Button.Save", "Uložit výsledek");
        BtnExportSpray.Content = T("REC04.Button.Export", "Export Excel");
        BtnExportScreen.Content = T("REC04.Button.Export", "Export Excel");

        LblSprayArticle.Text = T("REC04.Field.Article", "Artikl:");
        LblSprayHd.Text = T("REC04.Field.HdNumber", "HD-Nummer:");
        LblSprayColor.Text = T("REC04.Field.Color", "Barva:");
        LblRecipeNumber.Text = T("REC04.Field.Recipe", "Receptura:");
        LblRecipeKText.Text = T("REC04.Field.KText", "KText:");
        LblRecipeTotal.Text = T("REC04.Field.FinalTotal", "Celkem BOM:");

        ColSprayLayer.Header = T("REC04.Col.Layer", "Vrstva");
        ColSprayKText.Header = T("REC04.Col.KText", "KText");
        ColSprayType.Header = T("REC04.Col.Type", "Typ");
        ColSprayItems.Header = T("REC04.Col.Items", "Položky");

        foreach (var column in new[] { ColSourceTextS, ColSourceTextP }) column.Header = T("REC04.Col.SourceText", "Zdrojový text");
        foreach (var column in new[] { ColSourceGramsS, ColSourceGramsP }) column.Header = T("REC04.Col.SourceGrams", "Zdroj g");
        foreach (var column in new[] { ColBomGramsS, ColBomGramsP }) column.Header = T("REC04.Col.BomGrams", "Na 1 kg [g]");
        foreach (var column in new[] { ColSapNumberS, ColSapNumberP }) column.Header = T("REC04.Col.SapNumber", "SAP komponenta");
        foreach (var column in new[] { ColSapTextS, ColSapTextP }) column.Header = T("REC04.Col.SapText", "SAP text");
        foreach (var column in new[] { ColMatchS, ColMatchP }) column.Header = T("REC04.Col.Match", "Shoda");
        ColHardenerP.Header = T("REC04.Col.Hardener", "Tvrdidlo");
        ColHardenerStatusP.Header = T("REC04.Col.HardenerStatus", "Stav tvrdidla");
        ColRuleP.Header = T("REC04.Col.Rule", "Pravidlo");

        LblComponentPrefix.Text = T("REC04.Settings.ComponentPrefix", "SAP prefix komponent:");
        LblMatchThreshold.Text = T("REC04.Settings.MatchThreshold", "Automatická shoda:");
        ChkAddMissingHardener.Content = T("REC04.Settings.AddMissingHardener", "Doplnit tvrdidlo, pokud v receptuře chybí");
        LblHardenerTolerance.Text = T("REC04.Settings.HardenerTolerance", "Tolerance tvrdidla [p. b.]:");
        GroupHardenerRules.Header = T("REC04.Settings.HardenerRules", "Pravidla tvrdidel");
        GroupAliases.Header = T("REC04.Settings.Aliases", "Naučené aliasy");
        ColRuleFamily.Header = T("REC04.Settings.Family", "Řada");
        ColRuleRatio.Header = T("REC04.Settings.Ratio", "Přídavek [%]");
        ColRuleHardener.Header = T("REC04.Settings.Hardener", "Tvrdidlo");
        ColRuleActive.Header = T("REC04.Settings.Active", "Aktivní");
        ColAliasSource.Header = T("REC04.Col.SourceText", "Zdrojový text");
        ColAliasSap.Header = T("REC04.Col.SapNumber", "SAP komponenta");
        ColAliasText.Header = T("REC04.Col.SapText", "SAP text");
        BtnDeleteRule.Content = T("REC04.Button.DeleteRule", "Odstranit pravidlo");
        BtnDeleteAlias.Content = T("REC04.Button.DeleteAlias", "Odstranit alias");
        BtnSaveSettings.Content = T("REC04.Button.SaveSettings", "Uložit nastavení");

        if (!_canEditSettings)
        {
            TxtSettingsStatus.Text = T("REC04.Settings.AdminOnly", "Nastavení může měnit pouze DMS_ADMIN.");
        }
    }

    private void BuildAssignmentPanels()
    {
        SprayAssignmentHost.Content = BuildAssignmentPanel();
        ScreenAssignmentHost.Content = BuildAssignmentPanel();
    }

    private UIElement BuildAssignmentPanel()
    {
        var border = new Border
        {
            Padding = new Thickness(8),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        border.SetResourceReference(Border.BorderBrushProperty, "DmsBorderBrush");
        border.SetResourceReference(Border.BackgroundProperty, "DmsBackgroundBrush");

        var grid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(220),
            MinWidth = 160
        });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
            MinWidth = 420
        });
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(145),
            MinWidth = 135
        });

        var filter = new TextBox
        {
            Margin = new Thickness(0),
            MinWidth = 160,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        filter.TextChanged += CandidateFilter_TextChanged;
        Grid.SetColumn(filter, 0);
        grid.Children.Add(filter);

        var candidates = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            CanUserResizeColumns = true,
            CanUserReorderColumns = true,
            CanUserSortColumns = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinWidth = 420,
            MinHeight = 145,
            MaxHeight = 185,
            Margin = new Thickness(0)
        };

        candidates.Columns.Add(new DataGridTextColumn
        {
            Header = T("REC04.Col.SapNumber", "SAP komponenta"),
            Binding = new System.Windows.Data.Binding("MaterialNumber"),
            Width = 150,
            MinWidth = 130
        });

        candidates.Columns.Add(new DataGridTextColumn
        {
            Header = T("REC04.Col.SapText", "SAP text"),
            Binding = new System.Windows.Data.Binding("Description"),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            MinWidth = 240
        });

        candidates.Columns.Add(new DataGridTextColumn
        {
            Header = T("REC04.Col.Match", "Shoda"),
            Binding = new System.Windows.Data.Binding("ScoreText"),
            Width = 90,
            MinWidth = 80
        });

        Grid.SetColumn(candidates, 2);
        grid.Children.Add(candidates);

        var assign = new Button
        {
            Content = T("REC04.Button.AssignAlias", "Přiřadit + alias"),
            Margin = new Thickness(8, 0, 0, 0),
            MinWidth = 135,
            IsEnabled = false
        };
        assign.Click += AssignCandidate_Click;
        Grid.SetColumn(assign, 3);
        grid.Children.Add(assign);

        // The last interacted assignment panel owns the shared candidate state.
        filter.GotFocus += (_, _) => ActivateAssignmentPanel(filter, candidates, assign);
        candidates.GotFocus += (_, _) => ActivateAssignmentPanel(filter, candidates, assign);

        // Do NOT refresh ItemsSource when the Assign button receives focus:
        // doing so cleared the selected candidate immediately before Click,
        // which made "Přiřadit + alias" silently do nothing.
        assign.GotFocus += (_, _) =>
        {
            _candidateFilter = filter;
            _candidateGrid = candidates;
            _assignButton = assign;
        };

        candidates.SelectionChanged += (_, _) =>
            assign.IsEnabled =
                candidates.SelectedItem
                is RecipeMaterialCandidate;

        candidates.MouseDoubleClick += (_, _) =>
        {
            if (candidates.SelectedItem
                is RecipeMaterialCandidate)
            {
                AssignSelectedCandidate();
            }
        };

        border.Child = grid;
        return border;
    }

    private void ActivateAssignmentPanel(TextBox filter, DataGrid grid, Button button)
    {
        _candidateFilter = filter;
        _candidateGrid = grid;
        _assignButton = button;
        RefreshCandidates();
    }

    private void BtnLoadSpray_Click(object sender, RoutedEventArgs e)
    {
        var path = PickRecipeFile();
        if (path is null) return;

        try
        {
            _sprayResult = _parser.ParseSpray(path);
            _matcher.AutoMatch(_sprayResult, _sapMaterials, _settings);
            RenderSpray();
            BtnSaveSpray.IsEnabled = true;
            BtnExportSpray.IsEnabled = true;

            _logger.AdminAction(
                "REC04", "ParseSpray", _user,
                $"Source={path}; Article={_sprayResult.ArticleNumber}; HD={_sprayResult.HdNumber}; Layers={_sprayResult.Layers.Count}");
        }
        catch (Exception ex)
        {
            _logger.Error($"REC04 spray parse failed; Source={path}", ex);
            DmsMessage.Show(ex.Message, "REC04", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnLoadScreen_Click(object sender, RoutedEventArgs e)
    {
        var path = PickRecipeFile();
        if (path is null) return;

        try
        {
            _screenResult = _parser.ParseScreenPrint(path);
            var layer = _screenResult.Layers.Single();
            _normalizer.ApplyScreenPrintHardener(layer, _settings, _screenResult.Warnings);
            _matcher.AutoMatch(_screenResult, _sapMaterials, _settings);
            RenderScreen();
            BtnSaveScreen.IsEnabled = true;
            BtnExportScreen.IsEnabled = true;

            _logger.AdminAction(
                "REC04", "ParseScreenPrint", _user,
                $"Source={path}; Recipe={_screenResult.RecipeNumber}; Components={layer.Components.Count}; Total={layer.FinalTotalGrams:0.######}");
        }
        catch (Exception ex)
        {
            _logger.Error($"REC04 screen-print parse failed; Source={path}", ex);
            DmsMessage.Show(ex.Message, "REC04", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string? PickRecipeFile()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Recipe documents (*.pdf;*.docx)|*.pdf;*.docx|PDF (*.pdf)|*.pdf|Word (*.docx)|*.docx|All files (*.*)|*.*"
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private void RenderSpray()
    {
        if (_sprayResult is null) return;

        TxtSprayArticle.Text = _sprayResult.ArticleNumber;
        TxtSprayHd.Text = _sprayResult.HdNumber;
        TxtSprayColor.Text = _sprayResult.Color;
        GridSprayLayers.ItemsSource = _sprayResult.Layers;
        GridSprayLayers.Items.Refresh();

        if (_sprayResult.Layers.Count > 0)
        {
            GridSprayLayers.SelectedIndex = 0;
        }
    }

    private void RenderScreen()
    {
        if (_screenResult is null) return;

        var layer = _screenResult.Layers.Single();
        TxtRecipeNumber.Text = _screenResult.RecipeNumber;
        TxtRecipeKText.Text = _screenResult.KText;
        TxtRecipeTotal.Text = $"{layer.FinalTotalGrams:0.######} g / Base 1000 g";
        TxtScreenWarnings.Text = string.Join(Environment.NewLine, _screenResult.Warnings);
        GridScreenComponents.ItemsSource = layer.Components;
        GridScreenComponents.Items.Refresh();
    }

    private void GridSprayLayers_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GridSprayLayers.SelectedItem is not RecipeLayer layer)
        {
            GridSprayComponents.ItemsSource = null;
            TxtSprayTexts.Text = string.Empty;
            return;
        }

        GridSprayComponents.ItemsSource = layer.Components;
        TxtSprayTexts.Text = string.Join(Environment.NewLine, layer.TextItems);
    }

    private void GridComponents_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender == GridSprayComponents && GridSprayComponents.SelectedItem is RecipeComponent spray)
        {
            _selectedComponent = spray;
            _selectedComponentKind = RecipeImportKind.SprayCoating;
            ActivateHostForKind(RecipeImportKind.SprayCoating);
        }
        else if (sender == GridScreenComponents && GridScreenComponents.SelectedItem is RecipeComponent screen)
        {
            _selectedComponent = screen;
            _selectedComponentKind = RecipeImportKind.ScreenPrinting;
            ActivateHostForKind(RecipeImportKind.ScreenPrinting);
        }

        RefreshCandidates();
    }

    private void ActivateHostForKind(RecipeImportKind kind)
    {
        var host = kind == RecipeImportKind.SprayCoating ? SprayAssignmentHost : ScreenAssignmentHost;
        if (host.Content is not Border border || border.Child is not Grid grid) return;

        var filter = grid.Children.OfType<TextBox>().FirstOrDefault();
        var candidates = grid.Children.OfType<DataGrid>().FirstOrDefault();
        var assign = grid.Children.OfType<Button>().FirstOrDefault();

        if (filter is not null && candidates is not null && assign is not null)
        {
            ActivateAssignmentPanel(filter, candidates, assign);
        }
    }

    private void CandidateFilter_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender == _candidateFilter)
        {
            RefreshCandidates();
        }
    }

    private void RefreshCandidates()
    {
        if (_candidateGrid is null || _selectedComponent is null)
        {
            if (_assignButton is not null) _assignButton.IsEnabled = false;
            return;
        }

        var materials = _matcher.FilterSapComponents(_sapMaterials, _settings);
        var candidates = _matcher.GetCandidates(
            _selectedComponent,
            _selectedComponentKind,
            materials,
            _settings,
            30,
            _candidateFilter?.Text);

        _candidateGrid.ItemsSource = candidates;

        if (candidates.Count > 0)
        {
            // Select the best candidate visibly. The user can still pick any
            // other row before assigning it.
            _candidateGrid.SelectedIndex = 0;
            _candidateGrid.ScrollIntoView(
                _candidateGrid.SelectedItem);
        }
        else
        {
            _candidateGrid.SelectedItem = null;
        }

        if (_assignButton is not null)
        {
            _assignButton.IsEnabled =
                _candidateGrid.SelectedItem
                is RecipeMaterialCandidate;
        }
    }

    private void AssignCandidate_Click(
        object sender,
        RoutedEventArgs e)
    {
        AssignSelectedCandidate();
    }

    private void AssignSelectedCandidate()
    {
        if (_selectedComponent is null)
        {
            DmsMessage.Show(
                T(
                    "REC04.Validation.SelectSourceComponent",
                    "Nejprve vyber položku receptury."),
                "REC04",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        if (_candidateGrid?.SelectedItem
            is not RecipeMaterialCandidate candidate)
        {
            DmsMessage.Show(
                T(
                    "REC04.Validation.SelectSapCandidate",
                    "Vyber SAP komponentu ze seznamu kandidátů."),
                "REC04",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        var sourceText =
            _selectedComponent.SourceText;

        var signature =
            RecipeTextNormalizer
                .NormalizeTokenSignature(sourceText);

        var old =
            _selectedComponent
                .SapMaterialNumber;

        _matcher.Assign(
            _selectedComponent,
            candidate.Material,
            candidate.Score,
            "MANUAL_ALIAS");

        var aliasCreated =
            UpsertAlias(
                sourceText,
                candidate.Material);

        _settingsService.Save(_settings);

        // Apply the newly learned alias immediately to every currently loaded
        // component with the same normalized source text.
        foreach (var component in EnumerateLoadedComponents())
        {
            if (string.Equals(
                    RecipeTextNormalizer
                        .NormalizeTokenSignature(
                            component.SourceText),
                    signature,
                    StringComparison.OrdinalIgnoreCase))
            {
                _matcher.Assign(
                    component,
                    candidate.Material,
                    1d,
                    "ALIAS");
            }
        }

        GridSprayComponents.Items.Refresh();
        GridScreenComponents.Items.Refresh();
        GridAliases.Items.Refresh();

        _logger.AuditChange(
            "REC04",
            "RecipeComponent",
            sourceText,
            "SapMaterialNumber",
            old,
            candidate.Material.MaterialNumber,
            _user);

        if (aliasCreated)
        {
            _logger.AuditCreated(
                "REC04",
                "RecipeAlias",
                signature,
                _user,
                $"Source={sourceText}; SAP={candidate.Material.MaterialNumber}; Text={candidate.Material.Description}");
        }
        else
        {
            _logger.AuditChange(
                "REC04",
                "RecipeAlias",
                signature,
                "MaterialNumber",
                old,
                candidate.Material.MaterialNumber,
                _user);
        }
    }

    private IEnumerable<RecipeComponent> EnumerateLoadedComponents()
    {
        if (_sprayResult is not null)
        {
            foreach (var component
                     in _sprayResult.AllComponents)
            {
                yield return component;
            }
        }

        if (_screenResult is not null)
        {
            foreach (var component
                     in _screenResult.AllComponents)
            {
                yield return component;
            }
        }
    }

    private bool UpsertAlias(
        string sourceText,
        SapMaterial material)
    {
        var signature =
            RecipeTextNormalizer
                .NormalizeTokenSignature(
                    sourceText);

        var existing =
            _settings.Aliases
                .FirstOrDefault(alias =>
                    string.Equals(
                        RecipeTextNormalizer
                            .NormalizeTokenSignature(
                                alias.SourceText),
                        signature,
                        StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            _settings.Aliases.Add(
                new RecipeAliasRule
                {
                    SourceText = sourceText,
                    MaterialNumber =
                        material.MaterialNumber,
                    SapDescription =
                        material.Description
                });

            return true;
        }

        existing.SourceText = sourceText;
        existing.MaterialNumber =
            material.MaterialNumber;
        existing.SapDescription =
            material.Description;

        return false;
    }

    private void BtnSaveSpray_Click(object sender, RoutedEventArgs e) => SaveResult(_sprayResult);
    private void BtnSaveScreen_Click(object sender, RoutedEventArgs e) => SaveResult(_screenResult);

    private void SaveResult(RecipeImportResult? result)
    {
        if (result is null) return;

        if (!ValidateReady(result, out var error))
        {
            DmsMessage.Show(error, "REC04", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var directory = Path.Combine(_storagePaths.DmsDataDirectory, "RecipeImports");
        var path = _output.SaveJson(result, directory);

        _logger.AuditCreated(
            "REC04", "RecipeImport", Path.GetFileNameWithoutExtension(path), _user,
            $"Kind={result.Kind}; Source={result.SourceFile}; Article={result.ArticleNumber}; Recipe={result.RecipeNumber}; Path={path}");

        foreach (var layer in result.Layers)
        {
            foreach (var component in layer.Components)
            {
                _logger.AuditCreated(
                    "REC04", "RecipeImportComponent",
                    $"{layer.KText}:{component.SapMaterialNumber}", _user,
                    $"Source={component.SourceText}; SAP={component.SapMaterialNumber}; QuantityG={component.BomGrams:0.######}; Hardener={component.IsHardener}");
            }
        }

        DmsMessage.Show(path, "REC04", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnExportSpray_Click(object sender, RoutedEventArgs e) => ExportResult(_sprayResult);
    private void BtnExportScreen_Click(object sender, RoutedEventArgs e) => ExportResult(_screenResult);

    private void ExportResult(RecipeImportResult? result)
    {
        if (result is null) return;

        var dialog = new SaveFileDialog
        {
            Filter = "Excel workbook (*.xlsx)|*.xlsx",
            FileName = result.Kind == RecipeImportKind.ScreenPrinting
                ? $"Rezept-{result.RecipeNumber.Replace('/', '-')}.xlsx"
                : $"HD-{string.Concat(result.HdNumber.Where(char.IsDigit))}-{result.ArticleNumber}.xlsx"
        };

        if (dialog.ShowDialog() != true) return;

        _output.ExportExcel(result, dialog.FileName);
        _logger.AdminAction("REC04", "ExportExcel", _user, $"Kind={result.Kind}; File={dialog.FileName}");
    }

    private static bool ValidateReady(RecipeImportResult result, out string error)
    {
        var unresolved = result.AllComponents
            .Where(component => string.IsNullOrWhiteSpace(component.SapMaterialNumber))
            .Select(component => component.SourceText)
            .Distinct()
            .ToList();

        if (unresolved.Count > 0)
        {
            error = "Unresolved SAP components: " + string.Join(", ", unresolved);
            return false;
        }

        foreach (var layer in result.Layers.Where(layer => !layer.ProcessOnly))
        {
            var baseComponents = layer.Components.Where(component => !component.IsHardener).Sum(component => component.BomGrams);
            if (Math.Abs(baseComponents - 1000m) > 0.001m)
            {
                error = $"{layer.KText}: base components total {baseComponents:0.######} g instead of 1000 g.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private void LoadSettingsToUi()
    {
        TxtComponentPrefix.Text = _settings.SapComponentPrefix;
        TxtMatchThreshold.Text = _settings.AutoMatchThreshold.ToString("0.00", CultureInfo.InvariantCulture);
        ChkAddMissingHardener.IsChecked = _settings.AddMissingHardener;
        TxtHardenerTolerance.Text = _settings.HardenerTolerancePercent.ToString("0.###", CultureInfo.InvariantCulture);
        GridHardenerRules.ItemsSource = _settings.HardenerRules;
        GridAliases.ItemsSource = _settings.Aliases;

        TxtComponentPrefix.IsReadOnly = !_canEditSettings;
        TxtMatchThreshold.IsReadOnly = !_canEditSettings;
        ChkAddMissingHardener.IsEnabled = _canEditSettings;
        TxtHardenerTolerance.IsReadOnly = !_canEditSettings;
        GridHardenerRules.IsReadOnly = !_canEditSettings;
        BtnDeleteRule.IsEnabled = _canEditSettings;
        BtnDeleteAlias.IsEnabled = _canEditSettings;
        BtnSaveSettings.IsEnabled = _canEditSettings;
    }

    private void BtnSaveSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!_canEditSettings) return;

        GridHardenerRules.CommitEdit(DataGridEditingUnit.Cell, true);
        GridHardenerRules.CommitEdit(DataGridEditingUnit.Row, true);

        var oldJson = JsonSerializer.Serialize(_settings);
        _settings.SapComponentPrefix = TxtComponentPrefix.Text.Trim();

        if (!double.TryParse(TxtMatchThreshold.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var threshold) ||
            threshold < 0d || threshold > 1d)
        {
            DmsMessage.Show("Match threshold must be between 0 and 1.", "REC04", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!decimal.TryParse(
                TxtHardenerTolerance.Text.Replace(',', '.'),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var hardenerTolerance) ||
            hardenerTolerance < 0m ||
            hardenerTolerance > 10m)
        {
            DmsMessage.Show(
                T("REC04.Validation.HardenerTolerance", "Tolerance tvrdidla musí být mezi 0 a 10 procentními body."),
                "REC04",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _settings.AutoMatchThreshold = threshold;
        _settings.AddMissingHardener = ChkAddMissingHardener.IsChecked == true;
        _settings.HardenerTolerancePercent = hardenerTolerance;
        _settingsService.Save(_settings);
        var newJson = JsonSerializer.Serialize(_settings);

        _logger.AuditChange(
            "REC04", "RecipeImportSettings", "GLOBAL", "SettingsJson", oldJson, newJson, _user);
        TxtSettingsStatus.Text = $"Saved: {_settingsService.Path}";
    }

    private void BtnDeleteRule_Click(object sender, RoutedEventArgs e)
    {
        if (!_canEditSettings || GridHardenerRules.SelectedItem is not RecipeHardenerRule rule) return;

        _settings.HardenerRules.Remove(rule);
        GridHardenerRules.Items.Refresh();
        _logger.AuditDeleted(
            "REC04", "HardenerRule", rule.Family, _user,
            $"Ratio={rule.RatioPercent}; Hardener={rule.HardenerText}");
    }

    private void BtnDeleteAlias_Click(object sender, RoutedEventArgs e)
    {
        if (!_canEditSettings || GridAliases.SelectedItem is not RecipeAliasRule alias) return;

        _settings.Aliases.Remove(alias);
        GridAliases.Items.Refresh();
        _settingsService.Save(_settings);
        _logger.AuditDeleted(
            "REC04", "RecipeAlias", RecipeTextNormalizer.NormalizeTokenSignature(alias.SourceText), _user,
            $"Source={alias.SourceText}; SAP={alias.MaterialNumber}");
    }
}
