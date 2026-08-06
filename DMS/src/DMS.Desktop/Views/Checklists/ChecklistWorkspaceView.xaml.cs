using DMS.Core.Checklists;
using DMS.Core.Domain.Organization;
using DMS.Core.Domain.People;
using DMS.Core.Domain.Units;
using DMS.Core.Sap;
using DMS.Desktop.Services.Checklists;
using DMS.Desktop.Configuration.Roles;
using DMS.Desktop.Services.MasterData;
using DMS.Desktop.Views.Dialogs;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.IO;

namespace DMS.Desktop.Views.Checklists;

public partial class ChecklistWorkspaceView : UserControl
{
    private readonly string _operationCode;
    private readonly List<string> _arguments;
    private readonly string _windowsLogin;
    private readonly string _displayName;
    private readonly Guid? _currentPersonId;
    private readonly IReadOnlyList<string> _currentRoles;
    private readonly IReadOnlyList<DmsRoleDefinition> _availableRoles;
    private readonly Action<string> _executeTransaction;
    private readonly Action<string, string> _audit;
    private readonly Func<string, string> _translate;
    private readonly string _dmsDataRootPath;
    private readonly ChecklistService _service;
    private readonly DmsMasterDataService _masterData;
    private readonly ChecklistCatalogService _catalogs;
    private readonly Dictionary<string, FieldEditor> _editors = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FieldEditor> _repeatingRowEditors = new();
    private List<GridRow> _allRows = new();
    private ChecklistDefinition? _definition;
    private ChecklistInstance? _instance;
    private ViewMode _mode;

    public ChecklistWorkspaceView(
        string operationCode,
        IReadOnlyList<string> arguments,
        string dmsDataRootPath,
        string windowsLogin,
        string displayName,
        Guid? currentPersonId,
        IReadOnlyList<string> currentRoles,
        IReadOnlyList<DmsRoleDefinition> availableRoles,
        Action<string> executeTransaction,
        Action<string, string> audit,
        Func<string, string> translate)
    {
        InitializeComponent();
        _operationCode = operationCode.ToUpperInvariant();
        _arguments = arguments.ToList();
        _windowsLogin = windowsLogin;
        _displayName = displayName;
        _currentPersonId = currentPersonId;
        _currentRoles = currentRoles ?? Array.Empty<string>();
        _availableRoles = availableRoles ?? Array.Empty<DmsRoleDefinition>();
        _executeTransaction = executeTransaction;
        _audit = audit;
        _translate = translate;
        _dmsDataRootPath = dmsDataRootPath;

        var checklistRoot = Path.Combine(dmsDataRootPath, "Data", "Checklists");
        var definitionRepository = new ChecklistDefinitionRepository(Path.Combine(checklistRoot, "Definitions"));
        var instanceRepository = new ChecklistInstanceRepository(Path.Combine(checklistRoot, "Instances"));
        var sapRepository = new JsonSapMaterialRepository(new SapStoragePaths(dmsDataRootPath).SapMaterialsFilePath);
        _service = new ChecklistService(definitionRepository, instanceRepository, sapRepository);
        _masterData = new DmsMasterDataService(Path.Combine(dmsDataRootPath, "Data", "MasterData"));
        _catalogs = new ChecklistCatalogService(checklistRoot);

        Loaded += (_, _) => LoadCurrentMode();
    }

    private void LoadCurrentMode()
    {
        try
        {
            switch (_operationCode)
            {
                case "CHL00": LoadDefinitionMode(); break;
                case "CHL01": LoadCreateMode(); break;
                case "CHL05": LoadOverviewMode(); break;
                case "CHL02": LoadExistingMode(editable: true); break;
                case "CHL03": LoadExistingMode(editable: false); break;
                case "CHL04": LoadCopyMode(); break;
                case "CHL06": LoadReviewMode(); break;
                default: ShowMessage("Checklist", $"Unsupported transaction {_operationCode}."); break;
            }
        }
        catch (Exception ex)
        {
            ShowMessage("Checklist", ex.Message);
        }
    }

    private void LoadDefinitionMode()
    {
        if (_arguments.Count == 0)
        {
            ShowDefinitions("CHL00");
            return;
        }

        _definition = _service.FindDefinition(_arguments[0]);
        if (_definition is null)
        {
            ShowMessage(Tr("CHL.DefinitionNotFound", "Definice nenalezena"), _arguments[0]);
            return;
        }

        ShowDefinitionEditor(_definition);
    }

    private void LoadCreateMode()
    {
        if (_arguments.Count == 0)
        {
            ShowDefinitions("CHL01");
            return;
        }

        _definition = _service.FindDefinition(_arguments[0]);
        if (_definition is null)
        {
            ShowMessage(Tr("CHL.DefinitionNotFound", "Definice nenalezena"), _arguments[0]);
            return;
        }

        if (_arguments.Count == 1)
        {
            OpenSubjectSelector(_definition);
            return;
        }

        _instance = _service.CreateDraft(_definition, _arguments[1], _windowsLogin, _displayName);
        ShowForm(_definition, _instance, editable: true, isNew: true);
    }

