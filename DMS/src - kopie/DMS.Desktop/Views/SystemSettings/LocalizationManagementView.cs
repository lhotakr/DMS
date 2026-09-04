using ClosedXML.Excel;
using DMS.Desktop.Localization;
using DMS.Desktop.Logging;
using DMS.Desktop.UI;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace DMS.Desktop.Views.SystemSettings;

public sealed class LocalizationManagementView : UserControl
{
    private readonly StackPanel _cultureFilterPanel = new();
    private readonly Dictionary<string, CheckBox> _cultureCheckBoxes = new(StringComparer.OrdinalIgnoreCase);

    private readonly string _localizationRootPath;
    private readonly DmsLogger _logger;
    private readonly string _currentUserName;
    private readonly Action? _afterSave;
    private readonly Func<string, string>? _translate;

    private readonly ObservableCollection<DmsLocalizationRow> _rows = new();
    private readonly DataGrid _grid = new();
    private readonly TextBox _txtNewKey = new();

    private readonly TextBlock _title = new();
    private readonly TextBlock _subtitle = new();
    private readonly TextBlock _cultureFilterTitle = new();
    private readonly TextBlock _legend = new();

    private Button _btnAddKey = null!;
    private Button _btnDeleteKey = null!;
    private Button _btnImportExcel = null!;
    private Button _btnExportExcel = null!;
    private Button _btnReload = null!;
    private Button _btnSave = null!;
    private Button _btnAllCultures = null!;
    private Button _btnDefaultCultureOnly = null!;
    private Button _btnAddCulture = null!;
    private Button _btnRemoveCulture = null!;

    private DmsLocalizationIndex _index = new();
    private readonly Dictionary<string, Dictionary<string, string>> _dictionaries = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _culturesMarkedForDeletion = new(StringComparer.OrdinalIgnoreCase);

    public LocalizationManagementView(
        string localizationRootPath,
        DmsLogger logger,
        string currentUserName,
        Action? afterSave = null,
        Func<string, string>? translate = null)
    {
        _localizationRootPath = localizationRootPath;
        _logger = logger;
        _currentUserName = currentUserName;
        _afterSave = afterSave;
        _translate = translate;

        BuildLayout();
        ApplyUiText();
        LoadLocalization();
    }

    private string T(string key, string fallback)
    {
        if (_translate is null)
        {
            return fallback;
        }

        var value = _translate(key);

        if (string.IsNullOrWhiteSpace(value) ||
            string.Equals(value, key, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, $"[[{key}]]", StringComparison.OrdinalIgnoreCase))
        {
            return fallback;
        }

        return value;
    }

    private void BuildLayout()
    {
        var root = new DockPanel
        {
            LastChildFill = true
        };

        var headerPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(0, 0, 0, 12)
        };

        _title.FontSize = 22;
        _title.FontWeight = FontWeights.Bold;
        _title.Margin = new Thickness(0, 0, 0, 6);
        _title.SetResourceReference(TextBlock.ForegroundProperty, "DmsForegroundBrush");

        _subtitle.TextWrapping = TextWrapping.Wrap;
        _subtitle.Margin = new Thickness(0, 0, 0, 10);
        _subtitle.SetResourceReference(TextBlock.ForegroundProperty, "DmsMutedForegroundBrush");

