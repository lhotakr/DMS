using DMS.Integration.Mes.Reporting.Models;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace DMS.Desktop.Views.Mes;

public partial class MesReportingView
{
    private sealed class Mes06WorkcenterSelectionItem
        : INotifyPropertyChanged
    {
        private bool _isSelected;

        public Mes06WorkcenterSelectionItem(
            string code,
            string description)
        {
            Code = code;
            Description = description;
        }

        public string Code { get; }
        public string Description { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                {
                    return;
                }

                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(
            [CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(
                    propertyName));
        }
    }

    private sealed class Mes06WorkcenterGroupItem
        : INotifyPropertyChanged
    {
        private bool _updating;

        public Mes06WorkcenterGroupItem(
            string name,
            IReadOnlyList<Mes06WorkcenterSelectionItem> workcenters)
        {
            Name = name;
            Workcenters = workcenters;

            foreach (var workcenter
                     in Workcenters)
            {
                workcenter.PropertyChanged += Workcenter_PropertyChanged;
            }
        }

        public string Name { get; }

        public string DisplayName =>
            $"{Name} ({Workcenters.Count})";

        public IReadOnlyList<Mes06WorkcenterSelectionItem> Workcenters { get; }

        public bool? IsChecked
        {
            get
            {
                if (Workcenters.Count == 0)
                {
                    return false;
                }

                var selected =
                    Workcenters.Count(item =>
                        item.IsSelected);

                if (selected == 0)
                {
                    return false;
                }

                if (selected == Workcenters.Count)
                {
                    return true;
                }

                return null;
            }
            set
            {
                if (_updating
                    || !value.HasValue)
                {
                    return;
                }

                _updating = true;

                try
                {
                    foreach (var workcenter
                             in Workcenters)
                    {
                        workcenter.IsSelected =
                            value.Value;
                    }
                }
                finally
                {
                    _updating = false;
                }

                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public void Refresh()
        {
            OnPropertyChanged(
                nameof(IsChecked));
        }

        private void Workcenter_PropertyChanged(
            object? sender,
            PropertyChangedEventArgs e)
        {
            if (string.Equals(
                    e.PropertyName,
                    nameof(Mes06WorkcenterSelectionItem.IsSelected),
                    StringComparison.Ordinal))
            {
                Refresh();
            }
        }

        private void OnPropertyChanged(
            [CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(
                    propertyName));
        }
    }

    private IReadOnlyList<Mes06WorkcenterSelectionItem> _mes06Workcenters =
        Array.Empty<Mes06WorkcenterSelectionItem>();

    private IReadOnlyList<Mes06WorkcenterGroupItem> _mes06WorkcenterGroups =
        Array.Empty<Mes06WorkcenterGroupItem>();

    private bool _mes06SingleWorkcenterMode;
    private bool _mes06UpdatingSingleWorkcenterSelection;

    private void InitializeWorkcenterSelector(
        IReadOnlyList<MesWorkcenterRecord> workcenters,
        IReadOnlyDictionary<string, IReadOnlyList<string>> groupMap)
    {
        var items =
            workcenters
                .Where(workcenter =>
                    !string.IsNullOrWhiteSpace(
                        workcenter.Code))
                .GroupBy(
                    workcenter =>
                        workcenter.Code.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var first =
                        group.First();

                    return new Mes06WorkcenterSelectionItem(
                        first.Code.Trim(),
                        first.Description?.Trim()
                        ?? string.Empty);
                })
                .OrderBy(item =>
                    item.Code,
                    StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        foreach (var item
                 in items)
        {
            item.IsSelected = true;
            item.PropertyChanged += WorkcenterSelectionItem_PropertyChanged;
        }

        var byCode =
            items.ToDictionary(
                item => item.Code,
                StringComparer.OrdinalIgnoreCase);

        var groupMembers =
            new Dictionary<string, HashSet<Mes06WorkcenterSelectionItem>>(
                StringComparer.CurrentCultureIgnoreCase);

        foreach (var item
                 in items)
        {
            if (!groupMap.TryGetValue(
                    item.Code,
                    out var groups)
                || groups.Count == 0)
            {
                AddToWorkcenterGroup(
                    groupMembers,
                    T(
                        "MES06.Workcenters.OtherGroup",
                        "Other work centers"),
                    item);

                continue;
            }

            foreach (var groupName
                     in groups
                         .Where(group =>
                             !string.IsNullOrWhiteSpace(
                                 group)))
            {
                AddToWorkcenterGroup(
                    groupMembers,
                    groupName.Trim(),
                    item);
            }
        }

        var groupsView =
            groupMembers
                .OrderBy(
                    pair => pair.Key,
                    StringComparer.CurrentCultureIgnoreCase)
                .Select(pair =>
                    new Mes06WorkcenterGroupItem(
                        pair.Key,
                        pair.Value
                            .OrderBy(item =>
                                item.Code,
                                StringComparer.CurrentCultureIgnoreCase)
                            .ToList()))
                .ToList();

        _mes06Workcenters =
            items;

        _mes06WorkcenterGroups =
            groupsView;

        WorkcenterGroupItems.ItemsSource =
            groupsView;

        BtnSelectAllWorkcenters.Content =
            T(
                "MES06.Workcenters.SelectAll",
                "Select all");

        BtnClearWorkcenters.Content =
            T(
                "MES06.Workcenters.Clear",
                "Clear selection");

        UpdateWorkcenterSelectionSummary();
    }

    private static void AddToWorkcenterGroup(
        IDictionary<string, HashSet<Mes06WorkcenterSelectionItem>> groups,
        string groupName,
        Mes06WorkcenterSelectionItem item)
    {
        if (!groups.TryGetValue(
                groupName,
                out var members))
        {
            members =
                new HashSet<Mes06WorkcenterSelectionItem>();

            groups[groupName] =
                members;
        }

        members.Add(
            item);
    }

    private void WorkcenterSelectionItem_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (!string.Equals(
                e.PropertyName,
                nameof(Mes06WorkcenterSelectionItem.IsSelected),
                StringComparison.Ordinal))
        {
            return;
        }

        if (_mes06SingleWorkcenterMode
            && !_mes06UpdatingSingleWorkcenterSelection
            && sender
                is Mes06WorkcenterSelectionItem changedItem)
        {
            _mes06UpdatingSingleWorkcenterSelection =
                true;

            try
            {
                if (changedItem.IsSelected)
                {
                    foreach (var item
                             in _mes06Workcenters)
                    {
                        if (!ReferenceEquals(
                                item,
                                changedItem)
                            && item.IsSelected)
                        {
                            item.IsSelected =
                                false;
                        }
                    }
                }
                else if (!_mes06Workcenters.Any(item =>
                             item.IsSelected))
                {
                    // Production Graph always has exactly one workcenter.
                    changedItem.IsSelected =
                        true;
                }
            }
            finally
            {
                _mes06UpdatingSingleWorkcenterSelection =
                    false;
            }
        }

        foreach (var group
                 in _mes06WorkcenterGroups)
        {
            group.Refresh();
        }

        UpdateWorkcenterSelectionSummary();
    }

    private void BtnSelectAllWorkcenters_Click(
        object sender,
        RoutedEventArgs e)
    {
        SelectAllWorkcenters(
            true);
    }

    private void BtnClearWorkcenters_Click(
        object sender,
        RoutedEventArgs e)
    {
        SelectAllWorkcenters(
            false);
    }

    private void SelectAllWorkcenters(
        bool selected)
    {
        if (_mes06SingleWorkcenterMode)
        {
            var keep =
                _mes06Workcenters
                    .FirstOrDefault(item =>
                        item.IsSelected)
                ?? _mes06Workcenters
                    .FirstOrDefault();

            if (keep is not null)
            {
                _mes06UpdatingSingleWorkcenterSelection =
                    true;

                try
                {
                    foreach (var item
                             in _mes06Workcenters)
                    {
                        item.IsSelected =
                            ReferenceEquals(
                                item,
                                keep);
                    }
                }
                finally
                {
                    _mes06UpdatingSingleWorkcenterSelection =
                        false;
                }
            }

            UpdateWorkcenterSelectionSummary();

            return;
        }

        foreach (var item
                 in _mes06Workcenters)
        {
            item.IsSelected =
                selected;
        }

        UpdateWorkcenterSelectionSummary();
    }

    private void UpdateProductionGraphWorkcenterMode(
        DMS.Integration.Mes.Reporting.Definitions.MesReportDefinition? definition)
    {
        var enable =
            definition is not null
            && IsProductionGraphReport(
                definition);

        _mes06SingleWorkcenterMode =
            enable;

        BtnSelectAllWorkcenters.Visibility =
            enable
                ? Visibility.Collapsed
                : Visibility.Visible;

        BtnClearWorkcenters.Visibility =
            enable
                ? Visibility.Collapsed
                : Visibility.Visible;

        if (enable)
        {
            var keep =
                _mes06Workcenters
                    .Where(item =>
                        item.IsSelected)
                    .OrderBy(
                        item =>
                            item.Code,
                        StringComparer.CurrentCultureIgnoreCase)
                    .FirstOrDefault()
                ?? _mes06Workcenters
                    .OrderBy(
                        item =>
                            item.Code,
                        StringComparer.CurrentCultureIgnoreCase)
                    .FirstOrDefault();

            if (keep is not null)
            {
                _mes06UpdatingSingleWorkcenterSelection =
                    true;

                try
                {
                    foreach (var item
                             in _mes06Workcenters)
                    {
                        item.IsSelected =
                            ReferenceEquals(
                                item,
                                keep);
                    }
                }
                finally
                {
                    _mes06UpdatingSingleWorkcenterSelection =
                        false;
                }
            }
        }

        foreach (var group
                 in _mes06WorkcenterGroups)
        {
            group.Refresh();
        }

        UpdateWorkcenterSelectionSummary();
    }

    private void UpdateWorkcenterSelectionSummary()
    {
        var total =
            _mes06Workcenters.Count;

        var selected =
            _mes06Workcenters.Count(item =>
                item.IsSelected);

        if (total == 0)
        {
            TxtWorkcenterSelectionSummary.Text =
                T(
                    "MES06.Workcenters.NoneAvailable",
                    "No work centers");

            return;
        }

        if (_mes06SingleWorkcenterMode
            && selected == 1)
        {
            TxtWorkcenterSelectionSummary.Text =
                _mes06Workcenters
                    .First(item =>
                        item.IsSelected)
                    .Code;

            return;
        }

        if (selected == total)
        {
            TxtWorkcenterSelectionSummary.Text =
                string.Format(
                    T(
                        "MES06.Workcenters.All",
                        "All work centers ({0})"),
                    total);

            return;
        }

        if (selected == 0)
        {
            TxtWorkcenterSelectionSummary.Text =
                T(
                    "MES06.Workcenters.NoneSelected",
                    "No work center selected");

            return;
        }

        TxtWorkcenterSelectionSummary.Text =
            string.Format(
                T(
                    "MES06.Workcenters.Selected",
                    "{0} work centers selected"),
                selected);
    }

    private IReadOnlyList<string> GetSelectedWorkcenterCodes()
    {
        return _mes06Workcenters
            .Where(item =>
                item.IsSelected)
            .Select(item =>
                item.Code)
            .ToList();
    }

    private string GetSingleSelectedWorkcenterCodeForServer()
    {
        var selected =
            GetSelectedWorkcenterCodes();

        return selected.Count == 1
            ? selected[0]
            : string.Empty;
    }

    private string GetSelectedWorkcenterAuditText()
    {
        var selected =
            GetSelectedWorkcenterCodes();

        if (selected.Count == _mes06Workcenters.Count)
        {
            return "ALL";
        }

        return string.Join(
            ",",
            selected);
    }

    private IReadOnlyList<object> ApplySelectedWorkcenterFilter(
        IReadOnlyList<object> rows)
    {
        if (_mes06Workcenters.Count == 0)
        {
            return rows;
        }

        var selected =
            new HashSet<string>(
                GetSelectedWorkcenterCodes(),
                StringComparer.OrdinalIgnoreCase);

        if (selected.Count == _mes06Workcenters.Count)
        {
            return rows;
        }

        if (selected.Count == 0)
        {
            return Array.Empty<object>();
        }

        return rows
            .Where(row =>
            {
                var code =
                    FirstNonEmpty(
                        ReadProperty(
                            row,
                            "WorkcenterCode"),
                        ReadProperty(
                            row,
                            "Workcenter"),
                        ReadProperty(
                            row,
                            "ResourceCode"),
                        ReadProperty(
                            row,
                            "Resource"));

                return !string.IsNullOrWhiteSpace(
                           code)
                       && selected.Contains(
                           code.Trim());
            })
            .ToList();
    }
}
