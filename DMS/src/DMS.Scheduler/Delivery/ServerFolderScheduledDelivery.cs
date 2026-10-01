using DMS.Core.Scheduling;

namespace DMS.Scheduler.Delivery;

public sealed class ServerFolderScheduledDelivery : IScheduledDeliveryProvider
{
    private readonly Action<string>? _log;

    public ServerFolderScheduledDelivery(Action<string>? log = null)
    {
        _log = log;
    }

    public string Type => ScheduledDeliveryTypes.ServerFolder;

    public Task<string> DeliverAsync(
        ScheduledDeliveryContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var targetPath = Get(context, "targetPath");
        if (string.IsNullOrWhiteSpace(targetPath))
            throw new InvalidOperationException("Server folder delivery does not contain targetPath.");

        Directory.CreateDirectory(targetPath);

        var fileName =
            DeliveryTokenRenderer.Render(
                Get(
                    context,
                    "fileName",
                    DeliveryTokenRenderer.DefaultFileName),
                context.Job,
                context.Template,
                context.Report,
                context.ScheduledAtLocal);
        var fullPath = Path.Combine(targetPath, fileName);

        _log?.Invoke($"FILE_START Delivery={context.Delivery.Name}; File={fullPath}");
        File.WriteAllBytes(fullPath, context.Report.Content);
        _log?.Invoke($"FILE_OK Delivery={context.Delivery.Name}; File={fullPath}");

        return Task.FromResult(fullPath);
    }

    private static string Get(ScheduledDeliveryContext c, string key, string fallback = "") =>
        c.Delivery.Parameters.TryGetValue(key, out var value) ? value : fallback;
}
