using DMS.Core.Mes;
using System.Diagnostics;
using System.Net.Sockets;

namespace DMS.Desktop.Services.Mes;

/// <summary>
/// Read-only Modbus TCP diagnostic service used by MES00.
/// It can test a TCP endpoint and discover readable Modbus addresses.
/// No Modbus write function is implemented here.
/// </summary>
public sealed class MesModbusExplorerService
{
    public async Task<MesModbusConnectionProbe> TestTcpConnectionAsync(
        string host,
        int port,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return new MesModbusConnectionProbe(false, TimeSpan.Zero, "IP address is empty.");
        }

        if (port is < 1 or > 65535)
        {
            return new MesModbusConnectionProbe(false, TimeSpan.Zero, $"Invalid TCP port: {port}.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Math.Clamp(timeoutMs, 250, 30_000));

        using var client = new TcpClient
        {
            NoDelay = true
        };

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await client.ConnectAsync(host, port)
                .WaitAsync(timeout.Token)
                .ConfigureAwait(false);

            stopwatch.Stop();
            return new MesModbusConnectionProbe(true, stopwatch.Elapsed, "TCP port is reachable.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return new MesModbusConnectionProbe(
                false,
                stopwatch.Elapsed,
                $"TCP connection timed out after {Math.Clamp(timeoutMs, 250, 30_000)} ms.");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new MesModbusConnectionProbe(false, stopwatch.Elapsed, ex.Message);
        }
    }

    public async Task<IReadOnlyList<MesModbusExplorerValue>> ScanAsync(
        string host,
        int port,
        byte unitId,
        int timeoutMs,
        MesModbusArea area,
        int startAddress,
        int count,
        int blockSize,
        IProgress<MesModbusScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        ValidateRange(startAddress, count);

        var maximumBlockSize = area is MesModbusArea.Coil or MesModbusArea.DiscreteInput
            ? 2000
            : 125;

        blockSize = Math.Clamp(blockSize, 1, maximumBlockSize);

        var values = new List<MesModbusExplorerValue>(count);
        var budget = new MesModbusRequestBudget(Math.Clamp(count * 4, 64, 1024));

        await using var client = new ReadOnlyModbusTcpClient(
            host,
            port,
            unitId,
            Math.Clamp(timeoutMs, 250, 30_000));

        var processed = 0;

        while (processed < count)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentCount = Math.Min(blockSize, count - processed);
            var currentStart = startAddress + processed;

            await ReadAdaptiveAsync(
                client,
                area,
                currentStart,
                currentCount,
                values,
                budget,
                cancellationToken).ConfigureAwait(false);

            processed += currentCount;
            progress?.Report(new MesModbusScanProgress(processed, count, area));
        }

