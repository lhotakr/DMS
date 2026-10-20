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

    // Snapshot of the MES06 report layout at the moment the preset is
    // published for automation. Scheduler runs under a different process /
    // service account and must not depend on the Desktop user's LocalAppData.
    public List<ScheduledReportColumnLayout> Columns { get; set; } = new();

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public string UpdatedBy { get; set; } = string.Empty;

    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(Name)
            ? Id
            : Name;
    }

}

public sealed class ScheduledReportColumnLayout
{
    public string Key { get; set; } = string.Empty;
    public string Header { get; set; } = string.Empty;
    public bool Visible { get; set; } = true;
    public int Order { get; set; }
    public double Width { get; set; } = 120d;
    public int? DecimalPlaces { get; set; }
    public bool Bold { get; set; }
    public bool Underline { get; set; }
    public string Alignment { get; set; } = string.Empty;
}
public sealed class ScheduledReportTemplateFile
{
    public int Version { get; set; } = 1;
    public List<ScheduledReportTemplate> Templates { get; set; } = new();
}
