namespace DMS.Desktop.Models;

public sealed class ArticleDocumentItem
{
    public string FileName { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public string Extension { get; init; } = string.Empty;
    public long SizeBytes { get; init; }

    public string SizeText
    {
        get
        {
            if (SizeBytes < 1024)
            {
                return $"{SizeBytes} B";
            }

            if (SizeBytes < 1024 * 1024)
            {
                return $"{SizeBytes / 1024.0:0.0} KB";
            }

            return $"{SizeBytes / 1024.0 / 1024.0:0.0} MB";
        }
    }

    public string DocumentKind { get; init; } = "Dokument";
    public DateTime LastModified { get; init; }
    public string LastModifiedText => LastModified.ToString("dd.MM.yyyy HH:mm");
    public string IconText => Extension.ToLowerInvariant() switch
    {
        ".pdf" => "📄",
        ".doc" or ".docx" => "📝",
        ".xls" or ".xlsx" => "📊",
        ".png" or ".jpg" or ".jpeg" => "🖼",
        ".msg" or ".eml" => "✉",
        _ => "📁"
    };
}