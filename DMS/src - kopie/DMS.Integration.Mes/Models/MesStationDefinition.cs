namespace DMS.Integration.Mes.Models;

public sealed class MesStationDefinition
{
    public string StationCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string WorkCenter { get; set; } = string.Empty;
    public string Protocol { get; set; } = MesStationProtocols.Simulated;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public int Rack { get; set; }
    public int Slot { get; set; } = 1;
    public int PollTimeoutMs { get; set; } = 1500;
    public bool IsActive { get; set; } = true;
    public string Note { get; set; } = string.Empty;
    public List<MesStationDataPointDefinition> DataPoints { get; set; } = new();

    public string EffectiveName => string.IsNullOrWhiteSpace(DisplayName) ? StationCode : DisplayName;

    public void Normalize()
    {
        StationCode = StationCode?.Trim() ?? string.Empty;
        DisplayName = DisplayName?.Trim() ?? string.Empty;
        WorkCenter = WorkCenter?.Trim() ?? string.Empty;
        Protocol = string.IsNullOrWhiteSpace(Protocol) ? MesStationProtocols.Simulated : Protocol.Trim();
        Host = Host?.Trim() ?? string.Empty;
        Port = Math.Clamp(Port, 0, 65535);
        Rack = Math.Clamp(Rack, 0, 15);
        Slot = Math.Clamp(Slot, 0, 15);
        PollTimeoutMs = Math.Clamp(PollTimeoutMs, 250, 30000);
        Note = Note?.Trim() ?? string.Empty;

        foreach (var point in DataPoints)
        {
            point.Normalize();
        }
    }
}

public sealed class MesStationDataPointDefinition
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string DataType { get; set; } = "Bool";
    public string Role { get; set; } = MesDataPointRoles.Input;
    public bool IsRequired { get; set; } = true;

    public string EffectiveDisplayName => string.IsNullOrWhiteSpace(DisplayName) ? Name : DisplayName;

    public void Normalize()
    {
        Name = Name?.Trim() ?? string.Empty;
        DisplayName = DisplayName?.Trim() ?? string.Empty;
        Address = Address?.Trim() ?? string.Empty;
        DataType = string.IsNullOrWhiteSpace(DataType) ? "Bool" : DataType.Trim();
        Role = string.IsNullOrWhiteSpace(Role) ? MesDataPointRoles.Input : Role.Trim();
    }
}

public static class MesStationProtocols
{
    public const string Simulated = "Simulated";
    public const string FileMirror = "FileMirror";
    public const string TcpText = "TcpText";
    public const string BRGateway = "BRGateway";
    public const string SiemensS7 = "SiemensS7";
}

public static class MesDataPointRoles
{
    public const string Counter = "Counter";
    public const string Input = "Input";
    public const string Output = "Output";
    public const string Status = "Status";
}
