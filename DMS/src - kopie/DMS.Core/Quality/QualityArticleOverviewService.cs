using DMS.Core.Sap;

namespace DMS.Core.Quality;

public sealed class QualityArticleOverviewService
{
    private readonly IReadOnlyList<SapMaterial> _sapMaterials;
    private readonly SapMaterialStatusRuleService? _statusRuleService;
    private readonly IReadOnlyList<QualityArticle> _qualityArticles;
    private readonly IReadOnlyList<QualityPrintVersion> _printVersions;
    private readonly IReadOnlyList<QualityOrder> _orders;

    public QualityArticleOverviewService(
    IReadOnlyList<SapMaterial> sapMaterials,
    IReadOnlyList<QualityArticle> qualityArticles,
    IReadOnlyList<QualityPrintVersion> printVersions,
    IReadOnlyList<QualityOrder> orders,
    SapMaterialStatusRuleService? statusRuleService = null)
    {
        _sapMaterials = sapMaterials;
        _qualityArticles = qualityArticles;
        _printVersions = printVersions;
        _orders = orders;
        _statusRuleService = statusRuleService;
    }

    public QualityArticleOverview BuildOverview(string query)
    {
        query = NormalizeText(query);

        var normalizedSapNumber = NormalizeSapNumber(query);

        var sapMaterial = FindSapMaterial(normalizedSapNumber);

        var matchedPrintVersions = FindPrintVersions(query, normalizedSapNumber);

        var legacyArticleNumber =
            matchedPrintVersions.FirstOrDefault()?.LegacyArticleNumber
            ?? TryExtractLegacyArticleNumber(query)
            ?? TryFindLegacyFromSapMaterial(sapMaterial)
            ?? string.Empty;

        var qualityArticle = FindQualityArticle(legacyArticleNumber);

        if (matchedPrintVersions.Count == 0 && !string.IsNullOrWhiteSpace(legacyArticleNumber))
        {
            matchedPrintVersions = _printVersions
                .Where(item => string.Equals(item.LegacyArticleNumber, legacyArticleNumber, StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.FullPrintVersionNumber)
                .ToList();
        }

        var sapMaterialNumber =
            sapMaterial?.MaterialNumber
            ?? matchedPrintVersions.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.SapMaterialNumber))?.SapMaterialNumber
            ?? normalizedSapNumber;

        var orders = FindOrders(matchedPrintVersions, sapMaterialNumber, query);

        var tasks = matchedPrintVersions
            .SelectMany(printVersion => printVersion.Tasks.Select(task => new QualityTaskOverviewRow
            {
                PrintVersionNumber = printVersion.FullPrintVersionNumber,
                Number = task.Number,
                Text = task.Text,

                CreatedAt = task.CreatedAt,
                CreatedBy = task.CreatedBy,
                DueDate = task.DueDate,

                CompletedAt = task.CompletedAt,
                CompletedBy = task.CompletedBy
            }))
            .OrderBy(item => item.IsCompleted)
            .ThenBy(item => item.PrintVersionNumber)
            .ThenBy(item => item.Number)
            .ToList();

        var overview = new QualityArticleOverview
        {
            Query = query,
            SapMaterialNumber = sapMaterialNumber,
            SapMaterial = sapMaterial,
            LegacyArticleNumber = legacyArticleNumber,
            QualityArticle = qualityArticle,
            PrintVersions = matchedPrintVersions,
            Orders = orders,
            Tasks = tasks,
            FormattedMaterialStatus =
                _statusRuleService?.FormatStatus(sapMaterial?.MaterialStatus)
                ?? sapMaterial?.MaterialStatus
                ?? string.Empty,
        };

        if (sapMaterial is null && IsLikelySapNumber(query))
        {
            overview.Messages.Add($"SAP materiál {normalizedSapNumber} nebyl nalezen v SAP cache.");
        }

        if (qualityArticle is null)
        {
            overview.Messages.Add("Quality poznámky k historickému artiklu nebyly nalezeny.");
        }

        if (matchedPrintVersions.Count == 0)
        {
            overview.Messages.Add("Nebyla nalezena žádná tisková verze.");
        }

        if (orders.Count == 0)
        {
            overview.Messages.Add("Nebyla nalezena žádná historická quality zakázka.");
        }

        return overview;
    }

    private List<QualityPrintVersion> FindPrintVersions(string query, string normalizedSapNumber)
    {
        var result = _printVersions
            .Where(item =>
                string.Equals(item.FullPrintVersionNumber, query, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.SapMaterialNumber, normalizedSapNumber, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.LegacyArticleNumber, query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.FullPrintVersionNumber)
            .ToList();

        return result;
    }

    private List<QualityOrder> FindOrders(
        IReadOnlyList<QualityPrintVersion> printVersions,
        string sapMaterialNumber,
        string query)
    {
        var printVersionNumbers = printVersions
            .Select(item => item.FullPrintVersionNumber)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return _orders
            .Where(order =>
                printVersionNumbers.Contains(order.PrintVersionNumber) ||
                string.Equals(order.SapMaterialNumber, sapMaterialNumber, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(order.OrderNumber, query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(order => order.ProductionStart ?? DateTime.MinValue)
            .ThenByDescending(order => order.OrderNumber)
            .ToList();
    }

    private SapMaterial? FindSapMaterial(string materialNumber)
    {
        if (string.IsNullOrWhiteSpace(materialNumber))
        {
            return null;
        }

        return _sapMaterials.FirstOrDefault(item =>
            string.Equals(item.MaterialNumber, materialNumber, StringComparison.OrdinalIgnoreCase));
    }

    private QualityArticle? FindQualityArticle(string legacyArticleNumber)
    {
        if (string.IsNullOrWhiteSpace(legacyArticleNumber))
        {
            return null;
        }

        return _qualityArticles.FirstOrDefault(item =>
            string.Equals(item.LegacyArticleNumber, legacyArticleNumber, StringComparison.OrdinalIgnoreCase));
    }

    private static string? TryExtractLegacyArticleNumber(string value)
    {
        value = NormalizeText(value);

        if (value.Length >= 7 && value.Take(7).All(char.IsDigit))
        {
            return value[..7];
        }

        return null;
    }

    private static string? TryFindLegacyFromSapMaterial(SapMaterial? material)
    {
        if (material is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(material.OldMaterialNumber) &&
            material.OldMaterialNumber.Length >= 7)
        {
            var digits = new string(material.OldMaterialNumber.Where(char.IsDigit).ToArray());

            if (digits.Length >= 7)
            {
                return digits[..7];
            }
        }

        return null;
    }

    private static string NormalizeSapNumber(string value)
    {
        value = NormalizeText(value);

        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (value.Contains('.'))
        {
            return value;
        }

        return value.All(char.IsDigit)
            ? value.PadLeft(10, '0')
            : value;
    }

    private static bool IsLikelySapNumber(string value)
    {
        value = NormalizeText(value);

        return value.Length <= 10 && value.All(char.IsDigit);
    }

    private static string NormalizeText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
    }
}