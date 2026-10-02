using DMS.Integration.Mes.Database;
using System.Threading;
using System.Threading.Tasks;

namespace DMS.Integration.Mes.Reporting.Automation;

/// <summary>
/// Scheduler/JOB10 provider for the MES06 Plachta report.
/// The column order mirrors the interactive DMS report.
/// </summary>
public sealed class Mes06PlachtaAutomationProvider
    : IMes06AutomationReportProvider
{
    private readonly MesPlachtaReportService _service;

    public Mes06PlachtaAutomationProvider(
        MesDatabaseConnectionSettings settings,
        string? sapMaterialsFilePath = null)
    {
        _service =
            new MesPlachtaReportService(
                settings,
                sapMaterialsFilePath:
                    sapMaterialsFilePath);
    }

    public bool CanHandle(
        string reportCode) =>
        string.Equals(
            reportCode,
            MesPlachtaReportService.ReportCode,
            StringComparison.OrdinalIgnoreCase);

    public async Task<Mes06AutomationResult> GenerateAsync(
        Mes06AutomationRequest request,
        CancellationToken cancellationToken = default)
    {
        var rows =
            await _service.GetReportAsync(
                request.From,
                request.To,
                request.WorkcenterCodes,
                request.ShiftCode,
                request.Order,
                request.Operation,
                request.Article,
                request.MaxRows,
                cancellationToken);

        var columns =
            new[]
            {
                C("WorkcenterCode", "Stroj"),
                C("ShiftCode", "Směna"),
                C("BaanNumber", "Baan číslo"),
                C("SapNumber", "SAP číslo"),
                C("ProductDescription", "Popis artiklu"),
                C("OrderCode", "Číslo zakázky"),
                C("OrderQuantity", "Velikost zakázky"),
                C("OperationCode", "Průchod / operace"),
                C("PlannedPerformance", "Plánovaný výkon"),
                C("SetupMark", "Přestavba"),
                C("ReplacementMachineMark", "Náhradní stroj"),
                C("PersonnelCount", "Personál"),
                C("ProductionTimeHours", "Výrobní čas"),
                C("DowntimeHours", "Prostoje"),
                C("PrintedGross", "Natištěno hrubého"),
                C("PrintedNet", "Natištěno čistého"),
                C("TotalScrap", "Celkový odpad"),
                C("ScrapPercent", "% odpadu"),
                C("Notes", "Poznámky")
            };

        var data =
            rows
                .Select(row =>
                    (IReadOnlyDictionary<string, object?>)
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["WorkcenterCode"] = row.WorkcenterCode,
                        ["ShiftCode"] = row.ShiftCode,
                        ["BaanNumber"] = row.BaanNumber,
                        ["SapNumber"] = row.SapNumber,
                        ["ProductDescription"] = row.ProductDescription,
                        ["OrderCode"] = row.OrderCode,
                        ["OrderQuantity"] = row.OrderQuantity,
                        ["OperationCode"] = row.OperationCode,
                        ["PlannedPerformance"] = row.PlannedPerformance,
                        ["SetupMark"] = row.SetupMark,
                        ["ReplacementMachineMark"] = row.ReplacementMachineMark,
                        ["PersonnelCount"] = row.PersonnelCount,
                        ["ProductionTimeHours"] = row.ProductionTimeHours,
                        ["DowntimeHours"] = row.DowntimeHours,
                        ["PrintedGross"] = row.PrintedGross,
                        ["PrintedNet"] = row.PrintedNet,
                        ["TotalScrap"] = row.TotalScrap,
                        ["ScrapPercent"] = row.ScrapPercent,
                        ["Notes"] = row.Notes
                    })
                .ToArray();

        return new Mes06AutomationResult
        {
            ReportCode =
                MesPlachtaReportService.ReportCode,
            Columns =
                columns,
            Rows =
                data
        };
    }

    private static Mes06AutomationColumn C(
        string key,
        string header) =>
        new()
        {
            Key = key,
            Header = header
        };
}
