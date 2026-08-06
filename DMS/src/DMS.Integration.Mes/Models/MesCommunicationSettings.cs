namespace DMS.Integration.Mes.Models;

public sealed class MesCommunicationSettings
{
    public bool IsMonitoringEnabled { get; set; } = true;
    public int PingTimeoutMs { get; set; } = 1200;
    public int MaxParallelism { get; set; } = 16;
    public int AutoRefreshSeconds { get; set; } = 60;
    public string DevicesFilePath { get; set; } = @"\\10.131.10.5\FISData\devices.txt";

    public bool EnableStationDataPolling { get; set; } = true;
    public int StationPollTimeoutMs { get; set; } = 1500;
    public int StationAutoRefreshSeconds { get; set; } = 10;
    public string StationsFilePath { get; set; } = string.Empty;
    public string StationSnapshotsFolder { get; set; } = string.Empty;

    public bool EnableMachineUnlockSignal { get; set; } = false;
    public string UnlockProvider { get; set; } = "None";
    public string GatewayHost { get; set; } = string.Empty;
    public int GatewayPort { get; set; } = 0;
    public string SharedHandshakeFolder { get; set; } = string.Empty;
    public bool RequireOperatorConfirmation { get; set; } = true;
    public int SetupOkValidMinutes { get; set; } = 480;

    public void Normalize()
    {
        PingTimeoutMs = Math.Clamp(PingTimeoutMs, 250, 30000);
        MaxParallelism = Math.Clamp(MaxParallelism, 1, 64);
        AutoRefreshSeconds = Math.Clamp(AutoRefreshSeconds, 10, 3600);
        StationPollTimeoutMs = Math.Clamp(StationPollTimeoutMs, 250, 30000);
        StationAutoRefreshSeconds = Math.Clamp(StationAutoRefreshSeconds, 1, 3600);
        GatewayPort = Math.Clamp(GatewayPort, 0, 65535);
        SetupOkValidMinutes = Math.Clamp(SetupOkValidMinutes, 1, 1440);
        DevicesFilePath = DevicesFilePath?.Trim() ?? string.Empty;
        StationsFilePath = StationsFilePath?.Trim() ?? string.Empty;
        StationSnapshotsFolder = StationSnapshotsFolder?.Trim() ?? string.Empty;
        UnlockProvider = string.IsNullOrWhiteSpace(UnlockProvider) ? "None" : UnlockProvider.Trim();
        GatewayHost = GatewayHost?.Trim() ?? string.Empty;
        SharedHandshakeFolder = SharedHandshakeFolder?.Trim() ?? string.Empty;
    }
}
