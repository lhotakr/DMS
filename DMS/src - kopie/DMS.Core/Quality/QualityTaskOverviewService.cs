using DMS.Core.Sap;

namespace DMS.Core.Quality;

public sealed class QualityTaskOverviewService
{
    private static readonly DateTime DefaultCreatedAt =
        new DateTime(2026, 1, 1);

    private const string DefaultImportUser = "import";

    private readonly IReadOnlyList<SapMaterial> _sapMaterials;
    private readonly IReadOnlyList<QualityPrintVersion> _printVersions;

    public QualityTaskOverviewService(
        IReadOnlyList<SapMaterial> sapMaterials,
        IReadOnlyList<QualityPrintVersion> printVersions)
    {
        _sapMaterials = sapMaterials;
        _printVersions = printVersions;
    }

    public IReadOnlyList<QualityTaskCockpitRow> BuildRows()
    {
        var result = new List<QualityTaskCockpitRow>();

        foreach (var printVersion in _printVersions)
        {
            var legacyArticleNumber =
                ResolveLegacyArticleNumber(printVersion);

            var sapMaterial = ResolveSapMaterial(printVersion, legacyArticleNumber);

            var sapMaterialNumber = NormalizeSapNumber(printVersion.SapMaterialNumber);

            if (string.IsNullOrWhiteSpace(sapMaterialNumber))
            {
                sapMaterialNumber =
                    NormalizeSapNumber(
                        sapMaterial?.MaterialNumber);
            }

            var displayMaterialNumber =
                !string.IsNullOrWhiteSpace(sapMaterialNumber)
                    ? sapMaterialNumber
                    : $"Baan: {legacyArticleNumber}";

            var materialStatus =
                sapMaterial?.MaterialStatus
                ?? string.Empty;

            foreach (var task in printVersion.Tasks
                         .Where(item =>
                             !string.IsNullOrWhiteSpace(item.Text)))
            {
                var completedAt =
                    task.CompletedAt;

                var createdAt =
                    task.CreatedAt ?? DefaultCreatedAt;

                var createdBy =
                    string.IsNullOrWhiteSpace(task.CreatedBy)
                        ? DefaultImportUser
                        : task.CreatedBy.Trim();

                var completedBy =
                    completedAt.HasValue &&
                    string.IsNullOrWhiteSpace(task.CompletedBy)
                        ? DefaultImportUser
                        : task.CompletedBy.Trim();

                result.Add(new QualityTaskCockpitRow
                {
                    SapMaterialNumber = displayMaterialNumber,
                    MaterialStatus = materialStatus,
                    OldMaterialNumber = legacyArticleNumber,

                    TaskNumber = task.Number,
                    TaskText = task.Text,

                    CreatedAt = createdAt,
                    CreatedBy = createdBy,

                    DueDate = task.DueDate,

                    CompletedAt = completedAt,
                    CompletedBy = completedBy,

                    FullPrintVersionNumber =
        printVersion.FullPrintVersionNumber
                });
            }
        }

        return result
            .OrderBy(item => item.IsCompleted)
            .ThenBy(item => item.SapMaterialNumber)
            .ThenBy(item => item.OldMaterialNumber)
            .ThenBy(item => item.TaskNumber)
            .ToList();
    }

    private SapMaterial? ResolveSapMaterial(
        QualityPrintVersion printVersion,
        string legacyArticleNumber)
    {
        var sapNumber =
            NormalizeSapNumber(printVersion.SapMaterialNumber);

        if (!string.IsNullOrWhiteSpace(sapNumber))
        {
            var bySapNumber = _sapMaterials.FirstOrDefault(item =>
                string.Equals(
                    NormalizeSapNumber(item.MaterialNumber),
                    sapNumber,
                    StringComparison.OrdinalIgnoreCase));

            if (bySapNumber is not null)
            {
                return bySapNumber;
            }
        }

        if (string.IsNullOrWhiteSpace(legacyArticleNumber))
        {
            return null;
        }

        var legacyLookup = NormalizeLegacyArticleNumberForSapLookup(legacyArticleNumber);

        return _sapMaterials.FirstOrDefault(item =>
            string.Equals(
                NormalizeLegacyArticleNumberForSapLookup(item.OldMaterialNumber),
                legacyLookup,
                StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveLegacyArticleNumber(
        QualityPrintVersion printVersion)
    {
        var fullPrintVersion =
            printVersion.FullPrintVersionNumber?.Trim()
            ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(fullPrintVersion))
        {
            return fullPrintVersion;
        }

        var legacy =
            printVersion.LegacyArticleNumber?.Trim()
            ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(legacy))
        {
            return legacy;
        }

        return string.Empty;
    }

    private static string NormalizeLegacyArticleNumberForDisplay(
    string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
    }

    private static string NormalizeLegacyArticleNumberForSapLookup(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value.Trim();

        if (text.Contains('.'))
        {
            text = text.Split('.')[0];
        }

        return text.Trim();
    }

    private static string NormalizeLegacyArticleNumber(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value.Trim();

        if (text.Contains('.'))
        {
            text = text.Split('.')[0];
        }

        return text.Trim();
    }

    private static string NormalizeSapNumber(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value.Trim();

        if (text.Contains('.'))
        {
            text = text.Split('.')[0];
        }

        return text.All(char.IsDigit)
            ? text.PadLeft(10, '0')
            : text;
    }
}