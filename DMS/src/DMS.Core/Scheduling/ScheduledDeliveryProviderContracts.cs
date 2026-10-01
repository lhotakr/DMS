using System;
using System.Threading;
using System.Threading.Tasks;

namespace DMS.Core.Scheduling;

public sealed class ScheduledDeliveryContext
{
    public required ScheduledJobDefinition Job { get; init; }
    public ScheduledReportTemplate? Template { get; init; }
    public required GeneratedReport Report { get; init; }
    public required ScheduledDeliveryDefinition Delivery { get; init; }
    public required DateTime ScheduledAtLocal { get; init; }
}

public interface IScheduledDeliveryProvider
{
    string Type { get; }

    Task<string> DeliverAsync(
        ScheduledDeliveryContext context,
        CancellationToken cancellationToken = default);
}
