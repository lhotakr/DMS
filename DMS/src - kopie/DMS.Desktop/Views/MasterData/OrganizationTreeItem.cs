using DMS.Core.Domain.Organization;
using System.Collections.ObjectModel;

namespace DMS.Desktop.Views.MasterData;

public sealed class OrganizationTreeItem
{
    public required DmsOrganizationUnit Unit { get; init; }

    public ObservableCollection<OrganizationTreeItem> Children { get; } = new();

    public string DisplayText => $"{Unit.Code}  {Unit.Name}";

    // Used to restore the selected node and expand its ancestors after
    // saving/re-parenting and rebuilding the hierarchy.
    public bool IsSelected { get; set; }
    public bool IsExpanded { get; set; }
}
