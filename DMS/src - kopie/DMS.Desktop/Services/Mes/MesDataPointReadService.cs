using DMS.Core.Mes;
using System.Collections.Concurrent;
using System.Net;

namespace DMS.Desktop.Services.Mes;

public sealed class MesDataPointReadService : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, ReadOnlyModbusTcpClient> _clients =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<MesDeviceSnapshot> ReadSnapshotAsync(
        MesDeviceEntry device,
        MesPlcBinding? binding,
        MesIntegrationSettings settings,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.Now;

        if (binding is null)
        {
            return BuildConfigurationSnapshot(
                device,
                null,
                now,
                "No PLC binding is configured for this station.");
        }

        if (!binding.Enabled)
        {
            return BuildUnsupportedSnapshot(
                device,
                binding,
                now,
                "The PLC binding is disabled.");
        }

        if (!string.Equals(
                binding.Driver,
                MesDriverKeys.BrX20ModbusTcp,
                StringComparison.OrdinalIgnoreCase))
        {
            var message = string.Equals(
                binding.Driver,
                MesDriverKeys.SiemensDeferred,
                StringComparison.OrdinalIgnoreCase)
                ? "Siemens PLC reading is intentionally deferred to a later phase."
                : $"Unsupported MES driver: {binding.Driver}";

            return BuildUnsupportedSnapshot(device, binding, now, message);
        }

        var ipAddress = string.IsNullOrWhiteSpace(binding.IpAddressOverride)
            ? device.IpAddress
            : binding.IpAddressOverride.Trim();

        if (!IPAddress.TryParse(ipAddress, out _))
        {
            return BuildConfigurationSnapshot(
                device,
                binding,
                now,
                $"Invalid PLC IP address: {ipAddress}");
        }

        var configuredPoints = binding.DataPoints
            .Where(point => point.Enabled)
            .ToList();

        var invalidPoints = configuredPoints
            .Where(point => !IsSourceValid(point.Source, out _))
            .ToList();

        var readablePoints = configuredPoints
            .Except(invalidPoints)
            .ToList();

        var values = new List<MesDataPointValue>();

        foreach (var invalidPoint in invalidPoints)
        {
            IsSourceValid(invalidPoint.Source, out var error);
            values.Add(CreateErrorValue(
                invalidPoint,
                MesDataPointQuality.ConfigurationError,
                error,
                now));
        }

        if (readablePoints.Count == 0)
        {
            return new MesDeviceSnapshot
            {
                Device = device,
                Binding = binding,
                IsOnline = false,
                StatusMessage = invalidPoints.Count == 0
                    ? "No enabled data points are configured."
                    : "The B&R station is known, but Modbus addresses are not configured yet.",
                ReadAt = now,
                DataPoints = OrderValues(values)
            };
        }

        var timeoutMs = binding.TimeoutMs > 0
            ? binding.TimeoutMs
            : settings.DefaultTimeoutMs;

        var clientKey = $"{ipAddress}:{binding.Port}:{binding.UnitId}";

        var client = _clients.GetOrAdd(
            clientKey,
            _ => new ReadOnlyModbusTcpClient(
                ipAddress,
                binding.Port,
                binding.UnitId,
                timeoutMs));

        try
        {
            var rawMap = await ReadRawValuesAsync(
                client,
                readablePoints,
                cancellationToken).ConfigureAwait(false);

            foreach (var point in readablePoints)
            {
                try
                {
                    var rawValue = DecodePoint(point, rawMap);
                    var normalizedValue = ApplyInversion(point, rawValue);

                    values.Add(new MesDataPointValue
                    {
                        Code = point.Code,
                        LogicalSignal = point.LogicalSignal,
                        DisplayName = point.DisplayName,
                        ModuleType = point.ModuleType,
                        Slot = point.Slot,
                        Channel = point.Channel,
                        SourceText = BuildSourceText(point.Source),
                        RawValue = normalizedValue,
                        DisplayValue = FormatValue(normalizedValue),
                        Quality = MesDataPointQuality.Valid,
                        ReadAt = now
                    });
                }
                catch (Exception ex)
                {
                    values.Add(CreateErrorValue(
                        point,
                        MesDataPointQuality.Invalid,
                        ex.Message,
                        now));
                }
            }

            return new MesDeviceSnapshot
            {
                Device = device,
                Binding = binding,
                IsOnline = true,
                StatusMessage = "Online",
                ReadAt = now,
                DataPoints = OrderValues(values)
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            RemoveClient(clientKey);

            values.AddRange(readablePoints.Select(point =>
                CreateErrorValue(
                    point,
                    MesDataPointQuality.Offline,
                    $"Communication timeout after {timeoutMs} ms.",
                    now)));

            return BuildOfflineSnapshot(
                device,
                binding,
                now,
                "Communication timeout.",
                values);
        }
        catch (Exception ex)
        {
            RemoveClient(clientKey);

            values.AddRange(readablePoints.Select(point =>
                CreateErrorValue(
                    point,
                    MesDataPointQuality.Offline,
                    ex.Message,
                    now)));

            return BuildOfflineSnapshot(
                device,
                binding,
                now,
                ex.Message,
                values);
        }
    }

    private static async Task<Dictionary<MesRawAddress, object>> ReadRawValuesAsync(
        ReadOnlyModbusTcpClient client,
        IReadOnlyList<MesDataPointDefinition> points,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<MesRawAddress, object>();

        foreach (var areaGroup in points.GroupBy(point => point.Source!.Area))
        {
            if (areaGroup.Key is MesModbusArea.Coil or MesModbusArea.DiscreteInput)
            {
                await ReadBitAreaAsync(
                    client,
                    areaGroup.Key,
                    areaGroup.ToList(),
                    result,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await ReadRegisterAreaAsync(
                    client,
                    areaGroup.Key,
                    areaGroup.ToList(),
                    result,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        return result;
    }

    private static async Task ReadBitAreaAsync(
        ReadOnlyModbusTcpClient client,
        MesModbusArea area,
        IReadOnlyList<MesDataPointDefinition> points,
        IDictionary<MesRawAddress, object> result,
        CancellationToken cancellationToken)
    {
        var addresses = points
            .Select(point => point.Source!.Address!.Value)
            .Distinct()
            .OrderBy(address => address)
            .ToList();

        foreach (var batch in BuildBatches(addresses, maxQuantity: 2000, maxGap: 32))
        {
            var start = checked((ushort)batch.Start);
            var count = checked((ushort)batch.Quantity);

            var values = area == MesModbusArea.Coil
                ? await client.ReadCoilsAsync(start, count, cancellationToken).ConfigureAwait(false)
                : await client.ReadDiscreteInputsAsync(start, count, cancellationToken).ConfigureAwait(false);

            for (var index = 0; index < values.Length; index++)
            {
                result[new MesRawAddress(area, batch.Start + index)] = values[index];
            }
        }
    }

    private static async Task ReadRegisterAreaAsync(
        ReadOnlyModbusTcpClient client,
        MesModbusArea area,
        IReadOnlyList<MesDataPointDefinition> points,
        IDictionary<MesRawAddress, object> result,
        CancellationToken cancellationToken)
    {
        var registerAddresses = new HashSet<int>();

        foreach (var point in points)
        {
            var startAddress = point.Source!.Address!.Value;
            var wordCount = GetWordCount(point.Source.DataType);

            for (var offset = 0; offset < wordCount; offset++)
            {
                registerAddresses.Add(startAddress + offset);
            }
        }

        var addresses = registerAddresses.OrderBy(address => address).ToList();

        foreach (var batch in BuildBatches(addresses, maxQuantity: 125, maxGap: 8))
        {
            var start = checked((ushort)batch.Start);
            var count = checked((ushort)batch.Quantity);

            var values = area == MesModbusArea.HoldingRegister
                ? await client.ReadHoldingRegistersAsync(start, count, cancellationToken).ConfigureAwait(false)
                : await client.ReadInputRegistersAsync(start, count, cancellationToken).ConfigureAwait(false);

            for (var index = 0; index < values.Length; index++)
            {
                result[new MesRawAddress(area, batch.Start + index)] = values[index];
            }
        }
    }

    private static IEnumerable<MesAddressBatch> BuildBatches(
        IReadOnlyList<int> addresses,
        int maxQuantity,
        int maxGap)
    {
        if (addresses.Count == 0)
        {
            yield break;
        }

        var start = addresses[0];
        var end = addresses[0];

        for (var index = 1; index < addresses.Count; index++)
        {
            var address = addresses[index];
            var proposedQuantity = address - start + 1;
            var gap = address - end;

            if (proposedQuantity > maxQuantity || gap > maxGap)
            {
                yield return new MesAddressBatch(start, end - start + 1);
                start = address;
            }

            end = address;
        }

        yield return new MesAddressBatch(start, end - start + 1);
    }

    private static object DecodePoint(
        MesDataPointDefinition point,
        IReadOnlyDictionary<MesRawAddress, object> rawMap)
    {
        var source = point.Source!;
        var address = source.Address!.Value;

        if (source.Area is MesModbusArea.Coil or MesModbusArea.DiscreteInput)
        {
            return (bool)rawMap[new MesRawAddress(source.Area, address)];
        }

        var first = (ushort)rawMap[new MesRawAddress(source.Area, address)];

        if (source.DataType == MesDataType.Bool)
        {
            return source.BitIndex.HasValue
                ? (first & (1 << source.BitIndex.Value)) != 0
                : first != 0;
        }

        if (source.DataType == MesDataType.UInt16)
        {
            return first;
        }

        if (source.DataType == MesDataType.Int16)
        {
            return unchecked((short)first);
        }

        var second = (ushort)rawMap[new MesRawAddress(source.Area, address + 1)];

        var highWord = source.WordOrder == MesWordOrder.BigEndian
            ? first
            : second;
        var lowWord = source.WordOrder == MesWordOrder.BigEndian
            ? second
            : first;

        var raw32 = ((uint)highWord << 16) | lowWord;

        return source.DataType switch
        {
            MesDataType.UInt32 => raw32,
            MesDataType.Int32 => unchecked((int)raw32),
            MesDataType.Float32 => BitConverter.Int32BitsToSingle(unchecked((int)raw32)),
            _ => throw new InvalidOperationException(
                $"Unsupported register data type: {source.DataType}")
        };
    }

    private static object ApplyInversion(
        MesDataPointDefinition point,
        object rawValue)
    {
        if (!point.Inverted || rawValue is not bool booleanValue)
        {
            return rawValue;
        }

        return !booleanValue;
    }

    private static int GetWordCount(MesDataType dataType)
    {
        return dataType is MesDataType.UInt32 or MesDataType.Int32 or MesDataType.Float32
            ? 2
            : 1;
    }

    private static bool IsSourceValid(
        MesModbusSource? source,
        out string error)
    {
        if (source is null)
        {
            error = "Modbus source is not configured.";
            return false;
        }

        if (!source.Address.HasValue)
        {
            error = "Modbus address is not configured.";
            return false;
        }

        if (source.Address.Value is < 0 or > ushort.MaxValue)
        {
            error = $"Modbus address is outside the valid range: {source.Address.Value}.";
            return false;
        }

        var lastAddress = source.Address.Value + GetWordCount(source.DataType) - 1;

        if (lastAddress > ushort.MaxValue)
        {
            error = "The data point exceeds the Modbus address range.";
            return false;
        }

        if (source.BitIndex is < 0 or > 15)
        {
            error = $"Register bit index must be between 0 and 15: {source.BitIndex}.";
            return false;
        }

        if (source.Area is MesModbusArea.Coil or MesModbusArea.DiscreteInput &&
            source.DataType != MesDataType.Bool)
        {
            error = $"Area {source.Area} supports only Bool data points.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static MesDataPointValue CreateErrorValue(
        MesDataPointDefinition point,
        MesDataPointQuality quality,
        string error,
        DateTimeOffset now)
    {
        return new MesDataPointValue
        {
            Code = point.Code,
            LogicalSignal = point.LogicalSignal,
            DisplayName = point.DisplayName,
            ModuleType = point.ModuleType,
            Slot = point.Slot,
            Channel = point.Channel,
            SourceText = BuildSourceText(point.Source),
            DisplayValue = "—",
            Quality = quality,
            Error = error,
            ReadAt = now
        };
    }

    private static MesDeviceSnapshot BuildConfigurationSnapshot(
        MesDeviceEntry device,
        MesPlcBinding? binding,
        DateTimeOffset now,
        string message)
    {
        var points = binding?.DataPoints
            .Where(point => point.Enabled)
            .Select(point => CreateErrorValue(
                point,
                MesDataPointQuality.ConfigurationError,
                message,
                now))
            .ToList()
            ?? new List<MesDataPointValue>();

        return new MesDeviceSnapshot
        {
            Device = device,
            Binding = binding,
            IsOnline = false,
            StatusMessage = message,
            ReadAt = now,
            DataPoints = OrderValues(points)
        };
    }

    private static MesDeviceSnapshot BuildUnsupportedSnapshot(
        MesDeviceEntry device,
        MesPlcBinding binding,
        DateTimeOffset now,
        string message)
    {
        var points = binding.DataPoints
            .Where(point => point.Enabled)
            .Select(point => CreateErrorValue(
                point,
                MesDataPointQuality.Unsupported,
                message,
                now))
            .ToList();

        return new MesDeviceSnapshot
        {
            Device = device,
            Binding = binding,
            IsOnline = false,
            StatusMessage = message,
            ReadAt = now,
            DataPoints = OrderValues(points)
        };
    }

    private static MesDeviceSnapshot BuildOfflineSnapshot(
        MesDeviceEntry device,
        MesPlcBinding binding,
        DateTimeOffset now,
        string message,
        IReadOnlyList<MesDataPointValue> values)
    {
        return new MesDeviceSnapshot
        {
            Device = device,
            Binding = binding,
            IsOnline = false,
            StatusMessage = message,
            ReadAt = now,
            DataPoints = OrderValues(values)
        };
    }

    private static IReadOnlyList<MesDataPointValue> OrderValues(
        IEnumerable<MesDataPointValue> values)
    {
        return values
            .OrderBy(value => value.Slot)
            .ThenBy(value => value.Channel)
            .ThenBy(value => value.Code)
            .ToList();
    }

    private static string BuildSourceText(MesModbusSource? source)
    {
        if (source is null || !source.Address.HasValue)
        {
            return "Not mapped";
        }

        var bitText = source.BitIndex.HasValue
            ? $" bit {source.BitIndex.Value}"
            : string.Empty;

        return $"{source.Area}[{source.Address.Value}]{bitText}";
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => "—",
            bool booleanValue => booleanValue ? "ON" : "OFF",
            float floatValue => floatValue.ToString("0.###"),
            double doubleValue => doubleValue.ToString("0.###"),
            _ => Convert.ToString(value) ?? "—"
        };
    }

    private void RemoveClient(string key)
    {
        if (!_clients.TryRemove(key, out var client))
        {
            return;
        }

        _ = client.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        var clients = _clients.Values.ToList();
        _clients.Clear();

        foreach (var client in clients)
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    private readonly record struct MesRawAddress(
        MesModbusArea Area,
        int Address);

    private readonly record struct MesAddressBatch(
        int Start,
        int Quantity);
}
