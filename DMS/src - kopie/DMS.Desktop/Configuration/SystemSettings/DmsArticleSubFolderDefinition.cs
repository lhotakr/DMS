namespace DMS.Desktop.Configuration.SystemSettings;

public sealed class DmsArticleSubFolderDefinition
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string RelativePath { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}