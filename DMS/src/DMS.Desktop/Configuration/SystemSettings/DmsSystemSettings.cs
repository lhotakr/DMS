namespace DMS.Desktop.Configuration.SystemSettings;

public sealed class DmsSystemSettings
{
    public string DocumentsRootPath { get; set; } = string.Empty;

    public string ArticleFoldersRootPath { get; set; } = string.Empty;

    public bool CreateArticleFoldersOnSapImport { get; set; } = true;

    public List<DmsArticleSubFolderDefinition> ArticleSubFolders { get; set; } = new();

    public List<DmsMaterialRangeDefinition> ArticleFolderMaterialRanges { get; set; } = new();
}