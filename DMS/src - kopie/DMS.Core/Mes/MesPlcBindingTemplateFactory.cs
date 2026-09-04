namespace DMS.Core.Mes;

/// <summary>
/// Creates the standard B&R X20 signal layout used by most MES workplaces.
/// Physical Modbus addresses are deliberately not invented here; confirmed addresses
/// already present in an existing binding are preserved by point code.
/// </summary>
public static class MesPlcBindingTemplateFactory
{
    public static MesPlcBinding CreateStandardBrX20(
        string stationCode,
        MesPlcBinding? existing = null)
    {
        var sources = existing?.DataPoints
            .Where(point => !string.IsNullOrWhiteSpace(point.Code))
            .GroupBy(point => point.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => CloneSource(group.First().Source),
                StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, MesModbusSource?>(StringComparer.OrdinalIgnoreCase);

        var template = new MesPlcBinding
        {
            StationCode = stationCode?.Trim() ?? string.Empty,
            IpAddressOverride = existing?.IpAddressOverride,
            Driver = MesDriverKeys.BrX20ModbusTcp,
            Enabled = existing?.Enabled ?? true,
            Port = existing is { Port: > 0 } ? existing.Port : 502,
            UnitId = existing?.UnitId ?? 0,
            PollIntervalMs = existing is { PollIntervalMs: > 0 }
                ? existing.PollIntervalMs
                : 500,
            TimeoutMs = existing is { TimeoutMs: > 0 }
                ? existing.TimeoutMs
                : 3000,
            StaleAfterSeconds = existing is { StaleAfterSeconds: > 0 }
                ? existing.StaleAfterSeconds
                : 3,
            StopTimeoutSeconds = existing is { StopTimeoutSeconds: > 0 }
                ? existing.StopTimeoutSeconds
                : 30,
            Controller = existing?.Controller?.Trim() is { Length: > 0 } controller
                ? controller
                : "B&R X20BC0087",
            Modules = CreateStandardModules(),
            DataPoints = CreateStandardDataPoints()
        };

        foreach (var point in template.DataPoints)
        {
            if (sources.TryGetValue(point.Code, out var source))
            {
                point.Source = source;
            }
        }

        // Keep any confirmed, station-specific points that are not part of the
        // common template. Applying the template must never silently delete
        // future channels or a local extension.
        var standardCodes = template.DataPoints
            .Select(point => point.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (existing is not null)
        {
            template.DataPoints.AddRange(existing.DataPoints
                .Where(point => !standardCodes.Contains(point.Code))
                .Select(ClonePoint));
        }

        return template;
    }

    public static void SwapCounterMeanings(IList<MesDataPointDefinition> dataPoints)
    {
        var counter1 = dataPoints.FirstOrDefault(point =>
            string.Equals(point.Code, "Counter1", StringComparison.OrdinalIgnoreCase));
        var counter2 = dataPoints.FirstOrDefault(point =>
            string.Equals(point.Code, "Counter2", StringComparison.OrdinalIgnoreCase));

        if (counter1 is null || counter2 is null)
        {
            return;
        }

        (counter1.LogicalSignal, counter2.LogicalSignal) =
            (counter2.LogicalSignal, counter1.LogicalSignal);
        (counter1.DisplayName, counter2.DisplayName) =
            (counter2.DisplayName, counter1.DisplayName);
        (counter1.Enabled, counter2.Enabled) =
            (counter2.Enabled, counter1.Enabled);
        (counter1.VisibleInMes03, counter2.VisibleInMes03) =
            (counter2.VisibleInMes03, counter1.VisibleInMes03);
    }

    public static List<MesModuleDefinition> CreateStandardModules()
    {
        return new List<MesModuleDefinition>
        {
            new()
            {
                Slot = 1,
                Type = "X20DI2377",
                Description = "2-channel counter / digital input module"
            },
            new()
            {
                Slot = 2,
                Type = "X20DI6371",
                Description = "6-channel digital input module"
            },
            new()
            {
                Slot = 3,
                Type = "X20DO6322",
                Description = "6-channel digital output module controlled by MES"
            }
        };
    }

    public static List<MesDataPointDefinition> CreateStandardDataPoints()
    {
        return new List<MesDataPointDefinition>
        {
            Point("Counter1", "GOOD_PIECES", "Dobré kusy", "X20DI2377", 1, 1),
            Point("Counter2", "REJECT_PIECES", "Zmetky", "X20DI2377", 1, 2),
            Point("Input1", "IONIZER_ACTIVE", "Ionizátor", "X20DI6371", 2, 1),
            Point("Input2", "ROBOT_ACTIVE", "Aktivní robot", "X20DI6371", 2, 2),
            Point("Input3", "UV_LAMP_1_ACTIVE", "UV lampa 1", "X20DI6371", 2, 3),
            Point("Input4", "UV_LAMP_2_ACTIVE", "UV lampa 2", "X20DI6371", 2, 4),
            Point("Input5", "BURNER_ACTIVE", "Hořák", "X20DI6371", 2, 5),
            Point("Input6", "UV_LAMP_3_ACTIVE", "UV lampa 3", "X20DI6371", 2, 6),
            Point("Output1", "MACHINE_READY", "Stroj připraven", "X20DO6322", 3, 1),
            Point("Output2", "MACHINE_STOPPED", "Stroj zastaven", "X20DO6322", 3, 2),
            Point("Output3", "MACHINE_RUNNING", "Stroj běží", "X20DO6322", 3, 3),
            Point("Output4", "RESERVE_DO4", "Rezerva DO4", "X20DO6322", 3, 4, false, false),
            Point("Output5", "RESERVE_DO5", "Rezerva DO5", "X20DO6322", 3, 5, false, false),
            Point("Output6", "RESERVE_DO6", "Rezerva DO6", "X20DO6322", 3, 6, false, false)
        };
    }

    private static MesDataPointDefinition Point(
        string code,
        string logicalSignal,
        string displayName,
        string moduleType,
        int slot,
        int channel,
        bool enabled = true,
        bool visibleInMes03 = true)
    {
        return new MesDataPointDefinition
        {
            Code = code,
            LogicalSignal = logicalSignal,
            DisplayName = displayName,
            ModuleType = moduleType,
            Slot = slot,
            Channel = channel,
            Enabled = enabled,
            VisibleInMes03 = visibleInMes03,
            Inverted = false,
            Source = null
        };
    }

    private static MesDataPointDefinition ClonePoint(MesDataPointDefinition point)
    {
        return new MesDataPointDefinition
        {
            Code = point.Code,
            LogicalSignal = point.LogicalSignal,
            DisplayName = point.DisplayName,
            ModuleType = point.ModuleType,
            Slot = point.Slot,
            Channel = point.Channel,
            Enabled = point.Enabled,
            VisibleInMes03 = point.VisibleInMes03,
            Inverted = point.Inverted,
            Source = CloneSource(point.Source)
        };
    }

    private static MesModbusSource? CloneSource(MesModbusSource? source)
    {
        if (source is null)
        {
            return null;
        }

        return new MesModbusSource
        {
            Area = source.Area,
            Address = source.Address,
            DataType = source.DataType,
            BitIndex = source.BitIndex,
            WordOrder = source.WordOrder
        };
    }
}
