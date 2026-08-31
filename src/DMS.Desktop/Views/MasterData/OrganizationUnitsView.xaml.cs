using DMS.Desktop.UI;
using DMS.Core.Domain.Organization;
using DMS.Desktop.Logging;
using DMS.Desktop.Services.MasterData;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DMS.Desktop.Views.MasterData;

public partial class OrganizationUnitsView : UserControl
{
    private readonly DmsMasterDataService _service;
    private readonly DmsLogger _logger;
    private readonly string _user;
    private readonly Func<string, string> _translate;

    private List<DmsOrganizationUnit> _items = new();
    private DmsOrganizationUnit? _selected;

    private Point _dragStartPoint;
    private OrganizationTreeItem? _draggedNode;
    private TreeViewItem? _highlightedDropTarget;
    private string _baseStatusText = string.Empty;

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
        TxtDragDropHint.Text = T("SYS01.Organization.DragDrop.Hint");
    }

    private string TF(string key, params object?[] args)
    {
        var format = T(key);

        try
        {
            return string.Format(format, args);
        }
        catch (FormatException)
        {
            return format;
        }
    }

    private void LoadData(Guid? selectId = null)
    {
        _items = _service.LoadOrganizationUnits();

        var source = ChkShowInactive.IsChecked == true
            ? _items
            : _items.Where(x => x.IsActive).ToList();

        var tree =
            BuildTree(
                source,
                selectId);

        TreeUnits.ItemsSource = tree;

        _baseStatusText =
            $"{T("SYS01.MasterData.File")}: {_service.OrganizationUnitsPath}\n" +
            $"{T("SYS01.Organization.UnitCount")}: {_items.Count}";

        TxtStatus.Text = _baseStatusText;

        if (selectId.HasValue)
        {
            _selected =
                _items.FirstOrDefault(unit =>
                    unit.OrganizationUnitId ==
                    selectId.Value);

            ShowSelected();
        }
    }

    private static ObservableCollection<OrganizationTreeItem> BuildTree(
        List<DmsOrganizationUnit> source,
        Guid? selectId)
    {
        var map = source.ToDictionary(
            unit => unit.OrganizationUnitId,
            unit => new OrganizationTreeItem
            {
                Unit = unit
            });

        var roots =
            new List<OrganizationTreeItem>();

        foreach (var item in source
                     .OrderBy(unit => unit.SortOrder)
                     .ThenBy(unit => unit.Name))
        {
            if (item.ParentOrganizationUnitId.HasValue &&
                map.TryGetValue(
                    item.ParentOrganizationUnitId.Value,
                    out var parent))
            {
                parent.Children.Add(
                    map[item.OrganizationUnitId]);
            }
            else
            {
                roots.Add(
                    map[item.OrganizationUnitId]);
            }
        }

        if (selectId.HasValue)
        {
            foreach (var root in roots)
            {
                if (MarkSelectedAndExpand(
                        root,
                        selectId.Value))
                {
                    break;
                }
            }
        }

        return new ObservableCollection<OrganizationTreeItem>(
            roots);
    }

    private static bool MarkSelectedAndExpand(
        OrganizationTreeItem node,
        Guid selectedId)
    {
        if (node.Unit.OrganizationUnitId == selectedId)
        {
            node.IsSelected = true;
            return true;
        }

        foreach (var child in node.Children)
        {
            if (!MarkSelectedAndExpand(
                    child,
                    selectedId))
            {
                continue;
            }

            node.IsExpanded = true;
            return true;
        }

        return false;
    }

    private void TreeUnits_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is OrganizationTreeItem node)
        {
            _selected = node.Unit;
            ShowSelected();
        }
    }

    private void TreeUnits_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        _dragStartPoint =
            e.GetPosition(TreeUnits);

        _draggedNode =
            FindTreeViewItem(
                e.OriginalSource as DependencyObject)
                ?.DataContext
                as OrganizationTreeItem;
    }

    private void TreeUnits_PreviewMouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
            _draggedNode is null)
        {
            return;
        }

        var currentPoint =
            e.GetPosition(TreeUnits);

        if (Math.Abs(
                currentPoint.X -
                _dragStartPoint.X)
            < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(
                currentPoint.Y -
                _dragStartPoint.Y)
            < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var node =
            _draggedNode;

        try
        {
            DragDrop.DoDragDrop(
                TreeUnits,
                node,
                DragDropEffects.Move);
        }
        finally
        {
            _draggedNode = null;
            ClearDropVisuals();
            TxtStatus.Text = _baseStatusText;
        }
    }

    private void TreeUnits_DragOver(
        object sender,
        DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(
                typeof(OrganizationTreeItem)))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var dragged =
            e.Data.GetData(
                typeof(OrganizationTreeItem))
            as OrganizationTreeItem;

        if (dragged is null)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var targetContainer =
            FindTreeViewItem(
                e.OriginalSource as DependencyObject);

        var target =
            targetContainer?.DataContext
                as OrganizationTreeItem;

        var valid =
            CanMoveUnit(
                dragged.Unit,
                target?.Unit,
                out var status);

        e.Effects =
            valid
                ? DragDropEffects.Move
                : DragDropEffects.None;

        HighlightDropTarget(
            targetContainer,
            valid);

        TxtStatus.Text = status;
        e.Handled = true;
    }

    private void TreeUnits_Drop(
        object sender,
        DragEventArgs e)
    {
        try
        {
            if (!e.Data.GetDataPresent(
                    typeof(OrganizationTreeItem)))
            {
                return;
            }

            var dragged =
                e.Data.GetData(
                    typeof(OrganizationTreeItem))
                as OrganizationTreeItem;

            if (dragged is null)
            {
                return;
            }

            var targetContainer =
                FindTreeViewItem(
                    e.OriginalSource as DependencyObject);

            var target =
                targetContainer?.DataContext
                    as OrganizationTreeItem;

            if (!CanMoveUnit(
                    dragged.Unit,
                    target?.Unit,
                    out var status))
            {
                TxtStatus.Text = status;
                return;
            }

            MoveOrganizationUnit(
                dragged.Unit,
                target?.Unit);
        }
        finally
        {
            ClearDropVisuals();
            e.Handled = true;
        }
    }

    private bool CanMoveUnit(
        DmsOrganizationUnit dragged,
        DmsOrganizationUnit? targetParent,
        out string status)
    {
        if (targetParent is not null &&
            dragged.OrganizationUnitId ==
            targetParent.OrganizationUnitId)
        {
            status =
                T("SYS01.Organization.DragDrop.InvalidSelf");

            return false;
        }

        var newParentId =
            targetParent?.OrganizationUnitId;

        if (dragged.ParentOrganizationUnitId ==
            newParentId)
        {
            status =
                T("SYS01.Organization.DragDrop.AlreadyThere");

            return false;
        }

        if (targetParent is not null &&
            IsDescendantOf(
                targetParent,
                dragged.OrganizationUnitId))
        {
            status =
                T("SYS01.Organization.DragDrop.InvalidDescendant");

            return false;
        }

        status =
            targetParent is null
                ? T("SYS01.Organization.DragDrop.ValidRoot")
                : TF(
                    "SYS01.Organization.DragDrop.ValidTarget",
                    DescribeUnit(targetParent));

        return true;
    }

    private bool IsDescendantOf(
        DmsOrganizationUnit candidate,
        Guid possibleAncestorId)
    {
        var current =
            candidate;

        var visited =
            new HashSet<Guid>();

        while (current.ParentOrganizationUnitId.HasValue)
        {
            if (!visited.Add(
                    current.OrganizationUnitId))
            {
                // Existing corrupt data should never permit another
                // hierarchy change.
                return true;
            }

            var parentId =
                current.ParentOrganizationUnitId.Value;

            if (parentId == possibleAncestorId)
            {
                return true;
            }

            var parent =
                _items.FirstOrDefault(unit =>
                    unit.OrganizationUnitId ==
                    parentId);

            if (parent is null)
            {
                return false;
            }

            current = parent;
        }

        return false;
    }

    private void MoveOrganizationUnit(
        DmsOrganizationUnit dragged,
        DmsOrganizationUnit? targetParent)
    {
        var oldParentId =
            dragged.ParentOrganizationUnitId;

        var oldParent =
            oldParentId.HasValue
                ? _items.FirstOrDefault(unit =>
                    unit.OrganizationUnitId ==
                    oldParentId.Value)
                : null;

        var oldParentText =
            DescribeUnit(oldParent);

        var newParentText =
            DescribeUnit(targetParent);

        var confirmation =
            DmsConfirmDialog.Show(
                Window.GetWindow(this),
                T("SYS01.Organization.DragDrop.ConfirmTitle"),
                TF(
                    "SYS01.Organization.DragDrop.ConfirmMessage",
                    DescribeUnit(dragged),
                    oldParentText,
                    newParentText),
                DmsDialogButtons.YesNo);

        if (confirmation != MessageBoxResult.Yes)
        {
            TxtStatus.Text = _baseStatusText;
            return;
        }

        dragged.ParentOrganizationUnitId =
            targetParent?.OrganizationUnitId;

        try
        {
            // DmsMasterDataService performs the authoritative validation too:
            // missing parents and cycles are rejected before the JSON write.
            _service.SaveOrganizationUnits(
                _items);

            _logger.AuditChange(
                "SYS01",
                "OrganizationUnit",
                dragged.OrganizationUnitId.ToString(),
                "ParentOrganizationUnitId",
                oldParentId?.ToString(),
                dragged.ParentOrganizationUnitId?.ToString(),
                _user);

            LoadData(
                dragged.OrganizationUnitId);

            TxtStatus.Text =
                TF(
                    "SYS01.Organization.DragDrop.Saved",
                    DescribeUnit(dragged),
                    oldParentText,
                    newParentText)
                + "\n"
                + _baseStatusText;
        }
        catch (Exception ex)
        {
            // Keep the in-memory model consistent with the file when
            // validation or writing fails.
            dragged.ParentOrganizationUnitId =
                oldParentId;

            DmsMessage.Show(
                ex.Message,
                T("SYS01.Organization.DragDrop.SaveErrorTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            LoadData(
                dragged.OrganizationUnitId);
        }
    }

    private string DescribeUnit(
        DmsOrganizationUnit? unit)
    {
        return unit is null
            ? T("SYS01.Organization.DragDrop.Root")
            : $"{unit.Code} — {unit.Name}";
    }

    private void HighlightDropTarget(
        TreeViewItem? targetContainer,
        bool valid)
    {
        ClearDropVisuals();

        var resourceKey =
            valid
                ? "DmsAccentBrush"
                : "DmsErrorBrush";

        if (targetContainer is null)
        {
            TreePanelBorder.SetResourceReference(
                Border.BorderBrushProperty,
                resourceKey);

            TreePanelBorder.BorderThickness =
                new Thickness(2);

            return;
        }

        _highlightedDropTarget =
            targetContainer;

        targetContainer.SetResourceReference(
            Control.BorderBrushProperty,
            resourceKey);

        targetContainer.BorderThickness =
            new Thickness(2);
    }

    private void ClearDropVisuals()
    {
        if (_highlightedDropTarget is not null)
        {
            _highlightedDropTarget.ClearValue(
                Control.BorderBrushProperty);

            _highlightedDropTarget.ClearValue(
                Control.BorderThicknessProperty);

            _highlightedDropTarget = null;
        }

        TreePanelBorder.SetResourceReference(
            Border.BorderBrushProperty,
            "DmsBorderBrush");

        TreePanelBorder.BorderThickness =
            new Thickness(1);
    }

    private static TreeViewItem? FindTreeViewItem(
        DependencyObject? source)
    {
        var current =
            source;

        while (current is not null)
        {
            if (current is TreeViewItem item)
            {
                return item;
            }

            current =
                VisualTreeHelper.GetParent(
                    current);
        }

        return null;
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
            DmsMessage.Show(
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
            DmsMessage.Show(ex.Message, T("SYS01.Organization.SaveErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
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
