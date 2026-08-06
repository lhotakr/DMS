using DMS.Core.Domain.Organization;
using DMS.Core.Domain.People;
using DMS.Core.Domain.Units;

namespace DMS.Desktop.Services.MasterData;

public sealed class DmsMasterDataSnapshot
{
    public List<DmsOrganizationUnit> OrganizationUnits { get; set; } = new();
    public List<DmsPerson> People { get; set; } = new();
    public List<UnitDimension> UnitDimensions { get; set; } = new();
    public List<UnitDefinition> Units { get; set; } = new();
}
