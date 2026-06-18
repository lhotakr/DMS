using DMS.Desktop.Configuration.Roles;
using System.Collections.ObjectModel;
using System.Windows;

namespace DMS.Desktop.Views.SystemTransactions;

public partial class RoleSelectionWindow : Window
{
    private readonly ObservableCollection<RoleSelectionItem> _items = new();

    public List<string> SelectedRoleCodes { get; private set; } = new();

    public RoleSelectionWindow(
        IEnumerable<DmsRoleDefinition> availableRoles,
        IEnumerable<string> selectedRoles)
    {
        InitializeComponent();

        var selected = new HashSet<string>(
            selectedRoles,
            StringComparer.OrdinalIgnoreCase);

        foreach (var role in availableRoles.OrderBy(x => x.Code))
        {
            _items.Add(new RoleSelectionItem
            {
                Code = role.Code,
                Name = role.Name,
                Description = role.Description,
                IsSelected = selected.Contains(role.Code)
            });
        }

        ListRoles.ItemsSource = _items;
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        SelectedRoleCodes = _items
            .Where(x => x.IsSelected)
            .Select(x => x.Code)
            .OrderBy(x => x)
            .ToList();

        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private sealed class RoleSelectionItem
    {
        public string Code { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public bool IsSelected { get; set; }
    }
}