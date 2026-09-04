using System.Net;

namespace DMS.Integration.Mes.Models;

public sealed class MesDevice
{
    public string Address { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public int SourceLineNumber { get; set; }

    public bool IsIpAddress => IPAddress.TryParse(Address, out _);

    public string Key => string.IsNullOrWhiteSpace(Address)
        ? Name.Trim()
        : Address.Trim();
}
