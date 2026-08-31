namespace DMS.Integration.Mes.Database;

public sealed class MesConnectionHealthResult
{
    public bool IsEnabled { get; init; }

    public bool IsConnected { get; init; }

    public bool CanSelect { get; init; }

    public bool CanViewDefinition { get; init; }

    public string Server { get; init; } = string.Empty;

    public string Database { get; init; } = string.Empty;

    public string LoginName { get; init; } = string.Empty;

    public string ServerVersion { get; init; } = string.Empty;

    public long LatencyMilliseconds { get; init; }

    public DateTimeOffset CheckedAt { get; init; }

    public string Error { get; init; } = string.Empty;
}
