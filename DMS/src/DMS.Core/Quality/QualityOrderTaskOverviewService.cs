namespace DMS.Core.Quality;

public sealed class QualityOrderTaskOverviewService
{
    private readonly IReadOnlyList<QualityOrder> _orders;

    public QualityOrderTaskOverviewService(IReadOnlyList<QualityOrder> orders)
    {
        _orders = orders ?? Array.Empty<QualityOrder>();
    }

    public IReadOnlyList<QualityOrderTaskCockpitRow> BuildRows()
    {
        return _orders
            .SelectMany(order => (order.Tasks ?? Array.Empty<QualityTask>())
                .Where(task => !string.IsNullOrWhiteSpace(task.Text))
                .Select(task => new QualityOrderTaskCockpitRow
                {
                    OrderNumber = order.OrderNumber,
                    SapMaterialNumber = order.SapMaterialNumber,
                    PrintVersionNumber = order.PrintVersionNumber,
                    TaskNumber = task.Number,
                    TaskText = task.Text,
                    CreatedAt = task.CreatedAt,
                    CreatedBy = task.CreatedBy,
                    DueDate = task.DueDate,
                    CompletedAt = task.CompletedAt,
                    CompletedBy = task.CompletedBy
                }))
            .OrderBy(item => item.IsCompleted)
            .ThenBy(item => item.OrderNumber, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.TaskNumber)
            .ToList();
    }
}
