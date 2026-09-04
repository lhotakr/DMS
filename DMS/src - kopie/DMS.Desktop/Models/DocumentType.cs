namespace DMS.Desktop.Models;

public sealed class DocumentType
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool IsRequired { get; set; }

    public string Note { get; set; } = string.Empty;
}