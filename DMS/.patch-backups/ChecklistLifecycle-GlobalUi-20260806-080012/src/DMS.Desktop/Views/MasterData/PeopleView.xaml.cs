using DMS.Core.Domain.Organization;
using DMS.Core.Domain.People;
using DMS.Desktop.Logging;
using DMS.Desktop.Services.MasterData;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.MasterData;

public sealed class OrganizationChoice
{
    public Guid Id { get; init; }
    public string DisplayText { get; init; } = string.Empty;
}

public sealed class PersonTypeChoice
{
    public DmsPersonType Value { get; init; }
    public string DisplayText { get; init; } = string.Empty;
}

public sealed class PersonGridRow
{
    public required DmsPerson Person { get; init; }
    public string PersonnelNumber => Person.PersonnelNumber;
    public string FirstName => Person.FirstName;
    public string LastName => Person.LastName;
    public string OrganizationUnitName { get; init; } = string.Empty;
    public string PersonTypeText { get; init; } = string.Empty;
}

public partial class PeopleView : UserControl
{
    private readonly DmsMasterDataService _service;
    private readonly DmsLogger _logger;
    private readonly string _user;
    private readonly Func<string, string> _translate;

    private List<DmsPerson> _people = new();
    private List<DmsOrganizationUnit> _units = new();
    private DmsPerson? _selected;

    public PeopleView(
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
        LoadPersonTypes();
        LoadData();
    }

    private string T(string key) => _translate(key);

    private void ApplyLocalization()
    {
        TxtFilterLabel.Text = T("SYS01.MasterData.Filter");
        ChkShowInactive.Content = T("SYS01.MasterData.ShowInactive");
        ColPersonnelNumber.Header = T("SYS01.People.PersonnelNumber");
        ColLastName.Header = T("SYS01.People.LastName");
        ColFirstName.Header = T("SYS01.People.FirstName");
        ColOrganizationUnit.Header = T("SYS01.People.OrganizationUnit");
        ColPersonType.Header = T("SYS01.People.PersonType");
        ColActive.Header = T("SYS01.MasterData.Active");
        TxtPersonnelNumberLabel.Text = T("SYS01.People.PersonnelNumber");
        TxtFirstNameLabel.Text = T("SYS01.People.FirstName");
        TxtLastNameLabel.Text = T("SYS01.People.LastName");
        TxtOrganizationUnitLabel.Text = T("SYS01.People.OrganizationUnit");
        TxtPersonTypeLabel.Text = T("SYS01.People.PersonType");
        ChkActive.Content = T("SYS01.MasterData.Active");
        BtnNew.Content = T("SYS01.People.New");
        BtnSave.Content = T("SYS01.MasterData.Save");
        BtnToggle.Content = T("SYS01.MasterData.ToggleActive");
    }

    private void LoadPersonTypes()
    {
        CmbPersonType.ItemsSource = new[]
        {
            new PersonTypeChoice
            {
                Value = DmsPersonType.InternalEmployee,
                DisplayText = T("SYS01.People.Type.InternalEmployee")
            },
            new PersonTypeChoice
            {
                Value = DmsPersonType.ParentCompanyGermanyEmployee,
                DisplayText = T("SYS01.People.Type.ParentCompanyGermanyEmployee")
            }
        };
    }

    private string PersonTypeText(DmsPersonType value) => value switch
    {
        DmsPersonType.InternalEmployee => T("SYS01.People.Type.InternalEmployee"),
        DmsPersonType.ParentCompanyGermanyEmployee => T("SYS01.People.Type.ParentCompanyGermanyEmployee"),
        _ => value.ToString()
    };

    private void LoadData()
    {
        _people = _service.LoadPeople();
        _units = _service.LoadOrganizationUnits();

        CmbOrganizationUnit.ItemsSource = _units
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new OrganizationChoice
            {
                Id = x.OrganizationUnitId,
                DisplayText = string.IsNullOrWhiteSpace(x.Code)
                    ? x.Name
                    : $"{x.Code} — {x.Name}"
            })
            .ToList();

