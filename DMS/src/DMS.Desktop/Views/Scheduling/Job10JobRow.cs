namespace DMS.Desktop.Views.Scheduling;

public sealed class Job10JobRow
{
    public string Id { get; init; } = string.Empty;
    public bool IsEnabled { get; init; }
    public string Name { get; init; } = string.Empty;
    public string TemplateName { get; init; } = string.Empty;
    public string CronExpression { get; init; } = string.Empty;
}
