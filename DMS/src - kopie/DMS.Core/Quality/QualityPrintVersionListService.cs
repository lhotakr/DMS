namespace DMS.Core.Quality;

public sealed class QualityPrintVersionListService
{
    private readonly IReadOnlyList<QualityPrintVersion> _printVersions;

    public QualityPrintVersionListService(IReadOnlyList<QualityPrintVersion> printVersions)
    {
        _printVersions = printVersions;
    }

    public IReadOnlyList<QualityPrintVersionListRow> BuildRows()
    {
        return _printVersions
            .Select(printVersion => new QualityPrintVersionListRow
            {
                TasksCompleted = AreTasksCompleted(printVersion),
                SapMaterialNumber = printVersion.SapMaterialNumber,
                FullPrintVersionNumber = printVersion.FullPrintVersionNumber,
                Title = printVersion.Title,
                Decoration = printVersion.DecorationCode,
                Customer = printVersion.Customer,
                ColorType = printVersion.ColorType,
                SortKey = FirstNotEmpty(
                    printVersion.FullPrintVersionNumber,
                    printVersion.SapMaterialNumber)
            })
            .OrderByDescending(item => item.SortKey)
            .ToList();
    }

    private static bool AreTasksCompleted(QualityPrintVersion printVersion)
    {
        var realTasks = printVersion.Tasks
            .Where(task => !string.IsNullOrWhiteSpace(task.Text))
            .ToList();

        if (realTasks.Count == 0)
        {
            return true;
        }

        return realTasks.All(task => task.CompletedAt.HasValue);
    }
    private static string FirstNotEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }
}