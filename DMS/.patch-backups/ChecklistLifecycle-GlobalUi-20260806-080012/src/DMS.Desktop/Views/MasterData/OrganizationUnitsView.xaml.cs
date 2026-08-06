using DMS.Core.Domain.Organization;
using DMS.Desktop.Logging;
using DMS.Desktop.Services.MasterData;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.MasterData;

public partial class OrganizationUnitsView : UserControl
{
    private readonly DmsMasterDataService _service;
    private readonly DmsLogger _logger;
    private readonly string _user;
    private readonly Func<string, string> _translate;

    private List<DmsOrganizationUnit> _items = new();
    private DmsOrganizationUnit? _selected;

    public OrganizationUnitsView(
        DmsMasterDataService service,
        DmsLogger logger,
        string user,
        Func<string, string>? translate = null)
    {
        InitializeComponent();
        _service = service;
        _logger = logger;
        _user = user;
        _translate = translate ?? (key => key);

        ApplyLocalization();
        LoadData();
    }

    private string T(string key) => _translate(key);

    private void ApplyLocalization()
    {
        TxtTreeTitle.Text = T("SYS01.Organization.TreeTitle");
        ChkShowInactive.Content = T("SYS01.MasterData.ShowInactive");
        TxtDetailTitle.Text = T("SYS01.Organization.DetailTitle");
        TxtCodeLabel.Text = T("SYS01.Organization.Code");
        TxtNameLabel.Text = T("SYS01.Organization.Name");
        TxtExternalCodeLabel.Text = T("SYS01.Organization.ExternalCode");
        TxtSortOrderLabel.Text = T("SYS01.Organization.SortOrder");
        TxtDescriptionLabel.Text = T("SYS01.Organization.Description");
        ChkActive.Content = T("SYS01.MasterData.Active");
        BtnAddRoot.Content = T("SYS01.Organization.AddRoot");
        BtnAddChild.Content = T("SYS01.Organization.AddChild");
        BtnSave.Content = T("SYS01.MasterData.Save");
        BtnToggleActive.Content = T("SYS01.MasterData.ToggleActive");
    }

    private void LoadData(Guid? selectId = null)
    {
        _items = _service.LoadOrganizationUnits();
        var source = ChkShowInactive.IsChecked == true
            ? _items
            : _items.Where(x => x.IsActive).ToList();

        TreeUnits.ItemsSource = BuildTree(source);
        TxtStatus.Text =
            $"{T("SYS01.MasterData.File")}: {_service.OrganizationUnitsPath}\n" +
            $"{T("SYS01.Organization.UnitCount")}: {_items.Count}";
    }

    private static ObservableCollection<OrganizationTreeItem> BuildTree(List<DmsOrganizationUnit> source)
    {
        var map = source.ToDictionary(
            x => x.OrganizationUnitId,
            x => new OrganizationTreeItem { Unit = x });

        var roots = new List<OrganizationTreeItem>();

        foreach (var item in source.OrderBy(x => x.SortOrder).ThenBy(x => x.Name))
        {
            if (item.ParentOrganizationUnitId.HasValue
                && map.TryGetValue(item.ParentOrganizationUnitId.Value, out var parent))
            {
                parent.Children.Add(map[item.OrganizationUnitId]);
            }
            else
            {
                roots.Add(map[item.OrganizationUnitId]);
            }
        }

        return new ObservableCollection<OrganizationTreeItem>(roots);
    }