        ApplyFilter();
        TxtStatus.Text = $"{T("SYS01.MasterData.File")}: {_service.PeoplePath}";
    }

    private void ApplyFilter()
    {
        var query = TxtFilter.Text?.Trim() ?? string.Empty;

        GridPeople.ItemsSource = _people
            .Where(x => ChkShowInactive.IsChecked == true || x.IsActive)
            .Where(x => string.IsNullOrWhiteSpace(query)
                        || x.PersonnelNumber.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || x.FirstName.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || x.LastName.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.LastName)
            .ThenBy(x => x.FirstName)
            .Select(x => new PersonGridRow
            {
                Person = x,
                OrganizationUnitName = _units.FirstOrDefault(u => u.OrganizationUnitId == x.OrganizationUnitId)?.Name ?? T("SYS01.MasterData.Unknown"),
                PersonTypeText = PersonTypeText(x.PersonType)
            })
            .ToList();
    }

    private void FilterChanged(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
        {
            ApplyFilter();
        }
    }

    private void FilterChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded)
        {
            ApplyFilter();
        }
    }

    private void GridPeople_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GridPeople.SelectedItem is PersonGridRow row)
        {
            _selected = row.Person;
            ShowSelected();
        }
    }

    private void ShowSelected()
    {
        if (_selected is null)
        {
            return;
        }

        TxtPersonnelNumber.Text = _selected.PersonnelNumber;
        TxtFirstName.Text = _selected.FirstName;
        TxtLastName.Text = _selected.LastName;
        CmbOrganizationUnit.SelectedItem = (CmbOrganizationUnit.ItemsSource as IEnumerable<OrganizationChoice>)?
            .FirstOrDefault(x => x.Id == _selected.OrganizationUnitId);
        CmbPersonType.SelectedItem = (CmbPersonType.ItemsSource as IEnumerable<PersonTypeChoice>)?
            .FirstOrDefault(x => x.Value == _selected.PersonType);
        ChkActive.IsChecked = _selected.IsActive;
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        _selected = new DmsPerson
        {
            IsActive = true,
            PersonType = DmsPersonType.InternalEmployee
        };

        ShowSelected();
        TxtPersonnelNumber.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
        {
            New_Click(sender, e);
        }

        if (_selected is null)
        {
            return;
        }

        var existing = _people.FirstOrDefault(x => x.PersonId == _selected.PersonId);
        var before = existing is null
            ? null
            : new DmsPerson
            {
                PersonId = existing.PersonId,
                PersonnelNumber = existing.PersonnelNumber,
                FirstName = existing.FirstName,
                LastName = existing.LastName,
                OrganizationUnitId = existing.OrganizationUnitId,
                PersonType = existing.PersonType,
                IsActive = existing.IsActive
            };

        _selected.PersonnelNumber = TxtPersonnelNumber.Text.Trim();
        _selected.FirstName = TxtFirstName.Text.Trim();
        _selected.LastName = TxtLastName.Text.Trim();
        _selected.OrganizationUnitId = (CmbOrganizationUnit.SelectedItem as OrganizationChoice)?.Id ?? Guid.Empty;
        _selected.PersonType = (CmbPersonType.SelectedItem as PersonTypeChoice)?.Value ?? DmsPersonType.InternalEmployee;
        _selected.IsActive = ChkActive.IsChecked == true;

        if (string.IsNullOrWhiteSpace(_selected.PersonnelNumber)
            || string.IsNullOrWhiteSpace(_selected.FirstName)
            || string.IsNullOrWhiteSpace(_selected.LastName)
            || _selected.OrganizationUnitId == Guid.Empty)
        {
            MessageBox.Show(
                T("SYS01.People.Validation.Required"),
                T("SYS01.People.Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (existing is null)
        {
            _people.Add(_selected);
            _logger.AuditCreated(
                "SYS01",
                "Person",
                _selected.PersonId.ToString(),
                _user,
                $"PersonnelNumber={_selected.PersonnelNumber}; Name={_selected.DisplayName}; OrganizationUnitId={_selected.OrganizationUnitId}; Type={_selected.PersonType}");
        }
        else if (before is not null)
        {
            LogChanges(before, _selected);
        }

        try
        {
            _service.SavePeople(_people, _units);
            LoadData();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, T("SYS01.People.SaveErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
        {
            return;
        }

        _selected.IsActive = !_selected.IsActive;
        ChkActive.IsChecked = _selected.IsActive;
        Save_Click(sender, e);
    }

    private void LogChanges(DmsPerson oldValue, DmsPerson current)
    {
        void Audit(string field, object? oldFieldValue, object? newFieldValue)
        {
            if (!Equals(oldFieldValue, newFieldValue))
            {
                _logger.AuditChange(
                    "SYS01",
                    "Person",
                    current.PersonId.ToString(),
                    field,
                    oldFieldValue?.ToString(),
                    newFieldValue?.ToString(),
                    _user);
            }
        }

        Audit("PersonnelNumber", oldValue.PersonnelNumber, current.PersonnelNumber);
        Audit("FirstName", oldValue.FirstName, current.FirstName);
        Audit("LastName", oldValue.LastName, current.LastName);
        Audit("OrganizationUnitId", oldValue.OrganizationUnitId, current.OrganizationUnitId);
        Audit("PersonType", oldValue.PersonType, current.PersonType);
        Audit("IsActive", oldValue.IsActive, current.IsActive);
    }
}
