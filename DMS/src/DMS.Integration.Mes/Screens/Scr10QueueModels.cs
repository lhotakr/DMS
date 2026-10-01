using System;
using System.Collections.Generic;
using System.Linq;

namespace DMS.Integration.Mes.Screens
{
    public enum Scr10QueueMode
    {
        Current,
        Planned
    }

    public sealed class Scr10MesOrderRow
    {
        public string WorkcenterCode { get; init; } = string.Empty;
        public string OrderCode { get; init; } = string.Empty;
        public string ProductCode { get; init; } = string.Empty;
        public string SapNumber { get; init; } = string.Empty;
        public decimal OrderQuantity { get; init; }
        public decimal ProducedQuantity { get; init; }
        public string OperationCode { get; init; } = string.Empty;
        public string StatusCode { get; init; } = string.Empty;
        public int QueuePosition { get; init; }
    }

    public sealed class Scr10MachineState
    {
        public string WorkcenterCode { get; init; } = string.Empty;
        public string StateText { get; init; } = string.Empty;
        public string StateCategory { get; init; } = "Unknown";
        public string StateColor { get; init; } = "#9E9E9E";
        public DateTime? StateSince { get; init; }
    }

    public sealed class Scr10WorkcenterGroup
    {
        public string Code { get; init; } = string.Empty;
        public string DisplayName =>
            string.IsNullOrWhiteSpace(Code)
                ? "Všechny skupiny"
                : Code;

        public IReadOnlyList<string> WorkcenterCodes { get; init; }
            = Array.Empty<string>();
    }

    public sealed class Scr10QueueRecord
    {
        public string WorkcenterCode { get; init; } = string.Empty;
        public string WorkcenterState { get; init; } = string.Empty;
        public string WorkcenterStateCategory { get; init; } = "Unknown";
        public string WorkcenterStateColor { get; init; } = "#9E9E9E";

        public string OrderCode { get; init; } = string.Empty;
        public string ProductCode { get; init; } = string.Empty;
        public string SapNumber { get; init; } = string.Empty;

        public decimal OrderQuantity { get; init; }
        public decimal ProducedQuantity { get; init; }

        public decimal RemainingQuantity =>
            Math.Max(0m, OrderQuantity - ProducedQuantity);

        public string OperationCode { get; init; } = string.Empty;
        public string ScreenText { get; init; } = string.Empty;

        public int? ScreensNeeded =>
            string.IsNullOrWhiteSpace(ScreenText)
                ? null
                : (int)Math.Ceiling(RemainingQuantity / 10000m);

        public string StatusCode { get; init; } = string.Empty;
        public int QueuePosition { get; init; }
    }

    public sealed class Scr10QueueSnapshot
    {
        public IReadOnlyList<Scr10QueueRecord> Rows { get; init; }
            = Array.Empty<Scr10QueueRecord>();

        public DateTime LoadedAt { get; init; } = DateTime.Now;

        public int WorkcenterCount =>
            Rows.Select(x => x.WorkcenterCode)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

        public string Fingerprint =>
            string.Join("|", Rows.Select(x =>
                $"{x.WorkcenterCode};{x.OrderCode};{x.OperationCode};" +
                $"{x.ProducedQuantity};{x.StatusCode};{x.WorkcenterState};" +
                $"{x.ScreenText};{x.QueuePosition}"));
    }
}