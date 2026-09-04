namespace DMS.Desktop.Models;

public sealed class ArticleDocumentLink
{
    public string DocumentTypeCode { get; set; } = string.Empty;
    // DRAWING, PRINT_AREA, MASSBLATT, PACKING, RECIPE, APPROVAL...

    public string Name { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;
    // Draft, Approved, Archived

    public DateTime? ValidFrom { get; set; }

    public string Note { get; set; } = string.Empty;
}