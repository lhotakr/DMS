namespace DMS.Core.Quality;

public sealed class QualityStoragePaths
{
    public QualityStoragePaths(string basePath)
    {
        if (string.IsNullOrWhiteSpace(basePath))
        {
            throw new ArgumentException(
                "Základní cesta quality úložiště nesmí být prázdná.",
                nameof(basePath));
        }

        BasePath = basePath;
        DataPath = Path.Combine(BasePath, "Data");
        QualityPath = Path.Combine(DataPath, "Quality");

        QualityArticlesFilePath = Path.Combine(
            QualityPath,
            "quality-articles.json");

        QualityPrintVersionsFilePath = Path.Combine(
            QualityPath,
            "quality-print-versions.json");

        QualityOrdersFilePath = Path.Combine(
            QualityPath,
            "quality-orders.json");

        QualityCustomersFilePath = Path.Combine(
            QualityPath,
            "quality-customers.json");

        QualityColorTypesFilePath = Path.Combine(
            QualityPath,
            "quality-color-types.json");

        QualityGlassTreatmentsFilePath = Path.Combine(
            QualityPath,
            "quality-glass-treatments.json");

        QualityClassesFilePath = Path.Combine(
            QualityPath,
            "quality-classes.json");
    }

    public string BasePath { get; }

    public string DataPath { get; }

    public string QualityPath { get; }

    public string QualityArticlesFilePath { get; }

    public string QualityPrintVersionsFilePath { get; }

    public string QualityOrdersFilePath { get; }

    public string QualityCustomersFilePath { get; }
    public string QualityColorTypesFilePath { get; }

    public string QualityGlassTreatmentsFilePath { get; }

    public string QualityClassesFilePath { get; }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(DataPath);
        Directory.CreateDirectory(QualityPath);
    }
}