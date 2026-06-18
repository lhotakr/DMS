using DMS.Core.Common.Editing;
using DMS.Core.Quality;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.Quality;

public partial class QualitySettingsView : UserControl
{
    private readonly QualityStoragePaths _paths;
    private readonly JsonQualityRepository _repository;

    private ObservableCollection<EditableRow<QualityCustomer>> _customers = new();
    private ObservableCollection<EditableRow<QualityLookupItem>> _colorTypes = new();
    private ObservableCollection<EditableRow<QualityLookupItem>> _glassTreatments = new();
    private ObservableCollection<EditableRow<QualityLookupItem>> _qualityClasses = new();

    public QualitySettingsView()
    {
        InitializeComponent();

        const string basePath = @"Z:\SAP\DMS-db\DEV";

        _paths = new QualityStoragePaths(basePath);
        _paths.EnsureDirectories();

        _repository = new JsonQualityRepository(_paths);

        EnsureDefaultLookupFiles();

        LoadData();
    }

    private void LoadData()
    {
        _customers = new ObservableCollection<EditableRow<QualityCustomer>>(
            _repository
                .LoadCustomers()
                .OrderBy(item => item.Name)
                .Select(item => new EditableRow<QualityCustomer>(
                    CloneCustomer(item))));

        _colorTypes = LoadLookupRows(
            _repository.LoadColorTypes());

        _glassTreatments = LoadLookupRows(
            _repository.LoadGlassTreatments());

        _qualityClasses = LoadLookupRows(
            _repository.LoadQualityClasses());

        GridCustomers.ItemsSource = _customers;
        GridColorTypes.ItemsSource = _colorTypes;
        GridGlassTreatments.ItemsSource = _glassTreatments;
        GridQualityClasses.ItemsSource = _qualityClasses;

        TxtPaths.Text =
            $"Quality base path: {_paths.BasePath}\n" +
            $"Quality data path: {_paths.QualityPath}\n\n" +
            $"Zákazníci: {_paths.QualityCustomersFilePath}\n" +
            $"Typy barev: {_paths.QualityColorTypesFilePath}\n" +
            $"Úpravy skla: {_paths.QualityGlassTreatmentsFilePath}\n" +
            $"Třídy kvality: {_paths.QualityClassesFilePath}";

        TxtStatus.Text = "QASET připraven.";
    }

    private static ObservableCollection<EditableRow<QualityLookupItem>> LoadLookupRows(
        IReadOnlyList<QualityLookupItem> items)
    {
        return new ObservableCollection<EditableRow<QualityLookupItem>>(
            items
                .OrderBy(item => item.SortOrder)
                .ThenBy(item => item.Name)
                .Select(item => new EditableRow<QualityLookupItem>(
                    CloneLookup(item))));
    }

    // ============================================================
    // ADD
    // ============================================================

    private void BtnAddCustomer_Click(
        object sender,
        RoutedEventArgs e)
    {
        var row = new EditableRow<QualityCustomer>(
            new QualityCustomer
            {
                Code = string.Empty,
                Name = "Nový zákazník",
                IsActive = true,
                IsLoreal = false,
                SourceId = 0
            },
            EditableRowState.Added);

        _customers.Add(row);

        GridCustomers.SelectedItem = row;
        GridCustomers.ScrollIntoView(row);

        TxtStatus.Text = "Přidán nový zákazník.";
    }

    private void BtnAddColorType_Click(
        object sender,
        RoutedEventArgs e)
    {
        AddLookupRow(
            _colorTypes,
            GridColorTypes,
            "NEW_COLOR",
            "Nový typ barvy");

        TxtStatus.Text = "Přidán nový typ barvy.";
    }

    private void BtnAddGlassTreatment_Click(
        object sender,
        RoutedEventArgs e)
    {
        AddLookupRow(
            _glassTreatments,
            GridGlassTreatments,
            "NEW_GLASS",
            "Nová úprava skla");

        TxtStatus.Text = "Přidána nová úprava skla.";
    }

    private void BtnAddQualityClass_Click(
        object sender,
        RoutedEventArgs e)
    {
        AddLookupRow(
            _qualityClasses,
            GridQualityClasses,
            "NEW_CLASS",
            "Nová třída kvality");

        TxtStatus.Text = "Přidána nová třída kvality.";
    }

