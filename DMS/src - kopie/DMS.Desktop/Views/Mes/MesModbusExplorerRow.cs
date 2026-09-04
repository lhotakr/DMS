using DMS.Core.Mes;
using DMS.Desktop.Services.Mes;
using System.Globalization;

namespace DMS.Desktop.Views.Mes;

public sealed class MesModbusExplorerRow
{
    public MesModbusArea Area { get; init; }

    public int Address { get; init; }

    public bool IsReadable { get; init; }

    public ulong NumericValue { get; init; }

    public string ValueText { get; init; } = string.Empty;

    public string HexText { get; init; } = string.Empty;

    public string BinaryText { get; init; } = string.Empty;

    public string SignedText { get; init; } = string.Empty;

    public string PreviousText { get; init; } = string.Empty;

    public string DeltaText { get; init; } = string.Empty;

    public string ChangedBitsText { get; init; } = string.Empty;

    public int? SuggestedBitIndex { get; init; }

    public bool HasChanged { get; init; }

    public bool IsNonZero { get; init; }

    public string StatusText { get; init; } = string.Empty;

    public string Error { get; init; } = string.Empty;

    public string ReadAtText { get; init; } = string.Empty;

    public string Key => BuildKey(Area, Address);

    public string SearchText =>
        $"{Area} {Address} {ValueText} {HexText} {BinaryText} {SignedText} " +
        $"{PreviousText} {DeltaText} {ChangedBitsText} {StatusText} {Error}";

    public static MesModbusExplorerRow FromValue(
        MesModbusExplorerValue value,
        IReadOnlyDictionary<string, ulong> baseline)
    {
        var key = BuildKey(value.Area, value.Address);
        var hasPrevious = baseline.TryGetValue(key, out var previous);

        if (!value.IsReadable)
        {
            return new MesModbusExplorerRow
            {
                Area = value.Area,
                Address = value.Address,
                IsReadable = false,
                StatusText = "Nelze číst",
                Error = value.Error,
                ReadAtText = value.ReadAt.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)
            };
        }

        var numeric = value.NumericValue;
        var isBitArea = value.Area is MesModbusArea.Coil or MesModbusArea.DiscreteInput;
        var hasChanged = hasPrevious && previous != numeric;
        var xor = hasPrevious ? previous ^ numeric : 0UL;
        var changedBits = isBitArea
            ? hasChanged ? "bit 0" : string.Empty
            : FormatChangedBits((ushort)xor);
        var suggestedBit = isBitArea
            ? 0
            : GetSingleChangedBit((ushort)xor);

        return new MesModbusExplorerRow
        {
            Area = value.Area,
            Address = value.Address,
            IsReadable = true,
            NumericValue = numeric,
            ValueText = isBitArea
                ? numeric == 0 ? "OFF" : "ON"
                : numeric.ToString(CultureInfo.InvariantCulture),
            HexText = isBitArea
                ? numeric == 0 ? "0" : "1"
                : $"0x{numeric:X4}",
            BinaryText = isBitArea
                ? numeric == 0 ? "0" : "1"
                : Convert.ToString((int)numeric, 2).PadLeft(16, '0'),
            SignedText = isBitArea
                ? string.Empty
                : unchecked((short)(ushort)numeric).ToString(CultureInfo.InvariantCulture),
            PreviousText = hasPrevious
                ? isBitArea
                    ? previous == 0 ? "OFF" : "ON"
                    : previous.ToString(CultureInfo.InvariantCulture)
                : "—",
            DeltaText = hasPrevious
                ? isBitArea
                    ? $"{(previous == 0 ? "OFF" : "ON")} → {(numeric == 0 ? "OFF" : "ON")}"
                    : ((long)numeric - (long)previous).ToString("+0;-0;0", CultureInfo.InvariantCulture)
                : string.Empty,
            ChangedBitsText = changedBits,
            SuggestedBitIndex = suggestedBit,
            HasChanged = hasChanged,
            IsNonZero = numeric != 0,
            StatusText = hasChanged ? "Změněno" : "Čitelné",
            ReadAtText = value.ReadAt.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)
        };
    }

    public static string BuildKey(MesModbusArea area, int address) =>
        $"{area}:{address}";

    private static string FormatChangedBits(ushort xor)
    {
        if (xor == 0)
        {
            return string.Empty;
        }

        var bits = new List<int>();

        for (var bit = 0; bit < 16; bit++)
        {
            if ((xor & (1 << bit)) != 0)
            {
                bits.Add(bit);
            }
        }

        return string.Join(", ", bits.Select(bit => $"bit {bit}"));
    }

    private static int? GetSingleChangedBit(ushort xor)
    {
        if (xor == 0 || (xor & (xor - 1)) != 0)
        {
            return null;
        }

        for (var bit = 0; bit < 16; bit++)
        {
            if ((xor & (1 << bit)) != 0)
            {
                return bit;
            }
        }

        return null;
    }
}
