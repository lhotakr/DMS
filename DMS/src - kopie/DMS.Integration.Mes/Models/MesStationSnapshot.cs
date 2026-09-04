namespace DMS.Integration.Mes.Models;

public sealed class MesStationSnapshot
{
    public string StationCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string WorkCenter { get; set; } = string.Empty;
    public string Protocol { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public bool IsOnline { get; set; }
    public string State { get; set; } = "Unknown";
    public string ErrorMessage { get; set; } = string.Empty;
    public long? ResponseTimeMs { get; set; }
    public DateTime CheckedAt { get; set; } = DateTime.Now;
    public List<MesStationDataPointValue> DataPoints { get; set; } = new();
}

public sealed class MesStationDataPointValue
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool IsOk { get; set; }
    public string Quality { get; set; } = "Unknown";
    public string ErrorMessage { get; set; } = string.Empty;
}
