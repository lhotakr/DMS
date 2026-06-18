using DMS.Desktop.Configuration.Modules;
using DMS.Desktop.UI;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.SystemModules;

public partial class ModuleManagementView : UserControl, IUnsavedChangesGuard
{
    private readonly DmsModuleManagementService _service;
    private readonly ObservableCollection<DmsModuleDefinition> _modules = new();
    private bool _isLoading;

    public bool HasUnsavedChanges => _modules.Any(x => x.State != "Unchanged");

    public ModuleManagementView(string modulesPath)
    {
        InitializeComponent();

        _service = new DmsModuleManagementService(modulesPath);

        _modules.CollectionChanged += Modules_CollectionChanged;
        GridModules.ItemsSource = _modules;

        LoadModules();
    }

    public bool ConfirmNavigationAway()
    {
        if (!HasUnsavedChanges)
        {
            return true;
        }

        return DmsConfirmDialog.ShowQuestion(
            Window.GetWindow(this),
            "SYS13 - Neuložené změny",
            "Ve správě modulů jsou neuložené změny.\n\nChceš opravdu pokračovat bez uložení?");
    }

    private void Modules_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_isLoading || e.NewItems is null)
        {
            return;
        }

        foreach (var module in e.NewItems.OfType<DmsModuleDefinition>())
        {
            module.MarkAdded();
        }
    }

    private void LoadModules()
    {
        _isLoading = true;

        try
        {
            _modules.Clear();

            foreach (var module in _service.LoadAll())
            {
                module.MarkUnchanged();
                _modules.Add(module);
            }

            TxtStatus.Text = $"Načteno modulů: {_modules.Count}";
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void BtnReload_Click(object sender, RoutedEventArgs e)
    {
        if (HasUnsavedChanges)
        {
            var confirmUnsaved = DmsConfirmDialog.ShowQuestion(
                Window.GetWindow(this),
                "SYS13 - Znovu načíst",
                "V tabulce jsou neuložené změny.\n\nChceš je zahodit a znovu načíst dms-modules.json?");

            if (!confirmUnsaved)
            {
                return;
            }
        }
        else
        {
            var confirm = DmsConfirmDialog.ShowQuestion(
                Window.GetWindow(this),
                "SYS13 - Znovu načíst",
                "Chceš znovu načíst dms-modules.json?");

            if (!confirm)
            {
                return;
            }
        }

        LoadModules();
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var validationMessage = ValidateModules();

        if (!string.IsNullOrWhiteSpace(validationMessage))
        {
            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                "SYS13 - Kontrola modulů",
                validationMessage);

            return;
        }

        try
        {
            _service.SaveAll(_modules.Where(x => x.State != "Deleted"));

            TxtStatus.Text = $"Uloženo modulů: {_modules.Count(x => x.State != "Deleted")} | {DateTime.Now:dd.MM.yyyy HH:mm:ss}";

            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                "SYS13",
                "Moduly byly uloženy.");

            LoadModules();
        }
        catch (Exception ex)
        {
            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                "SYS13 - Chyba",
                $"Uložení modulů selhalo:\n\n{ex.Message}");
        }
    }

    private void BtnMarkDeleted_Click(object sender, RoutedEventArgs e)
    {
        if (GridModules.SelectedItem is not DmsModuleDefinition module)
        {
            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                "SYS13 - Mazání modulu",
                "Vyber modul, který chceš označit ke smazání.");

            return;
        }

        module.MarkDeleted();
        GridModules.Items.Refresh();

        TxtStatus.Text = $"Modul {module.Code} označen ke smazání. Změnu potvrď tlačítkem Uložit.";
    }

    private void BtnRestoreRow_Click(object sender, RoutedEventArgs e)
    {
        if (GridModules.SelectedItem is not DmsModuleDefinition module)
        {
            return;
        }

        if (module.State == "Added")
        {
            _modules.Remove(module);
        }
        else
        {
            module.MarkModified();
        }

        GridModules.Items.Refresh();
        TxtStatus.Text = "Označení řádku bylo vráceno.";
    }

    private string? ValidateModules()
    {
        var usedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var module in _modules.Where(x => x.State != "Deleted"))
        {
            if (string.IsNullOrWhiteSpace(module.Code) &&
                string.IsNullOrWhiteSpace(module.Name) &&
                string.IsNullOrWhiteSpace(module.Description))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(module.Code))
            {
                return "Modul nesmí mít prázdný kód.";
            }

            if (module.Code.Any(char.IsWhiteSpace))
            {
                return $"Kód modulu nesmí obsahovat mezery: {module.Code}";
            }

            if (!usedCodes.Add(module.Code.Trim()))
            {
                return $"Duplicitní kód modulu: {module.Code}";
            }

            if (string.IsNullOrWhiteSpace(module.Name))
            {
                return $"Modul {module.Code} musí mít vyplněný název.";
            }

            if (!usedNames.Add(module.Name.Trim()))
            {
                return $"Duplicitní název modulu: {module.Name}";
            }
        }

        return null;
    }
}