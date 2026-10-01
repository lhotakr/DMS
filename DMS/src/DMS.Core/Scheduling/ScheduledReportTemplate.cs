using System;
using System.Collections.Generic;

namespace DMS.Core.Scheduling;

public sealed class ScheduledReportTemplate
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Source { get; set; } = "MES06";
    public string ReportCode { get; set; } = string.Empty;
    public string ExecutorCode { get; set; } = string.Empty;

    public string QuickPeriodCode { get; set; } = "YESTERDAY";
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string TimeFrom { get; set; } = string.Empty;
    public string TimeTo { get; set; } = string.Empty;

    public bool SelectAllWorkcenters { get; set; } = true;
    public List<string> WorkcenterCodes { get; set; } = new();

    public string ShiftCode { get; set; } = string.Empty;
    public string Article { get; set; } = string.Empty;
    public string Order { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public string UpdatedBy { get; set; } = string.Empty;

    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(Name)
            ? Id
            : Name;
    }

}

public sealed class ScheduledReportTemplateFile
{
    public int Version { get; set; } = 1;
    public List<ScheduledReportTemplate> Templates { get; set; } = new();
}