        return values
            .OrderBy(value => value.Area)
            .ThenBy(value => value.Address)
            .ToList();
    }

    public async Task<IReadOnlyList<MesModbusExplorerValue>> ReadKnownAddressesAsync(
        string host,
        int port,
        byte unitId,
        int timeoutMs,
        IReadOnlyCollection<MesModbusExplorerAddress> addresses,
        CancellationToken cancellationToken)
    {
        var result = new List<MesModbusExplorerValue>();

        await using var client = new ReadOnlyModbusTcpClient(
            host,
            port,
            unitId,
            Math.Clamp(timeoutMs, 250, 30_000));

        foreach (var group in addresses
                     .Distinct()
                     .GroupBy(address => address.Area))
        {
            foreach (var address in group.OrderBy(item => item.Address))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var value = await ReadSingleAsync(
                        client,
                        address.Area,
                        address.Address,
                        cancellationToken).ConfigureAwait(false);
                    result.Add(value);
                }
                catch (Exception ex)
                {
                    result.Add(MesModbusExplorerValue.Unreadable(
                        address.Area,
                        address.Address,
                        ex.Message));
                }
            }
        }

        return result;
    }

    private static async Task ReadAdaptiveAsync(
        ReadOnlyModbusTcpClient client,
        MesModbusArea area,
        int startAddress,
        int count,
        ICollection<MesModbusExplorerValue> output,
        MesModbusRequestBudget budget,
        CancellationToken cancellationToken)
    {
        if (!budget.TryConsume())
        {
            AddUnreadableRange(
                output,
                area,
                startAddress,
                count,
                "Read request budget was exceeded. Reduce the scan range or block size.");
            return;
        }

        try
        {
            var readAt = DateTimeOffset.Now;

            if (area is MesModbusArea.Coil or MesModbusArea.DiscreteInput)
            {
                var bits = area == MesModbusArea.Coil
                    ? await client.ReadCoilsAsync(
                        checked((ushort)startAddress),
                        checked((ushort)count),
                        cancellationToken).ConfigureAwait(false)
                    : await client.ReadDiscreteInputsAsync(
                        checked((ushort)startAddress),
                        checked((ushort)count),
                        cancellationToken).ConfigureAwait(false);

                for (var index = 0; index < bits.Length; index++)
                {
                    output.Add(MesModbusExplorerValue.ReadableBit(
                        area,
                        startAddress + index,
                        bits[index],
                        readAt));
                }

                return;
            }

            var registers = area == MesModbusArea.HoldingRegister
                ? await client.ReadHoldingRegistersAsync(
                    checked((ushort)startAddress),
                    checked((ushort)count),
                    cancellationToken).ConfigureAwait(false)
                : await client.ReadInputRegistersAsync(
                    checked((ushort)startAddress),
                    checked((ushort)count),
                    cancellationToken).ConfigureAwait(false);

            for (var index = 0; index < registers.Length; index++)
            {
                output.Add(MesModbusExplorerValue.ReadableRegister(
                    area,
                    startAddress + index,
                    registers[index],
                    readAt));
            }
        }
        catch (ModbusProtocolException ex)
            when (count > 1 && (ex.ExceptionCode == 0x02 || ex.ExceptionCode == 0x03))
        {
            // A request can cross a boundary between readable and unreadable addresses.
            // Split only protocol address/value errors; never fan out timeouts or network errors.
            var firstCount = count / 2;
            var secondCount = count - firstCount;

            await ReadAdaptiveAsync(
                client,
                area,
                startAddress,
                firstCount,
                output,
                budget,
                cancellationToken).ConfigureAwait(false);

            await ReadAdaptiveAsync(
                client,
                area,
                startAddress + firstCount,
                secondCount,
                output,
                budget,
                cancellationToken).ConfigureAwait(false);
        }
        catch (ModbusProtocolException ex)
        {
            AddUnreadableRange(output, area, startAddress, count, ex.Message);
        }
    }

    private static async Task<MesModbusExplorerValue> ReadSingleAsync(
        ReadOnlyModbusTcpClient client,
        MesModbusArea area,
        int address,
        CancellationToken cancellationToken)
    {
        var readAt = DateTimeOffset.Now;
        var modbusAddress = checked((ushort)address);

        switch (area)
        {
            case MesModbusArea.Coil:
            {
                var values = await client.ReadCoilsAsync(
                    modbusAddress,
                    1,
                    cancellationToken).ConfigureAwait(false);
                return MesModbusExplorerValue.ReadableBit(
                    area,
                    address,
                    values[0],
                    readAt);
            }

            case MesModbusArea.DiscreteInput:
            {
                var values = await client.ReadDiscreteInputsAsync(
                    modbusAddress,
                    1,
                    cancellationToken).ConfigureAwait(false);
                return MesModbusExplorerValue.ReadableBit(
                    area,
                    address,
                    values[0],
                    readAt);
            }

            case MesModbusArea.HoldingRegister:
            {
                var values = await client.ReadHoldingRegistersAsync(
                    modbusAddress,
                    1,
                    cancellationToken).ConfigureAwait(false);
                return MesModbusExplorerValue.ReadableRegister(
                    area,
                    address,
                    values[0],
                    readAt);
            }

            case MesModbusArea.InputRegister:
            {
                var values = await client.ReadInputRegistersAsync(
                    modbusAddress,
                    1,
                    cancellationToken).ConfigureAwait(false);
                return MesModbusExplorerValue.ReadableRegister(
                    area,
                    address,
                    values[0],
                    readAt);
            }

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(area),
                    area,
                    "Unsupported Modbus area.");
        }
    }

    private static void AddUnreadableRange(
        ICollection<MesModbusExplorerValue> output,
        MesModbusArea area,
        int startAddress,
        int count,
        string error)
    {
        for (var index = 0; index < count; index++)
        {
            output.Add(MesModbusExplorerValue.Unreadable(
                area,
                startAddress + index,
                error));
        }
    }

    private static void ValidateRange(int startAddress, int count)
    {
        if (startAddress is < 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startAddress),
                "Modbus start address must be between 0 and 65535.");
        }

        if (count is < 1 or > 2048)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count),
                "Explorer scan count must be between 1 and 2048.");
        }

        if ((long)startAddress + count - 1 > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count),
                "The requested range exceeds Modbus address 65535.");
        }
    }

    private sealed class MesModbusRequestBudget
    {
        private int _remaining;

        public MesModbusRequestBudget(int maximumRequests)
        {
            _remaining = maximumRequests;
        }

        public bool TryConsume()
        {
            if (_remaining <= 0)
            {
                return false;
            }

            _remaining--;
            return true;
        }
    }
}

public sealed record MesModbusConnectionProbe(
    bool Success,
    TimeSpan Elapsed,
    string Message);

public sealed record MesModbusScanProgress(
    int Completed,
    int Total,
    MesModbusArea Area);

public readonly record struct MesModbusExplorerAddress(
    MesModbusArea Area,
    int Address);

public sealed class MesModbusExplorerValue
{
    public MesModbusArea Area { get; init; }

    public int Address { get; init; }

    public bool IsReadable { get; init; }

    public bool? BitValue { get; init; }

    public ushort? RegisterValue { get; init; }

    public string Error { get; init; } = string.Empty;

    public DateTimeOffset ReadAt { get; init; }

    public ulong NumericValue => RegisterValue.HasValue
        ? RegisterValue.Value
        : BitValue == true ? 1UL : 0UL;

    public static MesModbusExplorerValue ReadableBit(
        MesModbusArea area,
        int address,
        bool value,
        DateTimeOffset readAt) =>
        new()
        {
            Area = area,
            Address = address,
            IsReadable = true,
            BitValue = value,
            ReadAt = readAt
        };

    public static MesModbusExplorerValue ReadableRegister(
        MesModbusArea area,
        int address,
        ushort value,
        DateTimeOffset readAt) =>
        new()
        {
            Area = area,
            Address = address,
            IsReadable = true,
            RegisterValue = value,
            ReadAt = readAt
        };

    public static MesModbusExplorerValue Unreadable(
        MesModbusArea area,
        int address,
        string error) =>
        new()
        {
            Area = area,
            Address = address,
            IsReadable = false,
            Error = error,
            ReadAt = DateTimeOffset.Now
        };
}
