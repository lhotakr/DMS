using DMS.Core.Domain.Organization;
using System.Collections.ObjectModel;

namespace DMS.Desktop.Views.MasterData;

public sealed class OrganizationTreeItem
{
    public required DmsOrganizationUnit Unit { get; init; }
    public ObservableCollection<OrganizationTreeItem> Children { get; } = new();
    public string DisplayText => $"{Unit.Code}  {Unit.Name}";
}
