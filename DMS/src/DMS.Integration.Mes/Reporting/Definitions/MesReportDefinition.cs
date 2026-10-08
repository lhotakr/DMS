namespace DMS.Integration.Mes.Reporting.Definitions;

public sealed class MesReportDefinition
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string NameKey { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string DescriptionKey { get; set; } = string.Empty;

    public string DataSource { get; set; } = "Production";

    public int MaxRows { get; set; } = 5000;

    public MesChartDefinition? Chart { get; set; }


    public MesReportBehaviorDefinition Behavior { get; set; } = new();
    public List<MesReportColumnDefinition> Columns { get; set; } = new();

    public string DisplayText => Name;

    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(Name)
            ? Code
            : Name;
    }
}

public sealed class MesReportColumnDefinition
{
    public string Property { get; set; } = string.Empty;

    public string Header { get; set; } = string.Empty;

    public string HeaderKey { get; set; } = string.Empty;

    public double Width { get; set; } = 120d;

    public string Format { get; set; } = string.Empty;
}

public sealed class MesReportBehaviorDefinition
{
    public MesReportPrintBehaviorDefinition Print { get; set; } = new();
}

public sealed class MesReportPrintBehaviorDefinition
{
    public string Orientation { get; set; } = "Landscape";

    public bool RepeatColumnHeaders { get; set; } = true;

    // 0 = no hard row cap; measured page height decides the split.
    public int MaxRowsPerPage { get; set; }

    public bool UseMeasuredPagination { get; set; } = true;

    // Space reserved on page 1 for MES06 title/filter summary.
    public double FirstPageReservedHeight { get; set; } = 90d;

    // Safety reserve against printer/PDF renderer differences.
    public double PageSafetyMargin { get; set; } = 24d;

    public double HeaderMinimumHeight { get; set; } = 22d;
}
public sealed class MesChartDefinition
{
    public string Kind { get; set; } = "Column";

    public string GroupBy { get; set; } = string.Empty;

    public string Measure { get; set; } = string.Empty;

    public string Aggregation { get; set; } = "Sum";

    public string Title { get; set; } = string.Empty;

    public string TitleKey { get; set; } = string.Empty;

    public int Top { get; set; } = 12;
}
