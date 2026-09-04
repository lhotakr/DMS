using System.Text.Json;

namespace DMS.Integration.Mes.Reporting.Definitions;

public sealed class MesReportDefinitionService
{
    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

    public IReadOnlyList<MesReportDefinition> Load(
        string path)
    {
        if (!string.IsNullOrWhiteSpace(path) &&
            File.Exists(path))
        {
            try
            {
                var definitions =
                    JsonSerializer.Deserialize<List<MesReportDefinition>>(
                        File.ReadAllText(path),
                        JsonOptions);

                if (definitions is { Count: > 0 })
                {
                    Normalize(definitions);
                    return definitions;
                }
            }
            catch
            {
                // Fall through to safe built-in definitions.
            }
        }

        return CreateDefaults();
    }

    public IReadOnlyList<MesReportDefinition> CreateDefaults()
    {
        var result =
            new List<MesReportDefinition>
            {
                new()
                {
                    Code = "PRODUCTION",
                    Name = "Výrobní intervaly",
                    NameKey = "MES06.Report.Production.Name",
                    Description =
                        "Read-only detail výrobních intervalů FASTEC. Hodnoty nejsou v tomto pohledu agregovány.",
                    DescriptionKey = "MES06.Report.Production.Description",
                    DataSource = "Production",
                    MaxRows = 5000,
                    Columns =
                    {
                        C("Starttime", "Od", "MES06.Column.From", 145, "dd.MM.yyyy HH:mm:ss"),
                        C("Endtime", "Do", "MES06.Column.To", 145, "dd.MM.yyyy HH:mm:ss"),
                        C("WorkcenterCode", "Pracoviště", "MES06.Column.Workcenter", 105),
                        C("OrderCode", "Zakázka", "MES06.Column.Order", 100),
                        C("OperationCode", "Operace", "MES06.Column.Operation", 80),
                        C("ProductCode", "Artikl", "MES06.Column.Product", 150),
                        C("ProductDescription", "Popis artiklu", "MES06.Column.ProductDescription", 260),
                        C("OrderQuantity", "Množství zakázky", "MES06.Column.OrderQuantity", 120, "0.###"),
                        C("PerformanceTotal", "Celkem", "MES06.Column.Total", 95, "0.###"),
                        C("PerformanceGood", "OK", "MES06.Column.Good", 95, "0.###"),
                        C("PerformanceBad", "NOK", "MES06.Column.Bad", 95, "0.###"),
                        C("PerformanceRework", "Přepracování", "MES06.Column.Rework", 110, "0.###"),
                        C("DurationUtilization", "Využití [s]", "MES06.Column.UtilizationSeconds", 110, "0.###"),
                        C("DurationDown", "Prostoj [s]", "MES06.Column.DownSeconds", 110, "0.###")
                    }
                },
                new()
                {
                    Code = "STATES",
                    Name = "Stavy a prostoje",
                    NameKey = "MES06.Report.States.Name",
                    Description =
                        "Timeline stavů pracovišť. Graf sčítá délku stavů podle kategorie v zadaném období.",
                    DescriptionKey = "MES06.Report.States.Description",
                    DataSource = "States",
                    MaxRows = 10000,
                    Chart = new MesChartDefinition
                    {
                        Kind = "Column",
                        GroupBy = "CategoryName",
                        Measure = "DurationMinutes",
                        Aggregation = "Sum",
                        Title = "Délka stavů podle kategorie [min]",
                        TitleKey = "MES06.Report.States.ChartTitle",
                        Top = 12
                    },
                    Columns =
                    {
                        C("Starttime", "Od", "MES06.Column.From", 145, "dd.MM.yyyy HH:mm:ss"),
                        C("Endtime", "Do", "MES06.Column.To", 145, "dd.MM.yyyy HH:mm:ss"),
                        C("WorkcenterCode", "Pracoviště", "MES06.Column.Workcenter", 105),
                        C("OrderCode", "Zakázka", "MES06.Column.Order", 100),
                        C("ProductCode", "Artikl", "MES06.Column.Product", 150),
                        C("StateName", "Stav", "MES06.Column.State", 210),
                        C("CategoryName", "Kategorie", "MES06.Column.Category", 180),
                        C("DurationMinutes", "Délka [min]", "MES06.Column.DurationMinutes", 105, "0.00"),
                        C("CustomText", "Poznámka", "MES06.Column.Comment", 240)
                    }
                },
                new()
                {
                    Code = "COUNTERS",
                    Name = "Události čítačů",
                    NameKey = "MES06.Report.Counters.Name",
                    Description =
                        "Surové čítačové události. Záměrně bez SUM grafu: před agregací je nutné potvrdit semantiku resetů a korekcí jednotlivých čítačů.",
                    DescriptionKey = "MES06.Report.Counters.Description",
                    DataSource = "Counters",
                    MaxRows = 10000,
                    Columns =
                    {
                        C("Timestamp", "Čas", "MES06.Column.Timestamp", 145, "dd.MM.yyyy HH:mm:ss"),
                        C("WorkcenterCode", "Pracoviště", "MES06.Column.Workcenter", 105),
                        C("OrderCode", "Zakázka", "MES06.Column.Order", 100),
                        C("ProductCode", "Artikl", "MES06.Column.Product", 150),
                        C("CounterName", "Čítač", "MES06.Column.Counter", 200),
                        C("CounterDescription", "Popis", "MES06.Column.Description", 260),
                        C("Value", "Hodnota", "MES06.Column.Value", 110, "0.###"),
                        C("CustomText", "Poznámka / korekce", "MES06.Column.CommentCorrection", 260)
                    }
                }
            };

        Normalize(result);
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

    private static void Normalize(
        IEnumerable<MesReportDefinition> definitions)
    {
        foreach (var definition in definitions)
        {
            definition.Code =
                definition.Code?.Trim() ?? string.Empty;

            definition.Name =
                string.IsNullOrWhiteSpace(definition.Name)
                    ? definition.Code
                    : definition.Name.Trim();

            definition.Description =
                definition.Description?.Trim() ?? string.Empty;

            definition.DataSource =
                string.IsNullOrWhiteSpace(definition.DataSource)
                    ? "Production"
                    : definition.DataSource.Trim();

            definition.MaxRows =
                Math.Clamp(
                    definition.MaxRows,
                    100,
                    50000);

            definition.Columns ??= new List<MesReportColumnDefinition>();

            if (definition.Chart is not null)
            {
                definition.Chart.Top =
                    Math.Clamp(
                        definition.Chart.Top,
                        1,
                        50);
            }
        }
    }
}
