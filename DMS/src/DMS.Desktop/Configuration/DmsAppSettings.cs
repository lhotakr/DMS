namespace DMS.Desktop.Configuration;

public sealed class DmsAppSettings
{
    public string Environment { get; set; } = "DEV";
    public string ConfigurationMode { get; set; } = "LocalJson";
    public string ArticlesDataPath { get; set; } = string.Empty;
    public string ConfigurationRootPath { get; set; } = string.Empty;
    public string DocumentsRootPath { get; set; } = string.Empty;
    public string LogsRootPath { get; set; } = string.Empty;
    public string BrandingRootPath { get; set; } = string.Empty;

    public string DefaultTestArticleNumber { get; set; } = "1000015148";

    public string SapMode { get; set; } = "Disabled";
    public string MesMode { get; set; } = "Disabled";
    public string DatabaseMode { get; set; } = "Disabled";
}