    private static void AddLookupRow(
        ObservableCollection<EditableRow<QualityLookupItem>> collection,
        DataGrid grid,
        string code,
        string name)
    {
        var sortOrder = collection.Count == 0
            ? 10
            : collection.Max(row => row.Item.SortOrder) + 10;

        var row = new EditableRow<QualityLookupItem>(
            new QualityLookupItem
            {
                Code = code,
                Name = name,
                IsActive = true,
                SortOrder = sortOrder,
                Notes = string.Empty
            },
            EditableRowState.Added);

        collection.Add(row);

        grid.SelectedItem = row;
        grid.ScrollIntoView(row);
    }

    // ============================================================
    // DELETE
    // ============================================================

    private void BtnDeleteCustomer_Click(
        object sender,
        RoutedEventArgs e)
    {
        DeleteSelectedRows(GridCustomers, _customers);
        TxtStatus.Text = "Vybrané zákaznické záznamy byly označeny ke smazání.";
    }

    private void BtnDeleteColorType_Click(
        object sender,
        RoutedEventArgs e)
    {
        DeleteSelectedRows(GridColorTypes, _colorTypes);
        TxtStatus.Text = "Vybrané typy barev byly označeny ke smazání.";
    }

    private void BtnDeleteGlassTreatment_Click(
        object sender,
        RoutedEventArgs e)
    {
        DeleteSelectedRows(GridGlassTreatments, _glassTreatments);
        TxtStatus.Text = "Vybrané úpravy skla byly označeny ke smazání.";
    }

    private void BtnDeleteQualityClass_Click(
        object sender,
        RoutedEventArgs e)
    {
        DeleteSelectedRows(GridQualityClasses, _qualityClasses);
        TxtStatus.Text = "Vybrané třídy kvality byly označeny ke smazání.";
    }

    private static void DeleteSelectedRows<T>(
        DataGrid grid,
        ObservableCollection<EditableRow<T>> collection)
    {
        var selectedRows = grid.SelectedItems
            .OfType<EditableRow<T>>()
            .ToList();

        foreach (var row in selectedRows)
        {
            if (row.State == EditableRowState.Added)
            {
                collection.Remove(row);
            }
            else
            {
                row.State = EditableRowState.Deleted;
            }
        }
    }

    // ============================================================
    // SAVE
    // ============================================================

    private void BtnSaveCustomers_Click(
        object sender,
        RoutedEventArgs e)
    {
        CommitGrid(GridCustomers);

        var items = _customers
            .Where(row => row.State != EditableRowState.Deleted)
            .Select(row => row.Item)
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .OrderBy(item => item.Name)
            .ToList();

        _repository.SaveCustomers(items);

        TxtStatus.Text = $"Zákazníci uloženi. Počet: {items.Count:N0}";

        LoadData();
    }

    private void BtnSaveColorTypes_Click(
        object sender,
        RoutedEventArgs e)
    {
        SaveLookupRows(
            GridColorTypes,
            _colorTypes,
            _repository.SaveColorTypes,
            "Typy barev");
    }

    private void BtnSaveGlassTreatments_Click(
        object sender,
        RoutedEventArgs e)
    {
        SaveLookupRows(
            GridGlassTreatments,
            _glassTreatments,
            _repository.SaveGlassTreatments,
            "Úpravy skla");
    }

    private void BtnSaveQualityClasses_Click(
        object sender,
        RoutedEventArgs e)
    {
        SaveLookupRows(
            GridQualityClasses,
            _qualityClasses,
            _repository.SaveQualityClasses,
            "Třídy kvality");
    }

