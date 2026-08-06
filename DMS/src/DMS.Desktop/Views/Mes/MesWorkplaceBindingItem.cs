using DMS.Core.Mes;

namespace DMS.Desktop.Views.Mes;

public sealed class MesWorkplaceBindingItem
{
    public MesDeviceEntry? Device { get; init; }
    public string StationCode { get; init; } = string.Empty;
    public string IpAddress { get; init; } = string.Empty;
    public string DeviceName { get; init; } = string.Empty;
    public string SuggestedDriver { get; init; } = MesDriverKeys.Unconfigured;
    public bool HasBinding { get; init; }

    public string DisplayText =>
        $"{StationCode} — {DeviceName} — {IpAddress}" +
        (HasBinding ? string.Empty : " — bez mapování");

    public override string ToString()
    {
        return DisplayText;
    }
}