        var toolbar = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 4, 0, 0)
        };

        _txtNewKey.Width = 320;
        _txtNewKey.Height = 30;
        _txtNewKey.Margin = new Thickness(0, 0, 8, 8);
        _txtNewKey.VerticalContentAlignment = VerticalAlignment.Center;
        _txtNewKey.SetResourceReference(TextBox.BackgroundProperty, "DmsBackgroundBrush");
        _txtNewKey.SetResourceReference(TextBox.ForegroundProperty, "DmsForegroundBrush");
        _txtNewKey.SetResourceReference(TextBox.BorderBrushProperty, "DmsBorderBrush");

        _btnAddKey = CreateToolbarButton(string.Empty);
        _btnAddKey.Click += (_, _) => AddKey();

        _btnDeleteKey = CreateToolbarButton(string.Empty);
        _btnDeleteKey.Click += (_, _) => MarkSelectedKeyDeleted();

        _btnImportExcel = CreateToolbarButton(string.Empty);
        _btnImportExcel.Click += (_, _) => ImportFromExcel();

        _btnExportExcel = CreateToolbarButton(string.Empty);
        _btnExportExcel.Click += (_, _) => ExportToExcel();

        _btnReload = CreateToolbarButton(string.Empty);
        _btnReload.Click += (_, _) => ReloadWithUnsavedChangesCheck();

        _btnSave = CreatePrimaryButton(string.Empty);
        _btnSave.Click += (_, _) => SaveLocalization();

        toolbar.Children.Add(_txtNewKey);
        toolbar.Children.Add(_btnAddKey);
        toolbar.Children.Add(_btnDeleteKey);
        toolbar.Children.Add(_btnImportExcel);
        toolbar.Children.Add(_btnExportExcel);
        toolbar.Children.Add(_btnReload);
        toolbar.Children.Add(_btnSave);

        _cultureFilterTitle.FontWeight = FontWeights.SemiBold;
        _cultureFilterTitle.Margin = new Thickness(0, 10, 0, 4);
        _cultureFilterTitle.SetResourceReference(TextBlock.ForegroundProperty, "DmsForegroundBrush");

        _cultureFilterPanel.Orientation = Orientation.Horizontal;
        _cultureFilterPanel.Margin = new Thickness(0, 0, 0, 8);

        _legend.FontSize = 12;
        _legend.Margin = new Thickness(0, 0, 0, 8);
        _legend.TextWrapping = TextWrapping.Wrap;
        _legend.SetResourceReference(TextBlock.ForegroundProperty, "DmsMutedForegroundBrush");

        headerPanel.Children.Add(_title);
        headerPanel.Children.Add(_subtitle);
        headerPanel.Children.Add(toolbar);
        headerPanel.Children.Add(_cultureFilterTitle);
        headerPanel.Children.Add(_cultureFilterPanel);
        headerPanel.Children.Add(_legend);

        DockPanel.SetDock(headerPanel, Dock.Top);
        root.Children.Add(headerPanel);

        _grid.AutoGenerateColumns = false;
        _grid.CanUserAddRows = false;
        _grid.CanUserDeleteRows = false;
        _grid.IsReadOnly = false;
        _grid.ItemsSource = _rows;
        _grid.MinHeight = 420;
        _grid.SelectionMode = DataGridSelectionMode.Single;
        _grid.SelectionUnit = DataGridSelectionUnit.CellOrRowHeader;
        _grid.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _grid.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _grid.EnableColumnVirtualization = true;
        _grid.EnableRowVirtualization = true;
        _grid.CanUserResizeColumns = true;
        _grid.CanUserReorderColumns = true;
        _grid.CanUserSortColumns = true;
        _grid.RowStyle = CreateLocalizationRowStyle();
        _grid.CellStyle = CreateLocalizationCellStyle();
        _grid.CellEditEnding += Grid_CellEditEnding;

        _grid.SetResourceReference(DataGrid.BackgroundProperty, "DmsPanelBrush");
        _grid.SetResourceReference(DataGrid.ForegroundProperty, "DmsForegroundBrush");
        _grid.SetResourceReference(DataGrid.BorderBrushProperty, "DmsBorderBrush");

        root.Children.Add(_grid);
        Content = root;
    }

    private Button CreateToolbarButton(string text)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 110,
            Height = 30,
            Margin = new Thickness(0, 0, 8, 8)
        };

        button.SetResourceReference(Button.StyleProperty, "DmsFormButtonStyle");
        return button;
    }

    private Button CreatePrimaryButton(string text)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 130,
            Height = 30,
            Margin = new Thickness(0, 0, 8, 8)
        };

        button.SetResourceReference(Button.StyleProperty, "DmsPrimaryButtonStyle");
        return button;
    }

    private void ApplyUiText()
    {
        _title.Text = T("SYS01.Localization.Title", "Správa lokalizace");
        _subtitle.Text = string.Format(
            T("SYS01.Localization.Subtitle", "Slovníky jsou načítány ze složky: {0}"),
            _localizationRootPath);

        _txtNewKey.ToolTip = T("SYS01.Localization.NewKeyTooltip", "Nový překladový klíč, například Common.Save");
        _btnAddKey.Content = T("SYS01.Localization.AddKey", "Přidat klíč");
        _btnDeleteKey.Content = T("SYS01.Localization.DeleteKey", "Smazat klíč");
        _btnImportExcel.Content = T("SYS01.Localization.ImportExcel", "Import Excel");
        _btnExportExcel.Content = T("SYS01.Localization.ExportExcel", "Export Excel");
        _btnReload.Content = T("SYS01.Localization.Reload", "Načíst znovu");
        _btnSave.Content = T("SYS01.Localization.Save", "Uložit slovníky");

        _cultureFilterTitle.Text = T("SYS01.Localization.ShowLanguages", "Zobrazit jazyky:");
        _legend.Text = T(
            "SYS01.Localization.Legend",
            "Legenda: zelená = nový klíč, žlutá = změněno, červená = označeno ke smazání. Načíst znovu zahodí neuložené změny.");

        if (_btnAllCultures is not null)
        {
            _btnAllCultures.Content = T("SYS01.Localization.All", "Vše");
        }

        if (_btnDefaultCultureOnly is not null)
        {
            _btnDefaultCultureOnly.Content = T("SYS01.Localization.DefaultOnly", "Jen default");
        }

        if (_btnAddCulture is not null)
        {
            _btnAddCulture.Content = T("SYS01.Localization.AddCulture", "Přidat jazyk");
        }

        if (_btnRemoveCulture is not null)
        {
            _btnRemoveCulture.Content = T("SYS01.Localization.RemoveCulture", "Odebrat jazyk");
        }
    }

    private void LoadLocalization()
    {
        Directory.CreateDirectory(_localizationRootPath);

        _index = LoadIndex();
        EnsureDefaultCultures();

        _dictionaries.Clear();

        foreach (var culture in _index.SupportedCultures)
        {
            _dictionaries[culture.Culture] = LoadDictionary(culture.Culture);
        }

        BuildCultureFilter();
        BuildGridColumns();
        BuildRows();
        ApplyUiText();

        _logger.Info($"Lokalizace načtena ze složky: {_localizationRootPath}");
    }

    private DmsLocalizationIndex LoadIndex()
    {
        var path = Path.Combine(_localizationRootPath, "localization.index.json");

        if (!File.Exists(path))
        {
            return CreateDefaultIndex();
        }

        var json = File.ReadAllText(path);

        if (string.IsNullOrWhiteSpace(json))
        {
            return CreateDefaultIndex();
        }

        try
        {
            return JsonSerializer.Deserialize<DmsLocalizationIndex>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? CreateDefaultIndex();
        }
        catch (JsonException ex)
        {
            ShowWarning(
                T("SYS01.Localization.InvalidIndexTitle", "Chyba indexu lokalizace"),
                string.Format(T("SYS01.Localization.InvalidIndexMessage", "Soubor localization.index.json není platný.\n\n{0}"), ex.Message));

            return CreateDefaultIndex();
        }
    }

    private static DmsLocalizationIndex CreateDefaultIndex()
    {
        return new DmsLocalizationIndex
        {
            DefaultCulture = "en-US",
            SupportedCultures =
            {
                new DmsSupportedCulture
                {
                    Culture = "en-US",
                    DisplayName = "English",
                    IsDefault = true
                },
                new DmsSupportedCulture
                {
                    Culture = "cs-CZ",
                    DisplayName = "Čeština"
                },
                new DmsSupportedCulture
                {
                    Culture = "de-DE",
                    DisplayName = "Deutsch"
                }
            }
        };
    }

    private void EnsureDefaultCultures()
    {
        if (string.IsNullOrWhiteSpace(_index.DefaultCulture))
        {
            _index.DefaultCulture = "en-US";
        }

        if (_index.SupportedCultures.Count == 0)
        {
            _index.SupportedCultures.Add(new DmsSupportedCulture
            {
                Culture = _index.DefaultCulture,
                DisplayName = _index.DefaultCulture,
                IsDefault = true
            });
        }

        if (!_index.SupportedCultures.Any(item =>
                string.Equals(item.Culture, _index.DefaultCulture, StringComparison.OrdinalIgnoreCase)))
        {
            _index.SupportedCultures.Insert(0, new DmsSupportedCulture
            {
                Culture = _index.DefaultCulture,
                DisplayName = _index.DefaultCulture,
                IsDefault = true
            });
        }
    }

    private Dictionary<string, string> LoadDictionary(string cultureName)
    {
        var path = Path.Combine(_localizationRootPath, $"{cultureName}.json");

        if (!File.Exists(path))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var json = File.ReadAllText(path);

        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException ex)
        {
            ShowWarning(
                T("SYS01.Localization.InvalidDictionaryTitle", "Chyba lokalizačního slovníku"),
                string.Format(
                    T("SYS01.Localization.InvalidDictionaryMessage", "Soubor {0} není platný.\n\nŘádek: {1}, pozice: {2}\n\n{3}"),
                    path,
                    ex.LineNumber,
                    ex.BytePositionInLine,
                    ex.Message));

            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void BuildGridColumns()
    {
        _grid.Columns.Clear();

        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = T("SYS01.Localization.KeyColumn", "Key"),
            Binding = new Binding(nameof(DmsLocalizationRow.Key)),
            IsReadOnly = true,
            Width = new DataGridLength(260),
            MinWidth = 220,
            ElementStyle = CreateCellTextStyle()
        });

        foreach (var culture in _index.SupportedCultures)
        {
            if (!IsCultureVisible(culture.Culture))
            {
                continue;
            }

            _grid.Columns.Add(new DataGridTextColumn
            {
                Header = $"{culture.DisplayName} ({culture.Culture})",
                Binding = new Binding($"Translations[{culture.Culture}]")
                {
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                },
                Width = new DataGridLength(320),
                MinWidth = 220,
                ElementStyle = CreateCellTextStyle(),
                EditingElementStyle = CreateEditingCellTextBoxStyle()
            });
        }
    }

    private void BuildRows()
    {
        _rows.Clear();

        var keys = _dictionaries
            .Values
            .SelectMany(dictionary => dictionary.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var key in keys)
        {
            var row = new DmsLocalizationRow
            {
                Key = key
            };

            foreach (var culture in _index.SupportedCultures)
            {
                var dictionary = _dictionaries[culture.Culture];

                row.Translations[culture.Culture] = dictionary.TryGetValue(key, out var value)
                    ? value
                    : string.Empty;
            }

            row.AcceptChanges();
            _rows.Add(row);
        }
    }

    private void AddKey()
    {
        var key = _txtNewKey.Text?.Trim();

        if (string.IsNullOrWhiteSpace(key))
        {
            ShowInfo(
                T("SYS01.Localization.AddKeyTitle", "Přidání klíče"),
                T("SYS01.Localization.EnterKeyMessage", "Zadej nový překladový klíč."));
            return;
        }

        if (_rows.Any(row => string.Equals(row.Key, key, StringComparison.OrdinalIgnoreCase)))
        {
            ShowInfo(
                T("SYS01.Localization.AddKeyTitle", "Přidání klíče"),
                string.Format(T("SYS01.Localization.KeyExistsMessage", "Klíč {0} už existuje."), key));
            return;
        }

        var newRow = new DmsLocalizationRow
        {
            Key = key,
            IsNew = true
        };

        foreach (var culture in _index.SupportedCultures)
        {
            newRow.Translations[culture.Culture] = string.Empty;
        }

        _rows.Add(newRow);
        _txtNewKey.Text = string.Empty;

        _grid.SelectedItem = newRow;
        _grid.ScrollIntoView(newRow);
    }

    private void MarkSelectedKeyDeleted()
    {
        if (_grid.SelectedItem is not DmsLocalizationRow row)
        {
            ShowInfo(
                T("SYS01.Localization.DeleteKeyTitle", "Smazání klíče"),
                T("SYS01.Localization.SelectKeyToDelete", "Vyber klíč, který chceš smazat."));
            return;
        }

        if (row.IsNew)
        {
            _rows.Remove(row);
            return;
        }

        row.IsDeleted = true;
        row.IsModified = false;
        row.RefreshState();
    }

    private void ReloadWithUnsavedChangesCheck()
    {
        if (HasUnsavedChanges())
        {
            var confirm = DmsConfirmDialog.Show(
                Window.GetWindow(this),
                T("SYS01.Localization.ReloadTitle", "Načíst znovu"),
                T("SYS01.Localization.ReloadQuestion", "Neuložené změny budou zahozeny. Chceš pokračovat?"),
                DmsDialogButtons.YesNo);

            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }
        }

        LoadLocalization();
    }

    private bool HasUnsavedChanges()
    {
        return _rows.Any(row => row.IsNew || row.IsModified || row.IsDeleted);
    }

    private void SaveLocalization()
    {
        Directory.CreateDirectory(_localizationRootPath);
        SaveIndex();

        foreach (var culture in _index.SupportedCultures)
        {
            var dictionary = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in _rows)
            {
                if (row.IsDeleted || string.IsNullOrWhiteSpace(row.Key))
                {
                    continue;
                }

                row.Translations.TryGetValue(culture.Culture, out var value);
                dictionary[row.Key.Trim()] = value ?? string.Empty;
            }

            var path = Path.Combine(_localizationRootPath, $"{culture.Culture}.json");
            SaveJsonFile(path, dictionary);
        }

        DeleteRemovedCultureFiles();

        _logger.AdminAction(
            "SYS01",
            "SaveLocalization",
            _currentUserName,
            $"LocalizationRootPath={_localizationRootPath}; Cultures={string.Join(", ", _index.SupportedCultures.Select(item => item.Culture))}; Keys={_rows.Count(row => !row.IsDeleted)}");

        ShowInfo(
            T("SYS01.Localization.SaveTitle", "Lokalizace"),
            T("SYS01.Localization.SaveSuccess", "Jazykové slovníky byly uloženy."));

        _culturesMarkedForDeletion.Clear();
        LoadLocalization();
        _afterSave?.Invoke();
    }

    private void DeleteRemovedCultureFiles()
    {
        foreach (var cultureName in _culturesMarkedForDeletion.ToList())
        {
            var path = Path.Combine(_localizationRootPath, $"{cultureName}.json");

            if (!File.Exists(path))
            {
                continue;
            }

            var backupPath = path + ".removed.bak";
            File.Copy(path, backupPath, overwrite: true);
            File.Delete(path);
        }
    }

    private void SaveIndex()
    {
        var path = Path.Combine(_localizationRootPath, "localization.index.json");
        SaveJsonFile(path, _index);
    }

    private static void SaveJsonFile<T>(string path, T data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? AppContext.BaseDirectory);

        var json = JsonSerializer.Serialize(
            data,
            new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
            });

        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, json);

        if (File.Exists(path))
        {
            File.Copy(path, path + ".bak", overwrite: true);
        }

        File.Copy(tempPath, path, overwrite: true);
        File.Delete(tempPath);
    }

    private void BuildCultureFilter()
    {
        _cultureFilterPanel.Children.Clear();
        _cultureCheckBoxes.Clear();

        foreach (var culture in _index.SupportedCultures)
        {
            var checkBox = new CheckBox
            {
                Content = GetCultureDisplayText(culture),
                IsChecked = true,
                Margin = new Thickness(0, 0, 16, 8),
                VerticalAlignment = VerticalAlignment.Center,
                Tag = culture.Culture
            };

            checkBox.SetResourceReference(CheckBox.ForegroundProperty, "DmsForegroundBrush");
            checkBox.Checked += (_, _) => BuildGridColumns();
            checkBox.Unchecked += (_, _) => BuildGridColumns();

            _cultureCheckBoxes[culture.Culture] = checkBox;
            _cultureFilterPanel.Children.Add(checkBox);
        }

        _btnAllCultures = CreateToolbarButton(T("SYS01.Localization.All", "Vše"));
        _btnAllCultures.Click += (_, _) =>
        {
            foreach (var checkBox in _cultureCheckBoxes.Values)
            {
                checkBox.IsChecked = true;
            }

            BuildGridColumns();
        };

        _btnDefaultCultureOnly = CreateToolbarButton(T("SYS01.Localization.DefaultOnly", "Jen default"));
        _btnDefaultCultureOnly.Click += (_, _) =>
        {
            foreach (var pair in _cultureCheckBoxes)
            {
                pair.Value.IsChecked = string.Equals(
                    pair.Key,
                    _index.DefaultCulture,
                    StringComparison.OrdinalIgnoreCase);
            }

            BuildGridColumns();
        };

        _btnAddCulture = CreateToolbarButton(T("SYS01.Localization.AddCulture", "Přidat jazyk"));
        _btnAddCulture.Click += (_, _) => AddCulture();

        _btnRemoveCulture = CreateToolbarButton(T("SYS01.Localization.RemoveCulture", "Odebrat jazyk"));
        _btnRemoveCulture.Click += (_, _) => RemoveCulture();

        _cultureFilterPanel.Children.Add(_btnAllCultures);
        _cultureFilterPanel.Children.Add(_btnDefaultCultureOnly);
        _cultureFilterPanel.Children.Add(_btnAddCulture);
        _cultureFilterPanel.Children.Add(_btnRemoveCulture);
    }

    private string GetCultureDisplayText(DmsSupportedCulture culture)
    {
        var displayName = culture.Culture switch
        {
            "en-US" => T("Language.English", culture.DisplayName),
            "cs-CZ" => T("Language.Czech", culture.DisplayName),
            "de-DE" => T("Language.German", culture.DisplayName),
            _ => string.IsNullOrWhiteSpace(culture.DisplayName) ? culture.Culture : culture.DisplayName
        };

        var defaultSuffix = culture.IsDefault || string.Equals(culture.Culture, _index.DefaultCulture, StringComparison.OrdinalIgnoreCase)
            ? $" {T("SYS01.Localization.DefaultCultureSuffix", "(default)")}"
            : string.Empty;

        return $"{displayName} ({culture.Culture}){defaultSuffix}";
    }

    private void AddCulture()
    {
        CommitGridEdits();

        var dialog = new DmsCultureEditDialog(
            T("SYS01.Localization.AddCultureTitle", "Přidat jazykovou kulturu"),
            T("SYS01.Localization.CultureCode", "Kultura"),
            T("SYS01.Localization.CultureDisplayName", "Název"),
            T("SYS01.Localization.CultureIsDefault", "Nastavit jako výchozí jazyk"),
            T("SYS01.Localization.CultureCodeHint", "Například fr-FR, pl-PL, es-ES"),
            T("Common.OK", "OK"),
            T("Common.Cancel", "Zrušit"))
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var cultureCode = dialog.CultureCode.Trim();
        var displayName = dialog.DisplayName.Trim();

        if (!TryNormalizeCulture(cultureCode, out cultureCode, out var nativeName, out var validationMessage))
        {
            ShowWarning(T("SYS01.Localization.AddCultureTitle", "Přidat jazykovou kulturu"), validationMessage);
            return;
        }

        if (_index.SupportedCultures.Any(item => string.Equals(item.Culture, cultureCode, StringComparison.OrdinalIgnoreCase)))
        {
            ShowWarning(
                T("SYS01.Localization.AddCultureTitle", "Přidat jazykovou kulturu"),
                string.Format(T("SYS01.Localization.CultureAlreadyExists", "Kultura {0} už existuje."), cultureCode));
            return;
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = nativeName;
        }

        if (dialog.IsDefaultCulture)
        {
            foreach (var culture in _index.SupportedCultures)
            {
                culture.IsDefault = false;
            }

            _index.DefaultCulture = cultureCode;
        }

        _index.SupportedCultures.Add(new DmsSupportedCulture
        {
            Culture = cultureCode,
            DisplayName = displayName,
            IsDefault = dialog.IsDefaultCulture
        });

        _dictionaries[cultureCode] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _culturesMarkedForDeletion.Remove(cultureCode);

        foreach (var row in _rows)
        {
            row.Translations[cultureCode] = string.Empty;
            row.OriginalTranslations[cultureCode] = string.Empty;
            row.RefreshState();
        }

        BuildCultureFilter();
        BuildGridColumns();

        _logger.AdminAction(
            "SYS01",
            "AddLocalizationCulture",
            _currentUserName,
            $"Culture={cultureCode}; DisplayName={displayName}; IsDefault={dialog.IsDefaultCulture}");

        ShowInfo(
            T("SYS01.Localization.AddCultureTitle", "Přidat jazykovou kulturu"),
            string.Format(T("SYS01.Localization.CultureAdded", "Kultura {0} byla přidána. Změnu zapiš tlačítkem Uložit slovníky."), cultureCode));
    }

    private void RemoveCulture()
    {
        CommitGridEdits();

        if (_index.SupportedCultures.Count <= 1)
        {
            ShowWarning(
                T("SYS01.Localization.RemoveCultureTitle", "Odebrat jazykovou kulturu"),
                T("SYS01.Localization.CannotRemoveLastCulture", "Poslední jazykovou kulturu nelze odebrat."));
            return;
        }

        var dialog = new DmsCultureSelectDialog(
            T("SYS01.Localization.RemoveCultureTitle", "Odebrat jazykovou kulturu"),
            T("SYS01.Localization.RemoveCultureQuestion", "Vyber jazykovou kulturu, kterou chceš odebrat."),
            _index.SupportedCultures.Select(culture => new DmsCultureChoice(culture.Culture, GetCultureDisplayText(culture))).ToList(),
            T("Common.OK", "OK"),
            T("Common.Cancel", "Zrušit"))
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.SelectedCultureCode))
        {
            return;
        }

        var cultureCode = dialog.SelectedCultureCode;

        if (string.Equals(cultureCode, _index.DefaultCulture, StringComparison.OrdinalIgnoreCase))
        {
            ShowWarning(
                T("SYS01.Localization.RemoveCultureTitle", "Odebrat jazykovou kulturu"),
                T("SYS01.Localization.CannotRemoveDefaultCulture", "Výchozí jazykovou kulturu nelze odebrat. Nejdřív nastav jinou kulturu jako výchozí."));
            return;
        }

        var confirm = DmsConfirmDialog.Show(
            Window.GetWindow(this),
            T("SYS01.Localization.RemoveCultureTitle", "Odebrat jazykovou kulturu"),
            string.Format(T("SYS01.Localization.RemoveCultureConfirm", "Opravdu chceš odebrat jazykovou kulturu {0}? JSON soubor se smaže až po uložení slovníků."), cultureCode),
            DmsDialogButtons.YesNo);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        _index.SupportedCultures.RemoveAll(item => string.Equals(item.Culture, cultureCode, StringComparison.OrdinalIgnoreCase));
        _dictionaries.Remove(cultureCode);
        _cultureCheckBoxes.Remove(cultureCode);
        _culturesMarkedForDeletion.Add(cultureCode);

        foreach (var row in _rows)
        {
            row.Translations.Remove(cultureCode);
            row.OriginalTranslations.Remove(cultureCode);
            row.RefreshState();
        }

        BuildCultureFilter();
        BuildGridColumns();

        _logger.AdminAction(
            "SYS01",
            "RemoveLocalizationCulture",
            _currentUserName,
            $"Culture={cultureCode}");
    }

    private void CommitGridEdits()
    {
        _grid.CommitEdit(DataGridEditingUnit.Cell, true);
        _grid.CommitEdit(DataGridEditingUnit.Row, true);
    }

    private bool TryNormalizeCulture(
        string input,
        out string normalizedCultureName,
        out string nativeName,
        out string validationMessage)
    {
        normalizedCultureName = string.Empty;
        nativeName = string.Empty;
        validationMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            validationMessage = T("SYS01.Localization.CultureCodeRequired", "Kód kultury nesmí být prázdný.");
            return false;
        }

        try
        {
            var cultureInfo = CultureInfo.GetCultureInfo(input.Trim());
            normalizedCultureName = cultureInfo.Name;
            nativeName = cultureInfo.NativeName;
            return true;
        }
        catch (CultureNotFoundException)
        {
            validationMessage = string.Format(T("SYS01.Localization.InvalidCultureCode", "Kód kultury {0} není platný."), input);
            return false;
        }
    }

    private bool IsCultureVisible(string cultureName)
    {
        if (_cultureCheckBoxes.TryGetValue(cultureName, out var checkBox))
        {
            return checkBox.IsChecked == true;
        }

        return true;
    }

    private Style CreateCellTextStyle()
    {
        var style = new Style(typeof(TextBlock));

        style.Setters.Add(new Setter(TextBlock.ForegroundProperty, Application.Current.TryFindResource("DmsForegroundBrush")));
        style.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(6, 0, 6, 0)));
        style.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));

        return style;
    }

    private Style CreateEditingCellTextBoxStyle()
    {
        var style = new Style(typeof(TextBox));

        style.Setters.Add(new Setter(TextBox.ForegroundProperty, Application.Current.TryFindResource("DmsForegroundBrush")));
        style.Setters.Add(new Setter(TextBox.BackgroundProperty, Application.Current.TryFindResource("DmsBackgroundBrush")));
        style.Setters.Add(new Setter(TextBox.CaretBrushProperty, Application.Current.TryFindResource("DmsForegroundBrush")));
        style.Setters.Add(new Setter(TextBox.BorderBrushProperty, Application.Current.TryFindResource("DmsAccentBrush")));
        style.Setters.Add(new Setter(TextBox.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(TextBox.PaddingProperty, new Thickness(6, 0, 6, 0)));
        style.Setters.Add(new Setter(TextBox.VerticalContentAlignmentProperty, VerticalAlignment.Center));

        return style;
    }

    private Style CreateLocalizationRowStyle()
    {
        var style = new Style(typeof(DataGridRow));

        style.Setters.Add(new Setter(DataGridRow.ForegroundProperty, Application.Current.TryFindResource("DmsForegroundBrush")));
        style.Setters.Add(new Setter(DataGridRow.BackgroundProperty, Application.Current.TryFindResource("DmsPanelBrush")));

        style.Triggers.Add(CreateRowStateTrigger(
            nameof(DmsLocalizationRow.IsNew),
            Color.FromRgb(31, 64, 45),
            Color.FromRgb(220, 255, 230)));

        style.Triggers.Add(CreateRowStateTrigger(
            nameof(DmsLocalizationRow.IsModified),
            Color.FromRgb(75, 61, 24),
            Color.FromRgb(255, 240, 190)));

        style.Triggers.Add(CreateRowStateTrigger(
            nameof(DmsLocalizationRow.IsDeleted),
            Color.FromRgb(75, 32, 32),
            Color.FromRgb(255, 220, 220),
            0.65));

        return style;
    }

    private static DataTrigger CreateRowStateTrigger(
        string propertyName,
        Color background,
        Color foreground,
        double? opacity = null)
    {
        var trigger = new DataTrigger
        {
            Binding = new Binding(propertyName),
            Value = true
        };

        trigger.Setters.Add(new Setter(DataGridRow.BackgroundProperty, new SolidColorBrush(background)));
        trigger.Setters.Add(new Setter(DataGridRow.ForegroundProperty, new SolidColorBrush(foreground)));

        if (opacity.HasValue)
        {
            trigger.Setters.Add(new Setter(DataGridRow.OpacityProperty, opacity.Value));
        }

        return trigger;
    }

    private Style CreateLocalizationCellStyle()
    {
        var style = new Style(typeof(DataGridCell));

        style.Setters.Add(new Setter(DataGridCell.ForegroundProperty, Application.Current.TryFindResource("DmsForegroundBrush")));
        style.Setters.Add(new Setter(DataGridCell.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(DataGridCell.BorderBrushProperty, Application.Current.TryFindResource("DmsBorderBrush")));
        style.Setters.Add(new Setter(DataGridCell.BorderThicknessProperty, new Thickness(0)));

        var selectedTrigger = new Trigger
        {
            Property = DataGridCell.IsSelectedProperty,
            Value = true
        };

        selectedTrigger.Setters.Add(new Setter(DataGridCell.BackgroundProperty, Application.Current.TryFindResource("DmsAccentBrush")));
        selectedTrigger.Setters.Add(new Setter(DataGridCell.ForegroundProperty, Application.Current.TryFindResource("DmsOnAccentBrush")));
        selectedTrigger.Setters.Add(new Setter(DataGridCell.BorderBrushProperty, Application.Current.TryFindResource("DmsAccentBrush")));

        style.Triggers.Add(selectedTrigger);
        return style;
    }

    private void Grid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.Row.Item is not DmsLocalizationRow row)
        {
            return;
        }

        if (row.IsNew || row.IsDeleted)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            row.MarkModifiedIfNeeded();
        }, DispatcherPriority.Background);
    }

    private void ExportToExcel()
    {
        try
        {
            _grid.CommitEdit(DataGridEditingUnit.Cell, true);
            _grid.CommitEdit(DataGridEditingUnit.Row, true);

            var dialog = new SaveFileDialog
            {
                Title = T("SYS01.Localization.ExportExcelTitle", "Export lokalizace do Excelu"),
                Filter = "Excel workbook (*.xlsx)|*.xlsx",
                FileName = $"dms-localization-{DateTime.Now:yyyyMMdd-HHmm}.xlsx"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add("Localization");

            sheet.Cell(1, 1).Value = "Key";

            for (var i = 0; i < _index.SupportedCultures.Count; i++)
            {
                sheet.Cell(1, i + 2).Value = _index.SupportedCultures[i].Culture;
            }

            var rowIndex = 2;

            foreach (var row in _rows.Where(row => !row.IsDeleted).OrderBy(row => row.Key, StringComparer.OrdinalIgnoreCase))
            {
                sheet.Cell(rowIndex, 1).Value = row.Key;

                for (var i = 0; i < _index.SupportedCultures.Count; i++)
                {
                    var culture = _index.SupportedCultures[i].Culture;
                    row.Translations.TryGetValue(culture, out var value);
                    sheet.Cell(rowIndex, i + 2).Value = value ?? string.Empty;
                }

                rowIndex++;
            }

            var header = sheet.Range(1, 1, 1, _index.SupportedCultures.Count + 1);
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#FFE500");
            header.Style.Font.FontColor = XLColor.Black;

            sheet.SheetView.FreezeRows(1);
            sheet.Columns().AdjustToContents();
            sheet.Column(1).Width = Math.Max(sheet.Column(1).Width, 32);

            workbook.SaveAs(dialog.FileName);

            _logger.AdminAction(
                "SYS01",
                "ExportLocalizationExcel",
                _currentUserName,
                $"File={dialog.FileName}; Cultures={string.Join(", ", _index.SupportedCultures.Select(item => item.Culture))}; Keys={_rows.Count(row => !row.IsDeleted)}");

            ShowInfo(
                T("SYS01.Localization.ExportExcelTitle", "Export lokalizace do Excelu"),
                string.Format(T("SYS01.Localization.ExportExcelSuccess", "Export byl dokončen.\n\n{0}"), dialog.FileName));
        }
        catch (Exception ex)
        {
            ShowWarning(
                T("SYS01.Localization.ExportExcelTitle", "Export lokalizace do Excelu"),
                string.Format(T("SYS01.Localization.ExportExcelFailed", "Export se nepodařil.\n\n{0}"), ex.Message));
        }
    }

    private void ImportFromExcel()
    {
        try
        {
            if (HasUnsavedChanges())
            {
                var confirm = DmsConfirmDialog.Show(
                    Window.GetWindow(this),
                    T("SYS01.Localization.ImportExcelTitle", "Import lokalizace z Excelu"),
                    T("SYS01.Localization.ImportExcelUnsavedQuestion", "V gridu jsou neuložené změny. Import může některé z nich přepsat. Chceš pokračovat?"),
                    DmsDialogButtons.YesNo);

                if (confirm != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            var dialog = new OpenFileDialog
            {
                Title = T("SYS01.Localization.ImportExcelTitle", "Import lokalizace z Excelu"),
                Filter = "Excel workbook (*.xlsx)|*.xlsx"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            using var workbook = new XLWorkbook(dialog.FileName);
            var sheet = workbook.Worksheets.First();
            var usedRange = sheet.RangeUsed();

            if (usedRange is null)
            {
                ShowWarning(
                    T("SYS01.Localization.ImportExcelTitle", "Import lokalizace z Excelu"),
                    T("SYS01.Localization.ImportExcelEmpty", "Excel soubor neobsahuje žádná data."));
                return;
            }

            var headerRow = usedRange.FirstRowUsed();
            var lastRowNumber = usedRange.LastRowUsed().RowNumber();
            var lastColumnNumber = usedRange.LastColumnUsed().ColumnNumber();

            var keyColumn = 0;
            var cultureColumns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (var column = 1; column <= lastColumnNumber; column++)
            {
                var header = headerRow.Cell(column).GetString().Trim();

                if (string.Equals(header, "Key", StringComparison.OrdinalIgnoreCase))
                {
                    keyColumn = column;
                    continue;
                }

                if (_index.SupportedCultures.Any(culture => string.Equals(culture.Culture, header, StringComparison.OrdinalIgnoreCase)))
                {
                    cultureColumns[header] = column;
                }
            }

            if (keyColumn == 0)
            {
                ShowWarning(
                    T("SYS01.Localization.ImportExcelTitle", "Import lokalizace z Excelu"),
                    T("SYS01.Localization.ImportExcelMissingKey", "Excel musí obsahovat sloupec Key."));
                return;
            }

            if (cultureColumns.Count == 0)
            {
                ShowWarning(
                    T("SYS01.Localization.ImportExcelTitle", "Import lokalizace z Excelu"),
                    T("SYS01.Localization.ImportExcelMissingCultures", "Excel neobsahuje žádný podporovaný jazykový sloupec."));
                return;
            }

            var existingRows = _rows.ToDictionary(row => row.Key, StringComparer.OrdinalIgnoreCase);
            var importedRows = 0;
            var addedRows = 0;
            var modifiedRows = 0;

            for (var rowNumber = headerRow.RowNumber() + 1; rowNumber <= lastRowNumber; rowNumber++)
            {
                var key = sheet.Cell(rowNumber, keyColumn).GetString().Trim();

                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (!existingRows.TryGetValue(key, out var row))
                {
                    row = new DmsLocalizationRow
                    {
                        Key = key,
                        IsNew = true
                    };

                    foreach (var culture in _index.SupportedCultures)
                    {
                        row.Translations[culture.Culture] = string.Empty;
                    }

                    _rows.Add(row);
                    existingRows[key] = row;
                    addedRows++;
                }

                var changed = false;

                foreach (var pair in cultureColumns)
                {
                    var culture = pair.Key;
                    var column = pair.Value;
                    var importedValue = sheet.Cell(rowNumber, column).GetString();

                    row.Translations.TryGetValue(culture, out var currentValue);

                    if (!string.Equals(currentValue ?? string.Empty, importedValue ?? string.Empty, StringComparison.Ordinal))
                    {
                        row.Translations[culture] = importedValue ?? string.Empty;
                        changed = true;
                    }
                }

                if (changed && !row.IsNew && !row.IsDeleted)
                {
                    row.MarkModifiedIfNeeded();
                    modifiedRows++;
                }

                row.RefreshState();
                importedRows++;
            }

            BuildGridColumns();
            _grid.Items.Refresh();

            _logger.AdminAction(
                "SYS01",
                "ImportLocalizationExcel",
                _currentUserName,
                $"File={dialog.FileName}; ImportedRows={importedRows}; AddedRows={addedRows}; ModifiedRows={modifiedRows}");

            ShowInfo(
                T("SYS01.Localization.ImportExcelTitle", "Import lokalizace z Excelu"),
                string.Format(
                    T("SYS01.Localization.ImportExcelSuccess", "Import byl dokončen.\n\nNačteno řádků: {0}\nNové klíče: {1}\nZměněné klíče: {2}\n\nJSON soubory se zapíší až po tlačítku Uložit slovníky."),
                    importedRows,
                    addedRows,
                    modifiedRows));
        }
        catch (Exception ex)
        {
            ShowWarning(
                T("SYS01.Localization.ImportExcelTitle", "Import lokalizace z Excelu"),
                string.Format(T("SYS01.Localization.ImportExcelFailed", "Import se nepodařil.\n\n{0}"), ex.Message));
        }
    }

    private void ShowInfo(string title, string message)
    {
        DmsConfirmDialog.Show(
            Window.GetWindow(this),
            title,
            message,
            DmsDialogButtons.Ok);
    }

    private void ShowWarning(string title, string message)
    {
        DmsConfirmDialog.Show(
            Window.GetWindow(this),
            title,
            message,
            DmsDialogButtons.Ok);
    }
}

public sealed record DmsCultureChoice(string CultureCode, string DisplayText);

public sealed class DmsCultureEditDialog : Window
{
    private readonly TextBox _txtCultureCode = new();
    private readonly TextBox _txtDisplayName = new();
    private readonly CheckBox _chkIsDefault = new();

    public string CultureCode => _txtCultureCode.Text;
    public string DisplayName => _txtDisplayName.Text;
    public bool IsDefaultCulture => _chkIsDefault.IsChecked == true;

    public DmsCultureEditDialog(
        string title,
        string cultureCodeLabel,
        string displayNameLabel,
        string defaultCultureLabel,
        string cultureCodeHint,
        string okText,
        string cancelText)
    {
        Title = title;
        Width = 420;
        Height = 260;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = (Brush)(Application.Current.TryFindResource("DmsPanelBrush") ?? Brushes.White);

        var root = new Grid
        {
            Margin = new Thickness(16)
        };

        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var lblCultureCode = CreateLabel(cultureCodeLabel);
        Grid.SetRow(lblCultureCode, 0);
        root.Children.Add(lblCultureCode);

        _txtCultureCode.Height = 30;
        _txtCultureCode.Margin = new Thickness(0, 4, 0, 10);
        _txtCultureCode.ToolTip = cultureCodeHint;
        _txtCultureCode.SetResourceReference(TextBox.BackgroundProperty, "DmsBackgroundBrush");
        _txtCultureCode.SetResourceReference(TextBox.ForegroundProperty, "DmsForegroundBrush");
        _txtCultureCode.SetResourceReference(TextBox.BorderBrushProperty, "DmsBorderBrush");
        Grid.SetRow(_txtCultureCode, 1);
        root.Children.Add(_txtCultureCode);

        var displayPanel = new StackPanel();
        displayPanel.Children.Add(CreateLabel(displayNameLabel));
        _txtDisplayName.Height = 30;
        _txtDisplayName.Margin = new Thickness(0, 4, 0, 10);
        _txtDisplayName.SetResourceReference(TextBox.BackgroundProperty, "DmsBackgroundBrush");
        _txtDisplayName.SetResourceReference(TextBox.ForegroundProperty, "DmsForegroundBrush");
        _txtDisplayName.SetResourceReference(TextBox.BorderBrushProperty, "DmsBorderBrush");
        displayPanel.Children.Add(_txtDisplayName);

        _chkIsDefault.Content = defaultCultureLabel;
        _chkIsDefault.Margin = new Thickness(0, 4, 0, 0);
        _chkIsDefault.SetResourceReference(CheckBox.ForegroundProperty, "DmsForegroundBrush");
        displayPanel.Children.Add(_chkIsDefault);

        Grid.SetRow(displayPanel, 2);
        root.Children.Add(displayPanel);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var btnOk = CreateDialogButton(okText, true);
        btnOk.Click += (_, _) =>
        {
            DialogResult = true;
            Close();
        };

        var btnCancel = CreateDialogButton(cancelText, false);
        btnCancel.Click += (_, _) =>
        {
            DialogResult = false;
            Close();
        };

        buttons.Children.Add(btnOk);
        buttons.Children.Add(btnCancel);

        Grid.SetRow(buttons, 4);
        root.Children.Add(buttons);

        Content = root;
        Loaded += (_, _) => _txtCultureCode.Focus();
    }

    private static TextBlock CreateLabel(string text)
    {
        var label = new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold
        };

        label.SetResourceReference(TextBlock.ForegroundProperty, "DmsForegroundBrush");
        return label;
    }

    private static Button CreateDialogButton(string text, bool primary)
    {
        var button = new Button
        {
            Content = text,
            Width = 90,
            Height = 30,
            Margin = new Thickness(8, 0, 0, 0)
        };

        button.SetResourceReference(Button.StyleProperty, primary ? "DmsPrimaryButtonStyle" : "DmsFormButtonStyle");
        return button;
    }
}

public sealed class DmsCultureSelectDialog : Window
{
    private readonly ComboBox _cmbCultures = new();

    public string? SelectedCultureCode => (_cmbCultures.SelectedItem as DmsCultureChoice)?.CultureCode;

    public DmsCultureSelectDialog(
        string title,
        string message,
        IReadOnlyList<DmsCultureChoice> choices,
        string okText,
        string cancelText)
    {
        Title = title;
        Width = 460;
        Height = 190;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = (Brush)(Application.Current.TryFindResource("DmsPanelBrush") ?? Brushes.White);

        var root = new StackPanel
        {
            Margin = new Thickness(16)
        };

        var txtMessage = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };
        txtMessage.SetResourceReference(TextBlock.ForegroundProperty, "DmsForegroundBrush");
        root.Children.Add(txtMessage);

        _cmbCultures.ItemsSource = choices;
        _cmbCultures.DisplayMemberPath = nameof(DmsCultureChoice.DisplayText);
        _cmbCultures.SelectedValuePath = nameof(DmsCultureChoice.CultureCode);
        _cmbCultures.Height = 30;
        _cmbCultures.Margin = new Thickness(0, 0, 0, 16);
        _cmbCultures.SelectedIndex = choices.Count > 0 ? 0 : -1;
        _cmbCultures.SetResourceReference(ComboBox.BackgroundProperty, "DmsBackgroundBrush");
        _cmbCultures.SetResourceReference(ComboBox.ForegroundProperty, "DmsForegroundBrush");
        root.Children.Add(_cmbCultures);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var btnOk = CreateDialogButton(okText, true);
        btnOk.Click += (_, _) =>
        {
            DialogResult = true;
            Close();
        };

        var btnCancel = CreateDialogButton(cancelText, false);
        btnCancel.Click += (_, _) =>
        {
            DialogResult = false;
            Close();
        };

        buttons.Children.Add(btnOk);
        buttons.Children.Add(btnCancel);
        root.Children.Add(buttons);

        Content = root;
    }

    private static Button CreateDialogButton(string text, bool primary)
    {
        var button = new Button
        {
            Content = text,
            Width = 90,
            Height = 30,
            Margin = new Thickness(8, 0, 0, 0)
        };

        button.SetResourceReference(Button.StyleProperty, primary ? "DmsPrimaryButtonStyle" : "DmsFormButtonStyle");
        return button;
    }
}

public sealed class DmsLocalizationRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Key { get; set; } = string.Empty;

    public Dictionary<string, string> Translations { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> OriginalTranslations { get; } = new(StringComparer.OrdinalIgnoreCase);

    private bool _isNew;
    private bool _isModified;
    private bool _isDeleted;

    public bool IsNew
    {
        get => _isNew;
        set
        {
            if (_isNew == value)
            {
                return;
            }

            _isNew = value;
            OnPropertyChanged(nameof(IsNew));
        }
    }

    public bool IsModified
    {
        get => _isModified;
        set
        {
            if (_isModified == value)
            {
                return;
            }

            _isModified = value;
            OnPropertyChanged(nameof(IsModified));
        }
    }

    public bool IsDeleted
    {
        get => _isDeleted;
        set
        {
            if (_isDeleted == value)
            {
                return;
            }

            _isDeleted = value;
            OnPropertyChanged(nameof(IsDeleted));
        }
    }

    public void AcceptChanges()
    {
        OriginalTranslations.Clear();

        foreach (var pair in Translations)
        {
            OriginalTranslations[pair.Key] = pair.Value;
        }

        IsNew = false;
        IsModified = false;
        IsDeleted = false;
        RefreshState();
    }

    public void MarkModifiedIfNeeded()
    {
        if (IsNew || IsDeleted)
        {
            return;
        }

        foreach (var pair in Translations)
        {
            OriginalTranslations.TryGetValue(pair.Key, out var originalValue);

            if (!string.Equals(originalValue ?? string.Empty, pair.Value ?? string.Empty, StringComparison.Ordinal))
            {
                IsModified = true;
                RefreshState();
                return;
            }
        }

        IsModified = false;
        RefreshState();
    }

    public void RefreshState()
    {
        OnPropertyChanged(nameof(IsNew));
        OnPropertyChanged(nameof(IsModified));
        OnPropertyChanged(nameof(IsDeleted));
        OnPropertyChanged(nameof(Translations));
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
