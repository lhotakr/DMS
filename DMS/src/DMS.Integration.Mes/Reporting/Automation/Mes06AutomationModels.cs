using System;
using System.Collections.Generic;

namespace DMS.Integration.Mes.Reporting.Automation;

public sealed class Mes06AutomationRequest
{
    public string ReportCode { get; init; } = string.Empty;
    public DateTime From { get; init; }
    public DateTime To { get; init; }
    public IReadOnlyList<string> WorkcenterCodes { get; init; } = Array.Empty<string>();
    public string ShiftCode { get; init; } = string.Empty;
    public string Article { get; init; } = string.Empty;
    public string Order { get; init; } = string.Empty;
    public string Operation { get; init; } = string.Empty;
    public int MaxRows { get; init; } = 50000;
}

public sealed class Mes06AutomationColumn
{
    public string Key { get; init; } = string.Empty;
    public string Header { get; init; } = string.Empty;
}

public sealed class Mes06AutomationResult
{
    public string ReportCode { get; init; } = string.Empty;
    public IReadOnlyList<Mes06AutomationColumn> Columns { get; init; } = Array.Empty<Mes06AutomationColumn>();
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows { get; init; } =
        Array.Empty<IReadOnlyDictionary<string, object?>>();
}
