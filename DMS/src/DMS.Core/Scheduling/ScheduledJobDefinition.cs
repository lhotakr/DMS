using System;
using System.Collections.Generic;

namespace DMS.Core.Scheduling;

public sealed class ScheduledJobDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;

    public string TemplateId { get; set; } = string.Empty;

    // Legacy v1-v5 compatibility.
    public string ReportCode { get; set; } = string.Empty;

    public string CronExpression { get; set; } = "0 6 * * 1-5";
    public string TimeZoneId { get; set; } = "Europe/Prague";

    public Dictionary<string, string> Parameters { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    public List<ScheduledJobDeliveryDefinition> Deliveries { get; set; }
        = new();

    public string OutputFileNameTemplate { get; set; }
        = "{TemplateName}_{yyyy-MM-dd}_{HHmmss}.{Extension}";
}

public sealed class ScheduledJobDeliveryDefinition
{
    public string Type { get; set; } = "ServerFolder";
    public bool IsEnabled { get; set; } = true;
    public string TargetPath { get; set; } = string.Empty;
    public List<string> Recipients { get; set; } = new();
    public string SubjectTemplate { get; set; }
        = "{TemplateName} - {yyyy-MM-dd}";
}

public sealed class ScheduledJobFile
{
    public int Version { get; set; } = 2;
    public List<ScheduledJobDefinition> Jobs { get; set; } = new();
}
