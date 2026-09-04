namespace DMS.Integration.Mes.Models;

public sealed class MesProbeResult
{
    public MesDevice Device { get; init; } = new();
    public string State { get; init; } = "Unknown";
    public bool IsOnline { get; init; }
    public long? ResponseTimeMs { get; init; }
    public string FailureReason { get; init; } = string.Empty;
    public DateTime CheckedAt { get; init; } = DateTime.Now;

    public static MesProbeResult Unknown(MesDevice device, string reason)
    {
        return new MesProbeResult
        {
            Device = device,
            State = "Unknown",
            IsOnline = false,
            FailureReason = reason,
            CheckedAt = DateTime.Now
        };
    }
}
