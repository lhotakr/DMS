namespace DMS.Desktop.Configuration;

public sealed class DmsAppSettings
{
    public string Environment { get; set; } = "DEV";
    public string ConfigurationMode { get; set; } = "LocalJson";

    /// <summary>
    /// Shared DMS storage root without environment.
    /// Default: \\cze-sfs01\Data\SAP\DMS-db
    /// </summary>
    public string StorageRootPath { get; set; } = string.Empty;

    /// <summary>
    /// Canonical environment root.
    /// Example: \\cze-sfs01\Data\SAP\DMS-db\DEV
    /// </summary>
    public string EnvironmentRootPath { get; set; } = string.Empty;

    // Derived compatibility properties. DmsStoragePathPolicy.Normalize()
    // keeps them synchronized with EnvironmentRootPath.
    public string ConfigurationRootPath { get; set; } = string.Empty;
    public string DataRootPath { get; set; } = string.Empty;
    public string DocumentsRootPath { get; set; } = string.Empty;
    public string LogsRootPath { get; set; } = string.Empty;
    public string BrandingRootPath { get; set; } = string.Empty;
    public string ArticlesDataPath { get; set; } = string.Empty;

    public string DefaultTestArticleNumber { get; set; } = "1000015148";

    public string SapMode { get; set; } = "Disabled";
    public string MesMode { get; set; } = "Disabled";
    public string DatabaseMode { get; set; } = "Disabled";
}
