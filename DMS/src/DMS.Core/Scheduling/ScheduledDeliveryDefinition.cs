using System;
using System.Collections.Generic;

namespace DMS.Core.Scheduling;

public sealed class ScheduledDeliveryDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, string> Parameters { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);
    public string Type { get; set; } = "ServerFolder";
    public bool IsEnabled { get; set; } = true;
    public string TargetPath { get; set; } = string.Empty;
    public List<string> Recipients { get; set; } = new();
    public string SubjectTemplate { get; set; } = "{ReportCode} - {yyyy-MM-dd}";
    public string FileNameTemplate { get; set; }
        = "{ReportCode}_{yyyy-MM-dd}_{HHmmss}.{Extension}";

    // Email
    public List<string> Cc { get; set; } = new();
    public List<string> Bcc { get; set; } = new();
    public string BodyTemplate { get; set; } = string.Empty;
    public bool AttachReport { get; set; } = true;

    // SharePoint
    public string DocumentLibrary { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public bool Overwrite { get; set; } = true;


}

public static class ScheduledDeliveryTypes
{
    public const string ServerFolder = "SERVER_FOLDER";
    public const string Email = "EMAIL";
    public const string SharePoint = "SHAREPOINT";
}