    private void OpenSubjectSelector(ChecklistDefinition definition)
    {
        if (definition.SubjectType is not (ChecklistSubjectType.SapArticle or ChecklistSubjectType.SapMaterial))
        {
            ShowMessage(
                Tr("CHL.UnsupportedSubjectType", "Nepodporovaný typ předmětu"),
                definition.SubjectType.ToString());
            return;
        }

        var materialKind = string.IsNullOrWhiteSpace(definition.SubjectMaterialKind)
            ? null
            : definition.SubjectMaterialKind;

        var dialog = new ArticleNumberPromptWindow(
            materialKind,
            "Výběr SAP artiklu",
            $"Vyber SAP artikl pro checklist {definition.Name}.",
            new SapStoragePaths(_dmsDataRootPath),
            logger: null,
            currentUserName: _displayName,
            translate: key =>
            {
                var translated = _translate(key);
                return string.IsNullOrWhiteSpace(translated) || translated.StartsWith("[[", StringComparison.Ordinal)
                    ? key
                    : translated;
            })
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.ArticleNumber))
            return;

        _executeTransaction($"CHL01 {definition.Code} {dialog.ArticleNumber}");
    }

    private void ShowDefinitionEditor(ChecklistDefinition definition)
    {
        _mode = ViewMode.DefinitionDetail;
        _definition = definition;
        _editors.Clear();
        _repeatingRowEditors.Clear();

        GridItems.Visibility = Visibility.Collapsed;
        FormScroll.Visibility = Visibility.Visible;
        FilterBar.Visibility = Visibility.Collapsed;
        FormPanel.Children.Clear();

        TxtTitle.Text = $"CHL00 — {definition.Code}";
        TxtSubtitle.Text = "Definice checklistu. Změny se projeví u nově otevřených formulářů.";

        var nameBox = CreateTextBox(definition.Name, editable: true);
        var descriptionBox = CreateTextBox(definition.Description, editable: true);
        descriptionBox.AcceptsReturn = true;
        descriptionBox.TextWrapping = TextWrapping.Wrap;
        descriptionBox.MinHeight = 65;

        var prefixBox = CreateTextBox(
            string.IsNullOrWhiteSpace(definition.NumberPrefix)
                ? (string.Equals(definition.Code, "VZRMET", StringComparison.OrdinalIgnoreCase) ? "VzrMet" : definition.Code)
                : definition.NumberPrefix,
            editable: true);

        var versionBox = CreateTextBox(definition.Version.ToString(CultureInfo.InvariantCulture), editable: true);
        var subjectCombo = new ComboBox
        {
            ItemsSource = Enum.GetValues<ChecklistSubjectType>(),
            SelectedItem = definition.SubjectType,
            MinHeight = 30
        };
        var materialKindBox = CreateTextBox(definition.SubjectMaterialKind, editable: true);
        var activeCheck = new CheckBox { Content = "Aktivní", IsChecked = definition.IsActive };
        var multipleCheck = new CheckBox { Content = "Povolit více checklistů pro stejný objekt", IsChecked = definition.AllowMultipleInstancesPerSubject };
        var copyCheck = new CheckBox { Content = "Povolit kopírování", IsChecked = definition.SupportsCopy };
        var reviewCheck = new CheckBox { Content = "Vyžadovat kontrolu", IsChecked = definition.RequiresReview };

        FormPanel.Children.Add(CreateEditableCard("Název", nameBox));
        FormPanel.Children.Add(CreateEditableCard("Popis", descriptionBox));
        FormPanel.Children.Add(CreateEditableCard("Prefix čísla checklistu", prefixBox));
        FormPanel.Children.Add(CreateEditableCard("Verze definice", versionBox));
        FormPanel.Children.Add(CreateEditableCard("Typ předmětu", subjectCombo));
        FormPanel.Children.Add(CreateEditableCard("Typ SAP materiálu", materialKindBox));

        var flags = new StackPanel { Orientation = Orientation.Vertical };
        flags.Children.Add(activeCheck);
        flags.Children.Add(multipleCheck);
        flags.Children.Add(copyCheck);
        flags.Children.Add(reviewCheck);
        FormPanel.Children.Add(CreateEditableCard("Chování", flags));

        var allowOwnApprovalCheck = new CheckBox
        {
            Content = "Autor smí potvrdit kontrolu vlastního checklistu",
            IsChecked = definition.AllowAuthorToApproveOwnChecklist,
            Margin = new Thickness(0, 0, 0, 8)
        };

        var approvalRolesPanel = new StackPanel { Orientation = Orientation.Vertical };
        approvalRolesPanel.Children.Add(new TextBlock
        {
            Text = "Pokud není vybrána žádná role, může kontrolu potvrdit každý uživatel oprávněný ke spuštění CHL06.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
        approvalRolesPanel.Children.Add(allowOwnApprovalCheck);

        var approvalRoleChecks = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);
        foreach (var role in _availableRoles.Where(x => x.IsActive).OrderBy(x => x.Code))
        {
            var roleCheck = new CheckBox
            {
                Content = string.IsNullOrWhiteSpace(role.Name)
                    ? role.Code
                    : $"{role.Code} — {role.Name}",
                IsChecked = definition.AllowedApprovalRoleCodes.Any(x =>
                    string.Equals(x, role.Code, StringComparison.OrdinalIgnoreCase)),
                Margin = new Thickness(0, 2, 0, 2),
                ToolTip = role.Description
            };
            approvalRoleChecks[role.Code] = roleCheck;
            approvalRolesPanel.Children.Add(roleCheck);
        }

        FormPanel.Children.Add(CreateEditableCard("Kdo může potvrdit kontrolu", approvalRolesPanel));

        FormPanel.Children.Add(CreateSectionHeader("Sekce a pole"));

        var rows = new ObservableCollection<DefinitionFieldRow>(
            definition.Sections
                .OrderBy(section => section.SortOrder)
                .SelectMany(section => section.Fields
                    .OrderBy(field => field.SortOrder)
                    .Select(field => DefinitionFieldRow.From(section, field))));

        var fieldsGrid = CreateDefinitionFieldsGrid(rows);
        FormPanel.Children.Add(fieldsGrid);

        var rowButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var addButton = new Button { Content = "Přidat pole", Width = 125, Margin = new Thickness(0, 0, 8, 0) };
        addButton.Click += (_, _) =>
        {
            var nextOrder = rows.Count == 0 ? 10 : rows.Max(x => x.FieldSortOrder) + 10;
            rows.Add(new DefinitionFieldRow
            {
                SectionCode = "NEW_SECTION",
                SectionTitle = "Nová sekce",
                SectionSortOrder = 999,
                FieldCode = "NEW_FIELD",
                Label = "Nové pole",
                FieldType = ChecklistFieldType.Text,
                FieldSortOrder = nextOrder
            });
            fieldsGrid.SelectedItem = rows[^1];
            fieldsGrid.ScrollIntoView(rows[^1]);
        };

        var removeButton = new Button { Content = "Odebrat pole", Width = 125 };
        removeButton.Click += (_, _) =>
        {
            if (fieldsGrid.SelectedItem is DefinitionFieldRow selected)
                rows.Remove(selected);
        };

        rowButtons.Children.Add(addButton);
        rowButtons.Children.Add(removeButton);
        FormPanel.Children.Add(rowButtons);

        BtnPrimary.Content = "Uložit definici";
        BtnPrimary.Tag = "SAVE_DEFINITION";
        BtnPrimary.Visibility = Visibility.Visible;
        BtnSecondary.Visibility = Visibility.Collapsed;

        _definitionSaveAction = () =>
        {
            definition.Name = nameBox.Text.Trim();
            definition.Description = descriptionBox.Text.Trim();
            definition.NumberPrefix = prefixBox.Text.Trim();
            definition.Version = int.TryParse(versionBox.Text, out var version) && version > 0
                ? version
                : definition.Version;
            definition.SubjectType = subjectCombo.SelectedItem is ChecklistSubjectType subject
                ? subject
                : definition.SubjectType;
            definition.SubjectMaterialKind = string.IsNullOrWhiteSpace(materialKindBox.Text)
                ? null
                : materialKindBox.Text.Trim();
            definition.IsActive = activeCheck.IsChecked == true;
            definition.AllowMultipleInstancesPerSubject = multipleCheck.IsChecked == true;
            definition.SupportsCopy = copyCheck.IsChecked == true;
            definition.RequiresReview = reviewCheck.IsChecked == true;
            definition.AllowAuthorToApproveOwnChecklist = allowOwnApprovalCheck.IsChecked == true;
            definition.AllowedApprovalRoleCodes = approvalRoleChecks
                .Where(x => x.Value.IsChecked == true)
                .Select(x => x.Key)
                .OrderBy(x => x)
                .ToList();
            definition.Sections = BuildSections(rows);
        };
    }

    private Action? _definitionSaveAction;

    private static List<ChecklistSectionDefinition> BuildSections(IEnumerable<DefinitionFieldRow> rows)
    {
        return rows
            .GroupBy(row => new
            {
                Code = row.SectionCode.Trim(),
                Title = row.SectionTitle.Trim(),
                row.SectionSortOrder
            })
            .OrderBy(group => group.Key.SectionSortOrder)
            .Select(group => new ChecklistSectionDefinition
            {
                Code = group.Key.Code,
                Title = group.Key.Title,
                SortOrder = group.Key.SectionSortOrder,
                Fields = group
                    .OrderBy(row => row.FieldSortOrder)
                    .Select(row => new ChecklistFieldDefinition
                    {
                        Code = row.FieldCode.Trim(),
                        Label = row.Label.Trim(),
                        FieldType = row.FieldType,
                        SortOrder = row.FieldSortOrder,
                        IsRequired = row.IsRequired,
                        IsReadOnly = row.IsReadOnly,
                        UnitDimensionCode = NullIfWhiteSpace(row.UnitDimensionCode),
                        DefaultUnitCode = NullIfWhiteSpace(row.DefaultUnitCode),
                        SourceBinding = NullIfWhiteSpace(row.SourceBinding),
                        CatalogCode = NullIfWhiteSpace(row.CatalogCode),
                        AllowMultipleValues = row.AllowMultipleValues
                    })
                    .ToList()
            })
            .ToList();
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DataGrid CreateDefinitionFieldsGrid(ObservableCollection<DefinitionFieldRow> rows)
    {
        var grid = new DataGrid
        {
            ItemsSource = rows,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserResizeColumns = true,
            MinHeight = 300,
            MaxHeight = 520,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow
        };

        grid.Columns.Add(new DataGridTextColumn { Header = "Sekce", Binding = new System.Windows.Data.Binding(nameof(DefinitionFieldRow.SectionCode)), Width = 115 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Název sekce", Binding = new System.Windows.Data.Binding(nameof(DefinitionFieldRow.SectionTitle)), Width = 180 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Poř. sekce", Binding = new System.Windows.Data.Binding(nameof(DefinitionFieldRow.SectionSortOrder)), Width = 90 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Kód pole", Binding = new System.Windows.Data.Binding(nameof(DefinitionFieldRow.FieldCode)), Width = 140 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Název pole", Binding = new System.Windows.Data.Binding(nameof(DefinitionFieldRow.Label)), Width = new DataGridLength(2, DataGridLengthUnitType.Star), MinWidth = 200 });

        var typeColumn = new DataGridComboBoxColumn
        {
            Header = "Typ",
            ItemsSource = Enum.GetValues<ChecklistFieldType>(),
            SelectedItemBinding = new System.Windows.Data.Binding(nameof(DefinitionFieldRow.FieldType)),
            Width = 145
        };
        grid.Columns.Add(typeColumn);

        grid.Columns.Add(new DataGridTextColumn { Header = "Pořadí", Binding = new System.Windows.Data.Binding(nameof(DefinitionFieldRow.FieldSortOrder)), Width = 80 });
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Povinné", Binding = new System.Windows.Data.Binding(nameof(DefinitionFieldRow.IsRequired)), Width = 75 });
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Jen čtení", Binding = new System.Windows.Data.Binding(nameof(DefinitionFieldRow.IsReadOnly)), Width = 80 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Veličina", Binding = new System.Windows.Data.Binding(nameof(DefinitionFieldRow.UnitDimensionCode)), Width = 110 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Jednotka", Binding = new System.Windows.Data.Binding(nameof(DefinitionFieldRow.DefaultUnitCode)), Width = 90 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Zdroj", Binding = new System.Windows.Data.Binding(nameof(DefinitionFieldRow.SourceBinding)), Width = 170 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Katalog CHLSET", Binding = new System.Windows.Data.Binding(nameof(DefinitionFieldRow.CatalogCode)), Width = 140 });
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Více hodnot", Binding = new System.Windows.Data.Binding(nameof(DefinitionFieldRow.AllowMultipleValues)), Width = 90 });

        return grid;
    }

    private void LoadOverviewMode()
    {
        if (_arguments.Count == 0)
        {
            ShowDefinitionOverview();
            return;
        }

        ShowInstances(typeCode: _arguments[0], subjectOrNumber: null, operationForOpen: "CHL03");
    }

    private void LoadExistingMode(bool editable)
    {
        if (_arguments.Count == 0)
        {
            ShowInstances(null, null, editable ? "CHL02" : "CHL03");
            return;
        }

        var argument = _arguments[0];
        var direct = _service.FindInstance(argument);
        if (direct is not null)
        {
            var definition = _service.FindDefinition(direct.DefinitionCode)
                ?? throw new InvalidOperationException($"Definition {direct.DefinitionCode} was not found.");
            ShowForm(definition, direct, editable && direct.Status is ChecklistStatus.Draft or ChecklistStatus.InProgress or ChecklistStatus.ReturnedForCorrection, isNew: false);
            return;
        }

        ShowInstances(null, argument, editable ? "CHL02" : "CHL03");
    }

    private void LoadCopyMode()
    {
        if (_arguments.Count == 0)
        {
            ShowInstances(null, null, "CHL04");
            return;
        }

        var source = _service.FindInstance(_arguments[0]);
        if (source is null)
        {
            ShowInstances(null, _arguments[0], "CHL04");
            return;
        }

        _definition = _service.FindDefinition(source.DefinitionCode)
            ?? throw new InvalidOperationException($"Definition {source.DefinitionCode} was not found.");
        _instance = _service.CopyAsDraft(source, _windowsLogin, _displayName);
        ShowForm(_definition, _instance, editable: true, isNew: true);
        TxtSubtitle.Text = $"Kopie z {source.ChecklistNumber}. Schválení a stav se nekopírují.";
    }

    private void LoadReviewMode()
    {
        if (_arguments.Count == 0)
        {
            ShowInstances(null, null, "CHL06", onlyReviewable: true);
            return;
        }

        var direct = _service.FindInstance(_arguments[0]);
        if (direct is null)
        {
            ShowInstances(null, _arguments[0], "CHL06", onlyReviewable: true);
            return;
        }

        _definition = _service.FindDefinition(direct.DefinitionCode)
            ?? throw new InvalidOperationException($"Definition {direct.DefinitionCode} was not found.");
        ShowForm(_definition, direct, editable: false, isNew: false);
        var canApprove = CanCurrentUserApprove(_definition, direct, out var approvalReason);
        BtnPrimary.Content = "Potvrdit kontrolu";
        BtnPrimary.Visibility = Visibility.Visible;
        BtnPrimary.IsEnabled = canApprove;
        BtnPrimary.ToolTip = canApprove ? null : approvalReason;
        BtnPrimary.Tag = "REVIEW";
        BtnSecondary.Content = "Vrátit k opravě";
        BtnSecondary.Visibility = Visibility.Visible;
        BtnSecondary.Tag = "RETURN";
    }

    private void ShowDefinitions(string openOperation)
    {
        _mode = ViewMode.Definitions;
        TxtTitle.Text = $"{_operationCode} — {Tr("CHL.AvailableDefinitions", "Dostupné checklisty")}";
        TxtSubtitle.Text = openOperation == "CHL01"
            ? "Vyber typ checklistu, který chceš vyplnit."
            : "Vyber definici checklistu.";
        var definitions = _service.LoadDefinitions().Where(x => x.IsActive).ToList();
        _allRows = definitions.Select(x => new GridRow
        {
            Primary = x.Code,
            Secondary = x.Name,
            Tertiary = x.SubjectType.ToString(),
            Modified = $"v{x.Version}",
            Command = $"{openOperation} {x.Code}"
        }).ToList();
        ShowGrid();
    }

    private void ShowDefinitionOverview()
    {
        var instances = _service.LoadInstances();
        _allRows = _service.LoadDefinitions().Where(x => x.IsActive).Select(x => new GridRow
        {
            Primary = x.Code,
            Secondary = x.Name,
            Tertiary = $"{instances.Count(i => string.Equals(i.DefinitionCode, x.Code, StringComparison.OrdinalIgnoreCase))} záznamů",
            Modified = $"v{x.Version}",
            Command = $"CHL05 {x.Code}"
        }).ToList();
        TxtTitle.Text = "CHL05 — Přehled checklistů";
        TxtSubtitle.Text = "Vyber typ checklistu pro zobrazení jeho záznamů.";
        ShowGrid();
    }

    private void ShowInstances(string? typeCode, string? subjectOrNumber, string operationForOpen, bool onlyReviewable = false)
    {
        var query = _service.LoadInstances().AsEnumerable();
        if (!string.IsNullOrWhiteSpace(typeCode))
            query = query.Where(x => string.Equals(x.DefinitionCode, typeCode, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(subjectOrNumber))
            query = query.Where(x => string.Equals(x.SubjectReference, subjectOrNumber, StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(x.ChecklistNumber, subjectOrNumber, StringComparison.OrdinalIgnoreCase));
        if (onlyReviewable)
            query = query.Where(x => x.Status == ChecklistStatus.SubmittedForReview);

        _allRows = query.Select(x => new GridRow
        {
            Primary = x.ChecklistNumber,
            Secondary = $"{x.SubjectReference} — {x.SubjectDisplayName}",
            Tertiary = $"{x.DefinitionCode} · {x.Status}",
            Modified = x.ModifiedAt.ToString("dd.MM.yyyy HH:mm"),
            Command = $"{operationForOpen} {x.ChecklistNumber}"
        }).ToList();
        TxtTitle.Text = $"{_operationCode} — Checklisty";
        TxtSubtitle.Text = _allRows.Count == 0 ? "Nebyly nalezeny žádné odpovídající checklisty." : "Dvojklik otevře vybraný checklist.";
        ShowGrid();
    }

    private void ShowGrid()
    {
        _mode = ViewMode.Grid;
        _definitionSaveAction = null;
        FormScroll.Visibility = Visibility.Collapsed;
        GridItems.Visibility = Visibility.Visible;
        FilterBar.Visibility = Visibility.Visible;
        BtnPrimary.Visibility = Visibility.Collapsed;
        BtnSecondary.Visibility = Visibility.Collapsed;
        ApplyGridFilter();
    }

    private void ShowForm(ChecklistDefinition definition, ChecklistInstance instance, bool editable, bool isNew)
    {
        _mode = editable ? ViewMode.EditForm : ViewMode.ReadForm;
        _definition = definition;
        _instance = instance;
        _editors.Clear();
        _repeatingRowEditors.Clear();
        _definitionSaveAction = null;
        GridItems.Visibility = Visibility.Collapsed;
        FormScroll.Visibility = Visibility.Visible;
        FilterBar.Visibility = Visibility.Collapsed;
        FormPanel.Children.Clear();
        TxtTitle.Text = isNew ? $"CHL01 — {definition.Name}" : $"{instance.ChecklistNumber} — {definition.Name}";
        TxtSubtitle.Text = $"SAP artikl: {instance.SubjectReference} · Stav: {instance.Status} · Autor: {instance.CreatedByDisplayName}";

        foreach (var section in definition.Sections.OrderBy(x => x.SortOrder))
        {
            FormPanel.Children.Add(CreateSectionHeader(section.Title));
            foreach (var field in section.Fields.OrderBy(x => x.SortOrder))
            {
                FormPanel.Children.Add(CreateFieldEditor(field, instance, editable));
            }
        }

        BtnPrimary.Content = isNew ? "Uložit koncept" : "Uložit změny";
        BtnPrimary.Visibility = editable ? Visibility.Visible : Visibility.Collapsed;
        BtnPrimary.Tag = "SAVE";
        BtnSecondary.Content = "Odeslat ke kontrole";
        BtnSecondary.Tag = "SUBMIT";
        BtnSecondary.Visibility = editable && definition.RequiresReview
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private FrameworkElement CreateFieldEditor(ChecklistFieldDefinition field, ChecklistInstance instance, bool editable)
    {
        var border = new Border
        {
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1)
        };
        border.SetResourceReference(Border.BackgroundProperty, "DmsPanelBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "DmsBorderBrush");

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var label = new TextBlock
        {
            Text = field.Label + (field.IsRequired ? " *" : string.Empty),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 12, 0)
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "DmsForegroundBrush");
        grid.Children.Add(label);

        instance.Values.TryGetValue(field.Code, out var value);
        value ??= new ChecklistFieldValue();
        instance.Values[field.Code] = value;

        if (!editable || field.IsReadOnly)
        {
            var display = CreateReadOnlyValue(field, value);
            Grid.SetColumn(display, 1);
            grid.Children.Add(display);
            border.Child = grid;
            return border;
        }

        var editor = BuildEditor(field, value, editable: true);
        Grid.SetColumn(editor.Element, 1);
        grid.Children.Add(editor.Element);
        _editors[field.Code] = editor;
        border.Child = grid;
        return border;
    }

    private FieldEditor BuildEditor(ChecklistFieldDefinition field, ChecklistFieldValue value, bool editable)
    {
        switch (field.FieldType)
        {
            case ChecklistFieldType.Boolean:
                var check = new CheckBox { IsChecked = value.BooleanValue, IsEnabled = editable, VerticalAlignment = VerticalAlignment.Center };
                return new FieldEditor(check, () => value.BooleanValue = check.IsChecked);

            case ChecklistFieldType.Integer:
                var integer = CreateTextBox(value.IntegerValue?.ToString(CultureInfo.CurrentCulture), editable);
                return new FieldEditor(integer, () => value.IntegerValue = int.TryParse(integer.Text, out var v) ? v : null);

            case ChecklistFieldType.Decimal:
                var number = CreateTextBox(value.DecimalValue?.ToString(CultureInfo.CurrentCulture), editable);
                return new FieldEditor(number, () => value.DecimalValue = decimal.TryParse(number.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var v) ? v : null);

            case ChecklistFieldType.MultilineText:
                var multi = CreateTextBox(value.TextValue, editable);
                multi.AcceptsReturn = true;
                multi.TextWrapping = TextWrapping.Wrap;
                multi.MinHeight = 70;
                return new FieldEditor(multi, () => value.TextValue = multi.Text.Trim());

            case ChecklistFieldType.CatalogValue:
                return BuildCatalogEditor(field, value, editable);

            case ChecklistFieldType.RepeatingGroup:
                return BuildRepeatingGroupEditor(field, value, editable);

            case ChecklistFieldType.Person:
                var people = _masterData.LoadPeople().Where(x => x.IsActive).OrderBy(x => x.LastName).ThenBy(x => x.FirstName).ToList();
                var personCombo = ChoiceCombo(people.Select(x => new Choice(x.PersonId, $"{x.PersonnelNumber} — {x.DisplayName}")).ToList(), value.PersonId, editable);
                return new FieldEditor(personCombo, () => value.PersonId = (personCombo.SelectedItem as Choice)?.Id);

            case ChecklistFieldType.OrganizationUnit:
                var units = _masterData.LoadOrganizationUnits().Where(x => x.IsActive).OrderBy(x => x.Name).ToList();
                var unitCombo = ChoiceCombo(units.Select(x => new Choice(x.OrganizationUnitId, $"{x.Code} — {x.Name}")).ToList(), value.OrganizationUnitId, editable);
                return new FieldEditor(unitCombo, () => value.OrganizationUnitId = (unitCombo.SelectedItem as Choice)?.Id);

            case ChecklistFieldType.Measurement:
                return BuildMeasurementEditor(field, value, editable);

            default:
                var text = CreateTextBox(value.TextValue, editable);
                return new FieldEditor(text, () => value.TextValue = text.Text.Trim());
        }
    }

    private FieldEditor BuildCatalogEditor(ChecklistFieldDefinition field, ChecklistFieldValue value, bool editable)
    {
        var catalog = _catalogs.Find(field.CatalogCode);
        var items = catalog?.Items
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.DisplayText)
            .ToList()
            ?? field.CatalogValues.Select((text, index) => new ChecklistCatalogItem
            {
                Code = text,
                DisplayText = text,
                SortOrder = index
            }).ToList();

        if (!field.AllowMultipleValues)
        {
            var choices = items.Select(x => new CatalogChoice(x.Code, x.DisplayText)).ToList();
            var combo = new ComboBox
            {
                ItemsSource = choices,
                IsEnabled = editable,
                MinHeight = 30
            };
            combo.ItemTemplate = CreateDisplayTemplate(nameof(CatalogChoice.DisplayText));

            var selectedCode = value.CatalogSelections.FirstOrDefault()?.ItemCode ?? value.TextValue;
            combo.SelectedItem = choices.FirstOrDefault(x =>
                string.Equals(x.Code, selectedCode, StringComparison.OrdinalIgnoreCase));

            return new FieldEditor(combo, () =>
            {
                value.CatalogSelections.Clear();
                if (combo.SelectedItem is CatalogChoice selected)
                {
                    value.TextValue = selected.Code;
                    value.CatalogSelections.Add(new ChecklistCatalogSelection
                    {
                        CatalogCode = field.CatalogCode ?? string.Empty,
                        ItemCode = selected.Code,
                        DisplayTextSnapshot = selected.DisplayText
                    });
                }
                else
                {
                    value.TextValue = null;
                }
            });
        }

        var panel = new StackPanel();
        var checkBoxes = new List<(CheckBox CheckBox, ChecklistCatalogItem Item)>();

        foreach (var item in items)
        {
            var check = new CheckBox
            {
                Content = item.DisplayText,
                IsEnabled = editable,
                Margin = new Thickness(0, 2, 0, 2),
                IsChecked = value.CatalogSelections.Any(x =>
                    string.Equals(x.ItemCode, item.Code, StringComparison.OrdinalIgnoreCase))
            };
            panel.Children.Add(check);
            checkBoxes.Add((check, item));
        }

        return new FieldEditor(panel, () =>
        {
            value.CatalogSelections = checkBoxes
                .Where(x => x.CheckBox.IsChecked == true)
                .Select(x => new ChecklistCatalogSelection
                {
                    CatalogCode = field.CatalogCode ?? string.Empty,
                    ItemCode = x.Item.Code,
                    DisplayTextSnapshot = x.Item.DisplayText
                })
                .ToList();

            value.TextValue = string.Join(";",
                value.CatalogSelections.Select(x => x.ItemCode));
        });
    }

    private FieldEditor BuildRepeatingGroupEditor(ChecklistFieldDefinition field, ChecklistFieldValue value, bool editable)
    {
        var root = new StackPanel();
        var rowsPanel = new StackPanel();
        root.Children.Add(rowsPanel);

        void RenderRows()
        {
            rowsPanel.Children.Clear();

            foreach (var row in value.RepeatingRows.OrderBy(x => x.SortOrder).ToList())
            {
                var rowBorder = new Border
                {
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(10),
                    Margin = new Thickness(0, 0, 0, 6)
                };
                rowBorder.SetResourceReference(Border.BorderBrushProperty, "DmsBorderBrush");

                var rowGrid = new Grid();
                var editors = new List<FieldEditor>();
                var column = 0;

                foreach (var child in field.ChildFields.OrderBy(x => x.SortOrder))
                {
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition
                    {
                        Width = child.FieldType == ChecklistFieldType.Measurement
                            ? new GridLength(2, GridUnitType.Star)
                            : new GridLength(1, GridUnitType.Star)
                    });

                    if (!row.Values.TryGetValue(child.Code, out var childValue))
                    {
                        childValue = new ChecklistFieldValue();
                        row.Values[child.Code] = childValue;
                    }

                    var childStack = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
                    childStack.Children.Add(new TextBlock
                    {
                        Text = child.Label,
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(0, 0, 0, 3)
                    });

                    var childEditor = child.FieldType == ChecklistFieldType.Measurement
                        ? BuildMeasurementEditor(child, childValue, editable)
                        : BuildEditor(child, childValue, editable);

                    childStack.Children.Add(childEditor.Element);
                    editors.Add(childEditor);
                    Grid.SetColumn(childStack, column++);
                    rowGrid.Children.Add(childStack);
                }

                if (editable)
                {
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var remove = new Button
                    {
                        Content = "Odebrat",
                        MinWidth = 80,
                        Margin = new Thickness(4, 20, 0, 0)
                    };
                    remove.Click += (_, _) =>
                    {
                        foreach (var editor in editors) editor.Save();
                        value.RepeatingRows.Remove(row);
                        NormalizeRepeatingRows(value);
                        RenderRows();
                    };
                    Grid.SetColumn(remove, column);
                    rowGrid.Children.Add(remove);
                }

                rowBorder.Child = rowGrid;
                rowsPanel.Children.Add(rowBorder);

                _repeatingRowEditors.AddRange(editors);
            }
        }

        if (value.RepeatingRows.Count == 0 && editable)
        {
            value.RepeatingRows.Add(new ChecklistRepeatingRow { SortOrder = 10 });
        }

        RenderRows();

        if (editable)
        {
            var add = new Button
            {
                Content = "Přidat pistoli",
                HorizontalAlignment = HorizontalAlignment.Left,
                MinWidth = 120,
                Margin = new Thickness(0, 4, 0, 0)
            };
            add.Click += (_, _) =>
            {
                foreach (var editor in _repeatingRowEditors) editor.Save();
                value.RepeatingRows.Add(new ChecklistRepeatingRow
                {
                    SortOrder = value.RepeatingRows.Count == 0
                        ? 10
                        : value.RepeatingRows.Max(x => x.SortOrder) + 10
                });
                RenderRows();
            };
            root.Children.Add(add);
        }

        return new FieldEditor(root, () =>
        {
            foreach (var editor in _repeatingRowEditors) editor.Save();
            NormalizeRepeatingRows(value);
            value.IntegerValue = value.RepeatingRows.Count;
        });
    }

    private static void NormalizeRepeatingRows(ChecklistFieldValue value)
    {
        var order = 10;
        foreach (var row in value.RepeatingRows.OrderBy(x => x.SortOrder))
        {
            row.SortOrder = order;
            order += 10;
        }
    }

    private static DataTemplate CreateDisplayTemplate(string propertyName)
    {
        var template = new DataTemplate();
        var factory = new FrameworkElementFactory(typeof(TextBlock));
        factory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(propertyName));
        factory.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        template.VisualTree = factory;
        return template;
    }

    private FieldEditor BuildMeasurementEditor(ChecklistFieldDefinition field, ChecklistFieldValue value, bool editable)
    {
        var panel = new Grid();
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });

        var number = CreateTextBox(value.DecimalValue?.ToString(CultureInfo.CurrentCulture), editable);
        var dimensions = _masterData.LoadUnitDimensions();
        var dimension = dimensions.FirstOrDefault(x =>
            string.Equals(x.Code, field.UnitDimensionCode, StringComparison.OrdinalIgnoreCase));

        var units = dimension is null
            ? new List<UnitDefinition>()
            : _masterData.LoadUnits()
                .Where(x => x.UnitDimensionId == dimension.UnitDimensionId && x.IsActive)
                .OrderByDescending(x => string.Equals(x.Code, field.DefaultUnitCode, StringComparison.OrdinalIgnoreCase))
                .ThenBy(x => x.Code)
                .ToList();

        var choices = units
            .Select(unit => new UnitChoice(unit, $"{unit.Symbol} — {unit.Name}"))
            .ToList();

        var combo = new ComboBox
        {
            ItemsSource = choices,
            IsEnabled = editable,
            Margin = new Thickness(8, 0, 0, 0),
            MinHeight = 30
        };

        combo.SelectedItem = choices.FirstOrDefault(x =>
            string.Equals(
                x.Unit.Code,
                value.EnteredUnitCode ?? field.DefaultUnitCode,
                StringComparison.OrdinalIgnoreCase));

        Grid.SetColumn(combo, 1);
        panel.Children.Add(number);
        panel.Children.Add(combo);

        return new FieldEditor(panel, () =>
        {
            value.DecimalValue = decimal.TryParse(
                number.Text,
                NumberStyles.Number,
                CultureInfo.CurrentCulture,
                out var parsed)
                    ? parsed
                    : null;

            value.EnteredUnitCode = (combo.SelectedItem as UnitChoice)?.Unit.Code;

            if (value.DecimalValue.HasValue &&
                combo.SelectedItem is UnitChoice selected &&
                dimension is not null)
            {
                var baseUnit = units.FirstOrDefault(x =>
                    string.Equals(x.Code, dimension.BaseUnitCode, StringComparison.OrdinalIgnoreCase));

                if (baseUnit is not null)
                {
                    value.NormalizedValue = new UnitConversionService()
                        .Convert(value.DecimalValue.Value, selected.Unit, baseUnit);
                    value.NormalizedUnitCode = baseUnit.Code;
                }
            }
        });
    }

    private FrameworkElement CreateReadOnlyValue(ChecklistFieldDefinition field, ChecklistFieldValue value)
    {
        string text;

        switch (field.FieldType)
        {
            case ChecklistFieldType.Boolean:
                text = value.BooleanValue switch
                {
                    true => "Ano",
                    false => "Ne",
                    _ => "—"
                };
                break;

            case ChecklistFieldType.Integer:
                text = value.IntegerValue?.ToString(CultureInfo.CurrentCulture) ?? "—";
                break;

            case ChecklistFieldType.Decimal:
                text = value.DecimalValue?.ToString(CultureInfo.CurrentCulture) ?? "—";
                break;

            case ChecklistFieldType.Measurement:
                var number = value.DecimalValue?.ToString(CultureInfo.CurrentCulture) ?? "—";
                var unit = ResolveUnitDisplay(value.EnteredUnitCode ?? field.DefaultUnitCode);
                text = string.IsNullOrWhiteSpace(unit) ? number : $"{number} {unit}";
                break;

            case ChecklistFieldType.CatalogValue:
                text = value.CatalogSelections.Count > 0
                    ? string.Join(", ", value.CatalogSelections.Select(x => x.DisplayTextSnapshot))
                    : (string.IsNullOrWhiteSpace(value.TextValue) ? "—" : value.TextValue);
                break;

            case ChecklistFieldType.RepeatingGroup:
                text = value.RepeatingRows.Count == 0
                    ? "—"
                    : string.Join(Environment.NewLine,
                        value.RepeatingRows.OrderBy(x => x.SortOrder).Select((row, index) =>
                            $"Pistole {index + 1}: {FormatRepeatingRow(field, row)}"));
                break;

            case ChecklistFieldType.Person:
                text = ResolvePersonDisplay(value.PersonId);
                break;

            case ChecklistFieldType.OrganizationUnit:
                text = ResolveOrganizationDisplay(value.OrganizationUnitId);
                break;

            default:
                text = string.IsNullOrWhiteSpace(value.TextValue) ? "—" : value.TextValue;
                break;
        }

        var block = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(8, 6, 8, 6)
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, "DmsForegroundBrush");
        return block;
    }

    private string FormatRepeatingRow(ChecklistFieldDefinition field, ChecklistRepeatingRow row)
    {
        var values = new List<string>();
        foreach (var child in field.ChildFields.OrderBy(x => x.SortOrder))
        {
            if (!row.Values.TryGetValue(child.Code, out var value))
                continue;

            if (child.FieldType == ChecklistFieldType.Measurement)
            {
                var number = value.DecimalValue?.ToString(CultureInfo.CurrentCulture) ?? "—";
                values.Add($"{child.Label} {number} {ResolveUnitDisplay(value.EnteredUnitCode ?? child.DefaultUnitCode)}".Trim());
            }
            else if (child.FieldType == ChecklistFieldType.Integer)
            {
                values.Add($"{child.Label} {value.IntegerValue?.ToString(CultureInfo.CurrentCulture) ?? "—"}");
            }
            else
            {
                values.Add($"{child.Label} {value.TextValue ?? "—"}");
            }
        }
        return string.Join("; ", values);
    }

    private string ResolveUnitDisplay(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return string.Empty;

        var unit = _masterData.LoadUnits().FirstOrDefault(x =>
            string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));

        return unit?.Symbol ?? code;
    }

    private string ResolvePersonDisplay(Guid? personId)
    {
        if (!personId.HasValue)
            return "—";

        var person = _masterData.LoadPeople().FirstOrDefault(x => x.PersonId == personId.Value);
        return person is null
            ? personId.Value.ToString()
            : $"{person.PersonnelNumber} — {person.DisplayName}";
    }

    private string ResolveOrganizationDisplay(Guid? organizationUnitId)
    {
        if (!organizationUnitId.HasValue)
            return "—";

        var organization = _masterData.LoadOrganizationUnits()
            .FirstOrDefault(x => x.OrganizationUnitId == organizationUnitId.Value);

        return organization is null
            ? organizationUnitId.Value.ToString()
            : $"{organization.Code} — {organization.Name}";
    }

    private static TextBox CreateTextBox(string? value, bool editable) => new()
    {
        Text = value ?? string.Empty,
        IsReadOnly = !editable,
        MinHeight = 30,
        VerticalContentAlignment = VerticalAlignment.Center
    };

    private static ComboBox ChoiceCombo(List<Choice> items, Guid? selectedId, bool editable)
    {
        var combo = new ComboBox { ItemsSource = items, DisplayMemberPath = nameof(Choice.DisplayText), IsEnabled = editable, MinHeight = 30 };
        combo.SelectedItem = items.FirstOrDefault(x => x.Id == selectedId);
        return combo;
    }

    private void SaveEditors()
    {
        foreach (var editor in _editors.Values) editor.Save();
    }

    private void BtnPrimary_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Equals(BtnPrimary.Tag, "SAVE_DEFINITION") && _definition is not null)
            {
                _definitionSaveAction?.Invoke();
                _service.SaveDefinition(_definition);
                _audit("AUDIT", $"ChecklistDefinition={_definition.Code}; Version={_definition.Version}; NumberPrefix={_definition.NumberPrefix}");
                DmsMessage.Show($"Definice {_definition.Code} byla uložena.", "DMS", MessageBoxButton.OK, MessageBoxImage.Information);
                _executeTransaction($"CHL00 {_definition.Code}");
                return;
            }

            if (Equals(BtnPrimary.Tag, "SAVE") && _instance is not null)
            {
                SaveEditors();
                var isCreate = string.IsNullOrWhiteSpace(_instance.ChecklistNumber);
                _service.SaveDraft(_instance, _displayName);
                _audit(isCreate ? "AUDIT_CREATE" : "AUDIT", $"Checklist={_instance.ChecklistNumber}; Definition={_instance.DefinitionCode}; Subject={_instance.SubjectReference}; Status={_instance.Status}");
                DmsMessage.Show($"Checklist {_instance.ChecklistNumber} byl uložen.", "DMS", MessageBoxButton.OK, MessageBoxImage.Information);
                _executeTransaction($"CHL03 {_instance.ChecklistNumber}");
                return;
            }

            if (Equals(BtnPrimary.Tag, "REVIEW") && _instance is not null && _definition is not null)
            {
                if (!CanCurrentUserApprove(_definition, _instance, out var reason))
                {
                    DmsMessage.Warning("Schválení checklistu", reason, Window.GetWindow(this));
                    return;
                }

                if (!DmsMessage.Confirm("Schválení checklistu", $"Opravdu schválit checklist {_instance.ChecklistNumber}?", Window.GetWindow(this)))
                    return;

                _instance.Status = ChecklistStatus.Checked;
                _instance.CheckedByPersonId = _currentPersonId;
                _instance.CheckedByDisplayName = _displayName;
                _instance.CheckedAt = DateTimeOffset.Now;
                _service.SaveDraft(_instance, _displayName);
                _audit("CHECKLIST_APPROVED", $"Checklist={_instance.ChecklistNumber}; OldStatus=SubmittedForReview; NewStatus=Checked; ApprovedBy={_displayName}");
                _executeTransaction($"CHL03 {_instance.ChecklistNumber}");
            }
        }
        catch (Exception ex)
        {
            DmsMessage.Show(ex.Message, "DMS", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnSecondary_Click(object sender, RoutedEventArgs e)
    {
        if (_instance is null) return;

        if (Equals(BtnSecondary.Tag, "SUBMIT"))
        {
            try
            {
                SaveEditors();
                _service.SaveDraft(_instance, _displayName);

                if (!DmsMessage.Confirm(
                        "Odeslání checklistu",
                        $"Odeslat checklist {_instance.ChecklistNumber} ke kontrole? Po odeslání jej nebude možné běžně upravovat.",
                        Window.GetWindow(this)))
                    return;

                _instance.Status = ChecklistStatus.SubmittedForReview;
                _instance.SubmittedByPersonId = _currentPersonId;
                _instance.SubmittedByDisplayName = _displayName;
                _instance.SubmittedAt = DateTimeOffset.Now;
                _service.SaveDraft(_instance, _displayName);
                _audit("CHECKLIST_SUBMITTED", $"Checklist={_instance.ChecklistNumber}; OldStatus=InProgress; NewStatus=SubmittedForReview; SubmittedBy={_displayName}");
                _executeTransaction($"CHL03 {_instance.ChecklistNumber}");
            }
            catch (Exception ex)
            {
                DmsMessage.Error("Odeslání checklistu", ex.Message, Window.GetWindow(this));
            }
            return;
        }

        if (!Equals(BtnSecondary.Tag, "RETURN")) return;

        var reason = DmsTextPromptDialog.Show(
            Window.GetWindow(this),
            "Vrácení checklistu",
            "Uveď důvod vrácení checklistu k opravě:");

        if (reason is null) return;

        _instance.Status = ChecklistStatus.ReturnedForCorrection;
        _instance.ReturnReason = reason;
        _instance.ReturnedByPersonId = _currentPersonId;
        _instance.ReturnedByDisplayName = _displayName;
        _instance.ReturnedAt = DateTimeOffset.Now;
        _service.SaveDraft(_instance, _displayName);
        _audit("CHECKLIST_RETURNED", $"Checklist={_instance.ChecklistNumber}; OldStatus=SubmittedForReview; NewStatus=ReturnedForCorrection; ReturnedBy={_displayName}; Reason={reason}");
        _executeTransaction($"CHL03 {_instance.ChecklistNumber}");
    }

    private bool CanCurrentUserApprove(ChecklistDefinition definition, ChecklistInstance instance, out string reason)
    {
        reason = string.Empty;

        if (!definition.RequiresReview)
        {
            reason = "Tento typ checklistu nevyžaduje kontrolu.";
            return false;
        }

        if (!definition.AllowAuthorToApproveOwnChecklist &&
            string.Equals(instance.CreatedByLogin, _windowsLogin, StringComparison.OrdinalIgnoreCase))
        {
            reason = "Autor nemůže schválit vlastní checklist.";
            return false;
        }

        var hasConfiguredRestriction =
            definition.AllowedApprovalRoleCodes.Count > 0 ||
            definition.AllowedApprovalPersonIds.Count > 0 ||
            definition.AllowedApprovalOrganizationUnitIds.Count > 0;

        if (!hasConfiguredRestriction)
            return true;

        if (_currentPersonId.HasValue && definition.AllowedApprovalPersonIds.Contains(_currentPersonId.Value))
            return true;

        if (definition.AllowedApprovalRoleCodes.Any(role =>
            _currentRoles.Any(current => string.Equals(current, role, StringComparison.OrdinalIgnoreCase))))
            return true;

        if (_currentPersonId.HasValue && definition.AllowedApprovalOrganizationUnitIds.Count > 0)
        {
            var person = _masterData.LoadPeople().FirstOrDefault(x => x.PersonId == _currentPersonId.Value);
            if (person is not null && definition.AllowedApprovalOrganizationUnitIds.Contains(person.OrganizationUnitId))
                return true;
        }

        reason = "Nejsi mezi povolenými schvalovateli tohoto typu checklistu.";
        return false;
    }

    private void GridItems_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (GridItems.SelectedItem is GridRow row && !string.IsNullOrWhiteSpace(row.Command))
            _executeTransaction(row.Command);
    }

    private void TxtFilter_TextChanged(object sender, TextChangedEventArgs e) => ApplyGridFilter();
    private void BtnRefresh_Click(object sender, RoutedEventArgs e) => LoadCurrentMode();

    private void ApplyGridFilter()
    {
        var filter = TxtFilter.Text.Trim();
        GridItems.ItemsSource = string.IsNullOrWhiteSpace(filter)
            ? _allRows
            : _allRows.Where(x => x.SearchText.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void ShowMessage(string title, string message)
    {
        TxtTitle.Text = title;
        TxtSubtitle.Text = message;
        GridItems.Visibility = Visibility.Collapsed;
        FormScroll.Visibility = Visibility.Visible;
        FilterBar.Visibility = Visibility.Collapsed;
        FormPanel.Children.Clear();
        FormPanel.Children.Add(CreateInfoCard(title, message));
    }

    private static Border CreateEditableCard(string labelText, FrameworkElement editor)
    {
        var border = new Border
        {
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(12),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6)
        };
        border.SetResourceReference(Border.BackgroundProperty, "DmsPanelBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "DmsBorderBrush");

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var label = new TextBlock
        {
            Text = labelText,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "DmsForegroundBrush");

        Grid.SetColumn(editor, 1);
        grid.Children.Add(label);
        grid.Children.Add(editor);
        border.Child = grid;
        return border;
    }

    private static Border CreateSectionHeader(string title)
    {
        var border = new Border { Margin = new Thickness(0, 14, 0, 8), Padding = new Thickness(0, 0, 0, 6), BorderThickness = new Thickness(0, 0, 0, 1) };
        border.SetResourceReference(Border.BorderBrushProperty, "DmsBorderBrush");
        var text = new TextBlock { Text = title, FontSize = 19, FontWeight = FontWeights.Bold };
        text.SetResourceReference(TextBlock.ForegroundProperty, "DmsForegroundBrush");
        border.Child = text;
        return border;
    }

    private static Border CreateInfoCard(string label, string value)
    {
        var border = new Border { Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(12), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6) };
        border.SetResourceReference(Border.BackgroundProperty, "DmsPanelBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "DmsBorderBrush");
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = value, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap });
        border.Child = panel;
        return border;
    }

    private string Tr(string key, string fallback)
    {
        var value = _translate(key);
        return string.IsNullOrWhiteSpace(value) || string.Equals(value, key, StringComparison.OrdinalIgnoreCase) ? fallback : value;
    }

    private enum ViewMode { Grid, Definitions, DefinitionDetail, EditForm, ReadForm }
    private sealed record FieldEditor(FrameworkElement Element, Action Save);

    private sealed class Choice
    {
        public Choice(Guid id, string displayText)
        {
            Id = id;
            DisplayText = displayText;
        }

        public Guid Id { get; }
        public string DisplayText { get; }
        public override string ToString() => DisplayText;
    }

    private sealed class UnitChoice
    {
        public UnitChoice(UnitDefinition unit, string displayText)
        {
            Unit = unit;
            DisplayText = displayText;
        }

        public UnitDefinition Unit { get; }
        public string DisplayText { get; }
        public override string ToString() => DisplayText;
    }



    private sealed class CatalogChoice
    {
        public CatalogChoice(string code, string displayText)
        {
            Code = code;
            DisplayText = displayText;
        }

        public string Code { get; }
        public string DisplayText { get; }
        public override string ToString() => DisplayText;
    }

    private sealed class DefinitionFieldRow
    {
        public string SectionCode { get; set; } = string.Empty;
        public string SectionTitle { get; set; } = string.Empty;
        public int SectionSortOrder { get; set; }
        public string FieldCode { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public ChecklistFieldType FieldType { get; set; }
        public int FieldSortOrder { get; set; }
        public bool IsRequired { get; set; }
        public bool IsReadOnly { get; set; }
        public string? UnitDimensionCode { get; set; }
        public string? DefaultUnitCode { get; set; }
        public string? SourceBinding { get; set; }
        public string? CatalogCode { get; set; }
        public bool AllowMultipleValues { get; set; }

        public static DefinitionFieldRow From(
            ChecklistSectionDefinition section,
            ChecklistFieldDefinition field) => new()
        {
            SectionCode = section.Code,
            SectionTitle = section.Title,
            SectionSortOrder = section.SortOrder,
            FieldCode = field.Code,
            Label = field.Label,
            FieldType = field.FieldType,
            FieldSortOrder = field.SortOrder,
            IsRequired = field.IsRequired,
            IsReadOnly = field.IsReadOnly,
            UnitDimensionCode = field.UnitDimensionCode,
            DefaultUnitCode = field.DefaultUnitCode,
            SourceBinding = field.SourceBinding,
                CatalogCode = field.CatalogCode,
                AllowMultipleValues = field.AllowMultipleValues
        };
    }

    private sealed class GridRow
    {
        public string Primary { get; init; } = string.Empty;
        public string Secondary { get; init; } = string.Empty;
        public string Tertiary { get; init; } = string.Empty;
        public string Modified { get; init; } = string.Empty;
        public string Command { get; init; } = string.Empty;
        public string SearchText => $"{Primary} {Secondary} {Tertiary} {Modified}";
    }
}
