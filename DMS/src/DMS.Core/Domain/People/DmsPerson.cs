namespace DMS.Core.Domain.People;

public sealed class DmsPerson
{
    public Guid PersonId { get; set; } = Guid.NewGuid();
    public string PersonnelNumber { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public Guid OrganizationUnitId { get; set; }
    public DmsPersonType PersonType { get; set; }
    public bool IsActive { get; set; } = true;

    public string DisplayName => $"{FirstName} {LastName}".Trim();
}
