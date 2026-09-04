using DMS.Core.Sap;

namespace DMS.Core.Quality;

public sealed class QualityArticleOverview
{
    public string Query { get; init; } = string.Empty;

    public string SapMaterialNumber { get; init; } = string.Empty;

    public SapMaterial? SapMaterial { get; init; }
    public string FormattedMaterialStatus { get; init; } = string.Empty;

    public string LegacyArticleNumber { get; init; } = string.Empty;

    public QualityArticle? QualityArticle { get; init; }

    public List<QualityPrintVersion> PrintVersions { get; init; } = new();

    public List<QualityOrder> Orders { get; init; } = new();

    public List<QualityTaskOverviewRow> Tasks { get; init; } = new();

    public List<string> Messages { get; init; } = new();

    public bool HasData =>
        SapMaterial is not null ||
        QualityArticle is not null ||
        PrintVersions.Count > 0 ||
        Orders.Count > 0;
}
