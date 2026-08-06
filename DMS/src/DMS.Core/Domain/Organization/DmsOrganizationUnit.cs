namespace DMS.Core.Domain.Organization;

public sealed class DmsOrganizationUnit
{
    public Guid OrganizationUnitId { get; set; } = Guid.NewGuid();
    public Guid? ParentOrganizationUnitId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ExternalCode { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
