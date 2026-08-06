namespace DMS.Integration.Mes.Models;

public sealed class MesMonitorSnapshot
{
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int TotalDevices { get; set; }
    public int OnlineDevices { get; set; }
    public int OfflineDevices { get; set; }
    public int UnknownDevices { get; set; }
    public List<MesMonitorSnapshotRow> Rows { get; set; } = new();
}

public sealed class MesMonitorSnapshotRow
{
    public string Address { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public string State { get; set; } = "Unknown";
    public bool IsOnline { get; set; }
    public long? ResponseTimeMs { get; set; }
    public string FailureReason { get; set; } = string.Empty;
    public DateTime CheckedAt { get; set; }
}
