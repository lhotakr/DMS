using DMS.Core.Sap;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DMS.Integration.Mes.Screens
{
    public sealed class Scr10PreparationQueueService
    {
        private readonly Scr10QueueDataService _mesData;
        private readonly SapScreenBomResolver _screenResolver;

        public Scr10PreparationQueueService(
            Scr10QueueDataService mesData,
            SapScreenBomResolver screenResolver)
        {
            _mesData = mesData
                ?? throw new ArgumentNullException(nameof(mesData));

            _screenResolver = screenResolver
                ?? throw new ArgumentNullException(nameof(screenResolver));
        }

        public Task<IReadOnlyList<Scr10WorkcenterGroup>>
            GetWorkcenterGroupsAsync(
                CancellationToken cancellationToken = default) =>
            _mesData.GetWorkcenterGroupsAsync(cancellationToken);

        public async Task<Scr10QueueSnapshot> GetAsync(
            Scr10QueueMode mode,
            CancellationToken cancellationToken = default)
        {
            var mesRows =
                await _mesData.GetOrdersAsync(
                    mode,
                    cancellationToken);

            var workcenters = mesRows
                .Select(x => x.WorkcenterCode)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var states =
                await _mesData.GetCurrentStatesAsync(
                    workcenters,
                    cancellationToken);

            var screenTexts =
                await LoadScreenTextsAsync(
                    mesRows,
                    cancellationToken);

            // One production-order row can have several text items (screens)
            // in BOM alternative 01. Each screen becomes its own SCR10 row.
            var rows =
                mesRows
                    .SelectMany(mes =>
                    {
                        screenTexts.TryGetValue(
                            mes.SapNumber,
                            out var screens);

                        if (screens == null ||
                            screens.Count == 0)
                        {
                            return new[]
                            {
                                BuildRecord(
                                    mes,
                                    states,
                                    string.Empty)
                            };
                        }

                        return screens
                            .Select(screen =>
                                BuildRecord(
                                    mes,
                                    states,
                                    screen))
                            .ToArray();
                    })
                    .ToList();

            IEnumerable<Scr10QueueRecord> sorted =
                mode == Scr10QueueMode.Current
                    ? rows
                        .OrderBy(
                            x => x.WorkcenterCode,
                            StringComparer.OrdinalIgnoreCase)
                        .ThenBy(
                            x => x.OperationCode,
                            StringComparer.OrdinalIgnoreCase)
                        .ThenBy(
                            x => x.ScreenText,
                            StringComparer.CurrentCultureIgnoreCase)
                    : rows
                        .OrderBy(
                            x => x.WorkcenterCode,
                            StringComparer.OrdinalIgnoreCase)
                        .ThenBy(x => x.QueuePosition)
                        .ThenBy(
                            x => x.OperationCode,
                            StringComparer.OrdinalIgnoreCase)
                        .ThenBy(
                            x => x.ScreenText,
                            StringComparer.CurrentCultureIgnoreCase);

            return new Scr10QueueSnapshot
            {
                Rows = sorted.ToArray(),
                LoadedAt = DateTime.Now
            };
        }

        private async Task<
            IReadOnlyDictionary<string, IReadOnlyList<string>>>
            LoadScreenTextsAsync(
                IReadOnlyCollection<Scr10MesOrderRow> rows,
                CancellationToken cancellationToken)
        {
            var result =
                new Dictionary<string, IReadOnlyList<string>>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (var sapNumber in rows
                         .Select(x => x.SapNumber)
                         .Where(x => !string.IsNullOrWhiteSpace(x))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    result[sapNumber] =
                        await _screenResolver
                            .ResolveScreenTextsAsync(
                                sapNumber,
                                cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    result[sapNumber] =
                        Array.Empty<string>();
                }
            }

            return result;
        }

        private static Scr10QueueRecord BuildRecord(
            Scr10MesOrderRow mes,
            IReadOnlyDictionary<string, Scr10MachineState> states,
            string screenText)
        {
            states.TryGetValue(
                mes.WorkcenterCode,
                out var machineState);

            return new Scr10QueueRecord
            {
                WorkcenterCode = mes.WorkcenterCode,

                WorkcenterState =
                    machineState?.StateText
                    ?? string.Empty,

                WorkcenterStateCategory =
                    machineState?.StateCategory
                    ?? "Unknown",

                WorkcenterStateColor =
                    machineState?.StateColor
                    ?? "#9E9E9E",

                OrderCode = mes.OrderCode,
                ProductCode = mes.ProductCode,
                SapNumber = mes.SapNumber,
                OrderQuantity = mes.OrderQuantity,
                ProducedQuantity = mes.ProducedQuantity,
                OperationCode = mes.OperationCode,

                ScreenText =
                    screenText
                    ?? string.Empty,

                StatusCode = mes.StatusCode,
                QueuePosition = mes.QueuePosition
            };
        }
    }
}