    private void TreeUnits_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is OrganizationTreeItem node)
        {
            _selected = node.Unit;
            ShowSelected();
        }
    }

    private void ShowSelected()
    {
        if (_selected is null)
        {
            return;
        }

        TxtCode.Text = _selected.Code;
        TxtName.Text = _selected.Name;
        TxtExternalCode.Text = _selected.ExternalCode;
        TxtSortOrder.Text = _selected.SortOrder.ToString();
        TxtDescription.Text = _selected.Description;
        ChkActive.IsChecked = _selected.IsActive;
    }

    private void AddRoot_Click(object sender, RoutedEventArgs e) => BeginNew(null);

    private void AddChild_Click(object sender, RoutedEventArgs e) => BeginNew(_selected?.OrganizationUnitId);

    private void BeginNew(Guid? parentId)
    {
        _selected = new DmsOrganizationUnit
        {
            ParentOrganizationUnitId = parentId,
            SortOrder = (_items.Count + 1) * 10,
            IsActive = true
        };

        ShowSelected();
        TxtCode.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
        {
            BeginNew(null);
        }

        if (_selected is null)
        {
            return;
        }

        var existing = _items.FirstOrDefault(x => x.OrganizationUnitId == _selected.OrganizationUnitId);
        var before = existing is null
            ? null
            : new DmsOrganizationUnit
            {
                OrganizationUnitId = existing.OrganizationUnitId,
                ParentOrganizationUnitId = existing.ParentOrganizationUnitId,
                Code = existing.Code,
                Name = existing.Name,
                ExternalCode = existing.ExternalCode,
                Description = existing.Description,
                SortOrder = existing.SortOrder,
                IsActive = existing.IsActive
            };

        _selected.Code = TxtCode.Text.Trim().ToUpperInvariant();
        _selected.Name = TxtName.Text.Trim();
        _selected.ExternalCode = TxtExternalCode.Text.Trim();
        _selected.Description = TxtDescription.Text.Trim();
        _selected.IsActive = ChkActive.IsChecked == true;

        if (!int.TryParse(TxtSortOrder.Text, out var sortOrder))
        {
            sortOrder = 0;
        }

        _selected.SortOrder = sortOrder;

        if (string.IsNullOrWhiteSpace(_selected.Code) || string.IsNullOrWhiteSpace(_selected.Name))
        {
            MessageBox.Show(
                T("SYS01.Organization.Validation.Required"),
                T("SYS01.Organization.Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (existing is null)
        {
            _items.Add(_selected);
            _logger.AuditCreated(
                "SYS01",
                "OrganizationUnit",
                _selected.OrganizationUnitId.ToString(),
                _user,
                $"Code={_selected.Code}; Name={_selected.Name}; Parent={_selected.ParentOrganizationUnitId}");
        }
        else if (before is not null)
        {
            LogChanges(before, _selected);
        }

        try
        {
            _service.SaveOrganizationUnits(_items);
            LoadData(_selected.OrganizationUnitId);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, T("SYS01.Organization.SaveErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ToggleActive_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
        {
            return;
        }

        _selected.IsActive = !_selected.IsActive;
        ChkActive.IsChecked = _selected.IsActive;
        Save_Click(sender, e);
    }

    private void FilterChanged(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
        {
            LoadData(_selected?.OrganizationUnitId);
        }
    }

    private void LogChanges(DmsOrganizationUnit oldValue, DmsOrganizationUnit current)
    {
        void Audit(string field, object? oldFieldValue, object? newFieldValue)
        {
            if (!Equals(oldFieldValue, newFieldValue))
            {
                _logger.AuditChange(
                    "SYS01",
                    "OrganizationUnit",
                    current.OrganizationUnitId.ToString(),
                    field,
                    oldFieldValue?.ToString(),
                    newFieldValue?.ToString(),
                    _user);
            }
        }

        Audit("Code", oldValue.Code, current.Code);
        Audit("Name", oldValue.Name, current.Name);
        Audit("ExternalCode", oldValue.ExternalCode, current.ExternalCode);
        Audit("Description", oldValue.Description, current.Description);
        Audit("SortOrder", oldValue.SortOrder, current.SortOrder);
        Audit("IsActive", oldValue.IsActive, current.IsActive);
        Audit("ParentOrganizationUnitId", oldValue.ParentOrganizationUnitId, current.ParentOrganizationUnitId);
    }
}
