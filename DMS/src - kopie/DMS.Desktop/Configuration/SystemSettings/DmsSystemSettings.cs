namespace DMS.Desktop.Configuration.SystemSettings;

public sealed class DmsSystemSettings
{
    public string DocumentsRootPath { get; set; } = string.Empty;

    public string ArticleFoldersRootPath { get; set; } = string.Empty;

    public bool CreateArticleFoldersOnSapImport { get; set; } = true;

    public List<DmsArticleSubFolderDefinition> ArticleSubFolders { get; set; } = new();

    public List<DmsMaterialRangeDefinition> ArticleFolderMaterialRanges { get; set; } = new();

    // Branding / logo
    public string HeaderSecondaryLogoPath { get; set; } = string.Empty;
    public double HeaderSecondaryLogoMaxWidth { get; set; } = 320;
    public double HeaderSecondaryLogoMaxHeight { get; set; } = 80;
}