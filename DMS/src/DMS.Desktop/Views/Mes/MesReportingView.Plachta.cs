using DMS.Integration.Mes.Reporting;
using DMS.Integration.Mes.Reporting.Definitions;

namespace DMS.Desktop.Views.Mes;

public partial class MesReportingView
{
    private static bool IsPlachtaReport(
        MesReportDefinition? definition) =>
        definition is not null
        && string.Equals(
            definition.Code,
            MesPlachtaReportService.ReportCode,
            StringComparison.OrdinalIgnoreCase);

    private IReadOnlyList<MesReportDefinition> EnsurePlachtaReportDefinition(
        IReadOnlyList<MesReportDefinition> definitions)
    {
        if (definitions.Any(IsPlachtaReport))
        {
            return definitions;
        }

        var result = definitions.ToList();

        result.Add(
            new MesReportDefinition
            {
                Code = MesPlachtaReportService.ReportCode,
                Name = "Plachta",
                NameKey = "MES06.Report.Plachta.Name",
                Description = "Směnová výrobní sestava po stroji, zakázce a operaci včetně času, prostojů, personálu, produkce a odpadu.",
                DescriptionKey = "MES06.Report.Plachta.Description",
                DataSource = "Plachta",
                MaxRows = 50000,
                Chart = null,
                Columns =
                {
                    C("WorkcenterCode", "Stroj", "MES06.Plachta.Column.Workcenter", 100),
                    C("ShiftCode", "Směna", "MES06.Plachta.Column.Shift", 75),
                    C("BaanNumber", "Baan číslo", "MES06.Plachta.Column.BaanNumber", 155),
                    C("SapNumber", "SAP číslo", "MES06.Plachta.Column.SapNumber", 120),
                    C("OrderCode", "Číslo zakázky", "MES06.Plachta.Column.Order", 125),
                    C("OrderQuantity", "Velikost zakázky", "MES06.Plachta.Column.OrderQuantity", 125, "N0"),
                    C("OperationCode", "Průchod / operace", "MES06.Plachta.Column.Operation", 130),
                    C("PlannedPerformance", "Plánovaný výkon", "MES06.Plachta.Column.PlannedPerformance", 180),
                    C("SetupMark", "Přestavba", "MES06.Plachta.Column.Setup", 95),
                    C("ReplacementMachineMark", "Náhradní stroj", "MES06.Plachta.Column.ReplacementMachine", 115),
                    C("PersonnelCount", "Personál", "MES06.Plachta.Column.Personnel", 85, "N0"),
                    C("ProductionTimeHours", "Výrobní čas [h]", "MES06.Plachta.Column.ProductionTime", 120, "N2"),
                    C("DowntimeHours", "Prostoje [h]", "MES06.Plachta.Column.Downtime", 105, "N2"),
                    C("PrintedGross", "Natištěno hrubého", "MES06.Plachta.Column.PrintedGross", 130, "N0"),
                    C("PrintedNet", "Natištěno čistého", "MES06.Plachta.Column.PrintedNet", 130, "N0"),
                    C("TotalScrap", "Celkový odpad", "MES06.Plachta.Column.TotalScrap", 115, "N0"),
                    C("ScrapPercent", "% odpadu", "MES06.Plachta.Column.ScrapPercent", 95, "N2"),
                    C("Notes", "Poznámky", "MES06.Plachta.Column.Notes", 220)
                }
            });

        return result;
    }

    private static MesReportColumnDefinition C(
        string property,
        string header,
        string headerKey,
        double width,
        string format = "") =>
        new()
        {
            Property = property,
            Header = header,
            HeaderKey = headerKey,
            Width = width,
            Format = format
        };
}
