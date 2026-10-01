using DMS.Core.Scheduling;
using System;
using System.Linq;

namespace DMS.Desktop.Views.Scheduling;

public sealed class Job10DeliveryRow
{
    public required ScheduledJobDeliveryDefinition Definition { get; init; }

    public bool IsEnabled => Definition.IsEnabled;

    public string DisplayName =>
        Definition.Type switch
        {
            "ServerFolder" => "Server folder",
            "Email" => "E-mail",
            "SharePoint" => "SharePoint",
            _ => Definition.Type
        };

    public string Summary =>
        Definition.Type switch
        {
            "ServerFolder" => Definition.TargetPath,
            "Email" => Definition.Recipients.Count == 0
                ? "Bez příjemců"
                : string.Join("; ", Definition.Recipients.Take(2))
                  + (Definition.Recipients.Count > 2 ? " …" : string.Empty),
            "SharePoint" => Definition.TargetPath,
            _ => string.Empty
        };
}
