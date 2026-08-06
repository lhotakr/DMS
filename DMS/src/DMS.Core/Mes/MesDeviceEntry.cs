using System.Net;

namespace DMS.Core.Mes;

/// <summary>
/// One line from the shared MES devices.txt inventory.
/// The source format stays intentionally simple: IP;TYPE;NAME;...
/// </summary>
public sealed class MesDeviceEntry
{
    public string IpAddress { get; init; } = string.Empty;

    public string DeviceType { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string RawLine { get; init; } = string.Empty;

    public int SourceLineNumber { get; init; }

    public bool IsMachine =>
        string.Equals(DeviceType, "STROJ", StringComparison.OrdinalIgnoreCase);

    public string StationCode => MesDeviceNaming.ExtractStationCode(Name);

    public string SuggestedDriver => MesDeviceNaming.SuggestDriver(Name);

    public bool HasValidIpAddress => IPAddress.TryParse(IpAddress, out _);

    public string DisplayText => string.IsNullOrWhiteSpace(StationCode)
        ? $"{Name} ({IpAddress})"
        : $"{StationCode} — {Name} ({IpAddress})";

    public override string ToString()
    {
        return DisplayText;
    }
}

public static class MesDeviceNaming
{
    private static readonly string[] KnownPrefixes =
    {
        "BRIO",
        "S7AGL4",
        "S7AGL",
        "S7",
        "PLC"
    };

    public static string ExtractStationCode(string? name)
    {
        var value = name?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        foreach (var prefix in KnownPrefixes)
        {
            if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var stationCode = value[prefix.Length..]
                .Trim(' ', '-', ':', '/');

            return string.IsNullOrWhiteSpace(stationCode)
                ? value
                : stationCode;
        }

        return value;
    }

    public static string SuggestDriver(string? name)
    {
        var value = name?.Trim() ?? string.Empty;

        if (value.Contains("BRIO", StringComparison.OrdinalIgnoreCase))
        {
            return MesDriverKeys.BrX20ModbusTcp;
        }

        if (value.Contains("S7AGL", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("S7", StringComparison.OrdinalIgnoreCase))
        {
            return MesDriverKeys.SiemensDeferred;
        }

        return MesDriverKeys.Unconfigured;
    }
}
