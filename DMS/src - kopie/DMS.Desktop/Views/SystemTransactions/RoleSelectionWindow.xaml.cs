using DMS.Desktop.Configuration.Roles;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace DMS.Desktop.Views.SystemTransactions;

public partial class RoleSelectionWindow : Window
{
    private readonly ObservableCollection<RoleSelectionItem> _items = new();
    private readonly Func<string, string>? _translate;

    public List<string> SelectedRoleCodes { get; private set; } = new();

    public RoleSelectionWindow(
        IEnumerable<DmsRoleDefinition> availableRoles,
        IEnumerable<string> selectedRoles)
        : this(availableRoles, selectedRoles, translate: null)
    {
    }

    public RoleSelectionWindow(
        IEnumerable<DmsRoleDefinition> availableRoles,
        IEnumerable<string> selectedRoles,
        Func<string, string>? translate)
    {
        InitializeComponent();

        _translate = translate;

        ApplyLocalization();

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

    private void ApplyLocalization()
    {
        Title = T("SYS11.RoleSelection.WindowTitle", "Select transaction roles");

        TrySetTextBlock("TxtTitle", T("SYS11.RoleSelection.Title", "Transaction roles"));
        TrySetTextBlock("TxtSubtitle", T("SYS11.RoleSelection.Subtitle", "Select roles that are allowed to execute the transaction."));
        TrySetContent("BtnOk", T("Common.OK", "OK"));
        TrySetContent("BtnCancel", T("Common.Cancel", "Cancel"));

        if ((object)ListRoles is DataGrid grid)
        {
            foreach (var column in grid.Columns)
            {
                var bindingPath = GetColumnBindingPath(column);
                var header = bindingPath switch
                {
                    nameof(RoleSelectionItem.IsSelected) => T("SYS11.RoleSelection.Column.Selected", "Selected"),
                    nameof(RoleSelectionItem.Code) => T("SYS11.RoleSelection.Column.Code", "Code"),
                    nameof(RoleSelectionItem.Name) => T("SYS11.RoleSelection.Column.Name", "Name"),
                    nameof(RoleSelectionItem.Description) => T("SYS11.RoleSelection.Column.Description", "Description"),
                    _ => null
                };

                if (!string.IsNullOrWhiteSpace(header))
                {
                    column.Header = header;
                }
            }
        }
    }

    private static string? GetColumnBindingPath(DataGridColumn column)
    {
        if (column.SortMemberPath is { Length: > 0 } sortMemberPath)
        {
            return sortMemberPath;
        }

        if (column is DataGridBoundColumn boundColumn &&
            boundColumn.Binding is Binding binding)
        {
            return binding.Path?.Path;
        }

        return null;
    }

    private void TrySetTextBlock(string name, string value)
    {
        if (FindName(name) is TextBlock textBlock)
        {
            textBlock.Text = value;
        }
    }

    private void TrySetContent(string name, string value)
    {
        if (FindName(name) is ContentControl contentControl)
        {
            contentControl.Content = value;
        }
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

    private string T(string key, string fallback)
    {
        var translated = _translate?.Invoke(key);

        return IsMissingTranslation(translated, key)
            ? fallback
            : translated!;
    }

    private static bool IsMissingTranslation(string? value, string key)
    {
        return string.IsNullOrWhiteSpace(value)
               || string.Equals(value, key, StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, $"[[{key}]]", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class RoleSelectionItem
    {
        public string Code { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public bool IsSelected { get; set; }
    }
}
