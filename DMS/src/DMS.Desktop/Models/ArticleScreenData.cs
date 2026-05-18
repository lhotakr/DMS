namespace DMS.Desktop.Models;

public sealed class ArticleScreenData
{
    public string OperationNumber { get; set; } = string.Empty;
    // Například 0010

    public string Purpose { get; set; } = string.Empty;
    // Print, Primer

    public string MachineType { get; set; } = string.Empty;
    // K14, K15

    public string Mesh { get; set; } = string.Empty;

    public int PrintPass { get; set; }

    public string ScreenArticleNumber { get; set; } = string.Empty;

    public string Note { get; set; } = string.Empty;
}