    private void SaveLookupRows(
        DataGrid grid,
        ObservableCollection<EditableRow<QualityLookupItem>> collection,
        Action<IEnumerable<QualityLookupItem>> saveAction,
        string displayName)
    {
        CommitGrid(grid);

        var items = collection
            .Where(row => row.State != EditableRowState.Deleted)
            .Select(row => row.Item)
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.Code) ||
                !string.IsNullOrWhiteSpace(item.Name))
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Name)
            .ToList();

        saveAction(items);

        TxtStatus.Text = $"{displayName} uloženy. Počet: {items.Count:N0}";

        LoadData();
    }

    // ============================================================
    // EDIT TRACKING
    // ============================================================

    private void GridCustomers_CellEditEnding(
        object sender,
        DataGridCellEditEndingEventArgs e)
    {
        MarkRowModified(e.Row.Item);
    }

    private void GridLookup_CellEditEnding(
        object sender,
        DataGridCellEditEndingEventArgs e)
    {
        MarkRowModified(e.Row.Item);
    }

    private static void MarkRowModified(object? rowObject)
    {
        switch (rowObject)
        {
            case EditableRow<QualityCustomer> customerRow:
                customerRow.MarkModified();
                break;

            case EditableRow<QualityLookupItem> lookupRow:
                lookupRow.MarkModified();
                break;
        }
    }

    // ============================================================
    // RELOAD
    // ============================================================

    private void BtnReload_Click(
        object sender,
        RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Obnovit data z JSON souborů?\n\nNeuložené změny budou zahozeny.",
            "QASET",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        LoadData();
    }

    // ============================================================
    // DEFAULT DATA
    // ============================================================

    private void EnsureDefaultLookupFiles()
    {
        if (!_repository.LoadColorTypes().Any())
        {
            _repository.SaveColorTypes(new[]
            {
                new QualityLookupItem
                {
                    Code = "UV",
                    Name = "UV",
                    IsActive = true,
                    SortOrder = 10
                },
                new QualityLookupItem
                {
                    Code = "UV_LED",
                    Name = "UV LED",
                    IsActive = true,
                    SortOrder = 20
                },
                new QualityLookupItem
                {
                    Code = "KERAMIKA",
                    Name = "Keramika",
                    IsActive = true,
                    SortOrder = 30
                },
                new QualityLookupItem
                {
                    Code = "PERBUSEAL",
                    Name = "Perbuseal",
                    IsActive = true,
                    SortOrder = 40
                },
                new QualityLookupItem
                {
                    Code = "HORKA_RAZBA",
                    Name = "Horká ražba",
                    IsActive = true,
                    SortOrder = 50
                }
            });
        }

        if (!_repository.LoadGlassTreatments().Any())
        {
            _repository.SaveGlassTreatments(new[]
            {
                new QualityLookupItem
                {
                    Code = "CIRE",
                    Name = "Čiré",
                    IsActive = true,
                    SortOrder = 10
                },
                new QualityLookupItem
                {
                    Code = "STRIKANE",
                    Name = "Stříkané",
                    IsActive = true,
                    SortOrder = 20
                },
                new QualityLookupItem
                {
                    Code = "OPAL",
                    Name = "Opálové",
                    IsActive = true,
                    SortOrder = 30
                },
                new QualityLookupItem
                {
                    Code = "MAT",
                    Name = "Matované",
                    IsActive = true,
                    SortOrder = 40
                }
            });
        }

        if (!_repository.LoadQualityClasses().Any())
        {
            _repository.SaveQualityClasses(new[]
            {
                new QualityLookupItem
                {
                    Code = "A",
                    Name = "A",
                    IsActive = true,
                    SortOrder = 10
                },
                new QualityLookupItem
                {
                    Code = "B",
                    Name = "B",
                    IsActive = true,
                    SortOrder = 20
                },
                new QualityLookupItem
                {
                    Code = "C",
                    Name = "C",
                    IsActive = true,
                    SortOrder = 30
                }
            });
        }
    }

    // ============================================================
    // HELPERS
    // ============================================================

    private static void CommitGrid(DataGrid grid)
    {
        grid.CommitEdit(DataGridEditingUnit.Cell, true);
        grid.CommitEdit(DataGridEditingUnit.Row, true);
    }

    private static QualityCustomer CloneCustomer(
        QualityCustomer source)
    {
        return new QualityCustomer
        {
            Code = source.Code,
            Name = source.Name,
            IsActive = source.IsActive,
            IsLoreal = source.IsLoreal,
            SourceId = source.SourceId
        };
    }

    private static QualityLookupItem CloneLookup(
        QualityLookupItem source)
    {
        return new QualityLookupItem
        {
            Code = source.Code,
            Name = source.Name,
            IsActive = source.IsActive,
            SortOrder = source.SortOrder,
            Notes = source.Notes
        };
    }
}