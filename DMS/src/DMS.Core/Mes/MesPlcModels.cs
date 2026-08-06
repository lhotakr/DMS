namespace DMS.Core.Mes;

public enum MesModbusArea
{
    Coil,
    DiscreteInput,
    HoldingRegister,
    InputRegister
}

public enum MesDataType
{
    Bool,
    UInt16,
    Int16,
    UInt32,
    Int32,
    Float32
}

public enum MesWordOrder
{
    BigEndian,
    LittleEndianWordSwap
}

public enum MesDataPointQuality
{
    Unknown,
    Valid,
    Stale,
    Invalid,
    Offline,
    ConfigurationError,
    Unsupported
}

public sealed class MesIntegrationSettings
{
    /// <summary>
    /// Absolute path or a path relative to the DMS configuration root.
    /// It should point to the same live devices.txt used by the MES network monitor.
    /// </summary>
    public string DevicesFilePath { get; set; } = "devices.txt";

    /// <summary>
    /// Absolute path or a path relative to the DMS configuration root.
    /// </summary>
    public string PlcBindingsFilePath { get; set; } = "mes-plc-bindings.json";

    public int DeviceInventoryReloadSeconds { get; set; } = 5;

    public int DefaultRefreshIntervalMs { get; set; } = 500;

    public int DefaultTimeoutMs { get; set; } = 3000;

    public int DefaultStaleAfterSeconds { get; set; } = 3;
}

public sealed class MesPlcBindingSet
{
    public List<MesPlcBinding> Devices { get; set; } = new();
}

public sealed class MesPlcBinding
{
    public string StationCode { get; set; } = string.Empty;

    public string? IpAddressOverride { get; set; }

    public string Driver { get; set; } = MesDriverKeys.Unconfigured;

    public bool Enabled { get; set; } = true;

    public int Port { get; set; } = 502;

    public byte UnitId { get; set; } = 0;

    public int PollIntervalMs { get; set; } = 500;

    public int TimeoutMs { get; set; } = 3000;

    public int StaleAfterSeconds { get; set; } = 3;

    public int StopTimeoutSeconds { get; set; } = 30;

    public string Controller { get; set; } = string.Empty;

    public List<MesModuleDefinition> Modules { get; set; } = new();

    public List<MesDataPointDefinition> DataPoints { get; set; } = new();
}

public sealed class MesModuleDefinition
{
    public int Slot { get; set; }

    public string Type { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}

public sealed class MesDataPointDefinition
{
    /// <summary>
    /// Technical point name matching FASTEC where possible, e.g. Counter1 or Input4.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Stable logical signal key, e.g. GOOD_PIECES or UV_LAMP_2_ACTIVE.
    /// This is configurable so reversed counter wiring never requires a code change.
    /// </summary>
    public string LogicalSignal { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string ModuleType { get; set; } = string.Empty;

    public int Slot { get; set; }

    public int Channel { get; set; }

    public bool Enabled { get; set; } = true;

    public bool VisibleInMes03 { get; set; } = true;

    public bool Inverted { get; set; }

    public MesModbusSource? Source { get; set; }
}

public sealed class MesModbusSource
{
    public MesModbusArea Area { get; set; }

    /// <summary>
    /// Zero-based Modbus PDU address. Do not enter 30001/40001 notation here.
    /// Leave null until the X20 process image/register map is confirmed.
    /// </summary>
    public int? Address { get; set; }

    public MesDataType DataType { get; set; } = MesDataType.Bool;

    /// <summary>
    /// Optional bit index 0..15 when a boolean is stored inside a 16-bit register.
    /// </summary>
    public int? BitIndex { get; set; }

    public MesWordOrder WordOrder { get; set; } = MesWordOrder.BigEndian;
}

public sealed class MesDataPointValue
{
    public string Code { get; init; } = string.Empty;

    public string LogicalSignal { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string ModuleType { get; init; } = string.Empty;

    public int Slot { get; init; }

    public int Channel { get; init; }

    public string SourceText { get; init; } = string.Empty;

    public object? RawValue { get; init; }

    public string DisplayValue { get; init; } = string.Empty;

    public MesDataPointQuality Quality { get; init; }

    public string Error { get; init; } = string.Empty;

    public DateTimeOffset ReadAt { get; init; }
}

public sealed class MesDeviceSnapshot
{
    public MesDeviceEntry Device { get; init; } = new();

    public MesPlcBinding? Binding { get; init; }

    public bool IsOnline { get; init; }

    public string StatusMessage { get; init; } = string.Empty;

    public DateTimeOffset ReadAt { get; init; }

    public IReadOnlyList<MesDataPointValue> DataPoints { get; init; } =
        Array.Empty<MesDataPointValue>();
}
