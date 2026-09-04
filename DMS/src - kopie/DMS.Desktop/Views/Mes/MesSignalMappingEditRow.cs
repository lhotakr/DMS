using DMS.Core.Mes;
using System.Globalization;

namespace DMS.Desktop.Views.Mes;

public sealed class MesSignalMappingEditRow
{
    public string Code { get; set; } = string.Empty;
    public string LogicalSignal { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ModuleType { get; set; } = string.Empty;
    public int Slot { get; set; }
    public int Channel { get; set; }
    public bool Enabled { get; set; } = true;
    public bool VisibleInMes03 { get; set; } = true;
    public bool Inverted { get; set; }

    /// <summary>
    /// Editable Modbus area. The physical address is intentionally kept separate
    /// from the logical signal so a wiring exception never requires a code change.
    /// </summary>
    public MesModbusArea Area { get; set; } = MesModbusArea.InputRegister;

    /// <summary>
    /// Zero-based Modbus PDU address as text. Empty means that the point is not mapped.
    /// Do not enter 30001/40001 notation here.
    /// </summary>
    public string AddressText { get; set; } = string.Empty;

    public MesDataType DataType { get; set; } = MesDataType.Bool;

    /// <summary>
    /// Optional bit index 0..15 when Bool is packed inside a register.
    /// Leave empty for coils/discrete inputs or for a whole-register boolean.
    /// </summary>
    public string BitIndexText { get; set; } = string.Empty;

    public MesWordOrder WordOrder { get; set; } = MesWordOrder.BigEndian;

    public string SourceText
    {
        get
        {
            if (!TryParseAddress(AddressText, out var address))
            {
                return "Nenamapováno";
            }

            var bitText = TryParseBitIndex(BitIndexText, out var bitIndex)
                ? $" / bit {bitIndex}"
                : string.Empty;

            return $"{Area} {address} / {DataType}{bitText}";
        }
    }

    public static MesSignalMappingEditRow FromDefinition(MesDataPointDefinition definition)
    {
        var source = definition.Source;

        return new MesSignalMappingEditRow
        {
            Code = definition.Code,
            LogicalSignal = definition.LogicalSignal,
            DisplayName = definition.DisplayName,
            ModuleType = definition.ModuleType,
            Slot = definition.Slot,
            Channel = definition.Channel,
            Enabled = definition.Enabled,
            VisibleInMes03 = definition.VisibleInMes03,
            Inverted = definition.Inverted,
            Area = source?.Area ?? GetDefaultArea(definition.Code),
            AddressText = source?.Address?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            DataType = source?.DataType ?? GetDefaultDataType(definition.Code),
            BitIndexText = source?.BitIndex?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            WordOrder = source?.WordOrder ?? MesWordOrder.BigEndian
        };
    }

    public MesDataPointDefinition ToDefinition()
    {
        return new MesDataPointDefinition
        {
            Code = Code?.Trim() ?? string.Empty,
            LogicalSignal = LogicalSignal?.Trim() ?? string.Empty,
            DisplayName = DisplayName?.Trim() ?? string.Empty,
            ModuleType = ModuleType?.Trim() ?? string.Empty,
            Slot = Slot,
            Channel = Channel,
            Enabled = Enabled,
            VisibleInMes03 = VisibleInMes03,
            Inverted = Inverted,
            Source = BuildSource()
        };
    }

    private MesModbusSource? BuildSource()
    {
        if (string.IsNullOrWhiteSpace(AddressText))
        {
            return null;
        }

        if (!TryParseAddress(AddressText, out var address))
        {
            throw new InvalidOperationException(
                $"Datový bod {Code}: Modbus adresa musí být celé číslo 0 až 65535.");
        }

        int? bitIndex = null;

        if (!string.IsNullOrWhiteSpace(BitIndexText))
        {
            if (!TryParseBitIndex(BitIndexText, out var parsedBitIndex))
            {
                throw new InvalidOperationException(
                    $"Datový bod {Code}: bit registru musí být celé číslo 0 až 15.");
            }

            bitIndex = parsedBitIndex;
        }

        var dataType = Area is MesModbusArea.Coil or MesModbusArea.DiscreteInput
            ? MesDataType.Bool
            : DataType;

        if (Area is MesModbusArea.Coil or MesModbusArea.DiscreteInput)
        {
            bitIndex = null;
        }

        return new MesModbusSource
        {
            Area = Area,
            Address = address,
            DataType = dataType,
            BitIndex = bitIndex,
            WordOrder = WordOrder
        };
    }

    private static bool TryParseAddress(string? value, out int address)
    {
        return int.TryParse(
                   value?.Trim(),
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out address)
               && address is >= 0 and <= ushort.MaxValue;
    }

    private static bool TryParseBitIndex(string? value, out int bitIndex)
    {
        return int.TryParse(
                   value?.Trim(),
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out bitIndex)
               && bitIndex is >= 0 and <= 15;
    }

    private static MesModbusArea GetDefaultArea(string? code)
    {
        if (code?.StartsWith("Output", StringComparison.OrdinalIgnoreCase) == true)
        {
            return MesModbusArea.HoldingRegister;
        }

        return MesModbusArea.InputRegister;
    }

    private static MesDataType GetDefaultDataType(string? code)
    {
        return code?.StartsWith("Counter", StringComparison.OrdinalIgnoreCase) == true
            ? MesDataType.UInt16
            : MesDataType.Bool;
    }
}
