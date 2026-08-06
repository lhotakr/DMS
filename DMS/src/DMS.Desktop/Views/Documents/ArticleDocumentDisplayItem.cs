using DMS.Core.Documents;

namespace DMS.Desktop.Views.Documents;

public sealed class ArticleDocumentDisplayItem
{
    public DmsArticleDocumentRecord Record { get; init; } = new();

    public string Id => Record.Id;

    public string FileName => Record.StoredFileName;

    public string OriginalFileName => Record.OriginalFileName;

    public string DocumentKind => Record.DocumentKind;

    public string Description => Record.Description;

    public string Extension => Record.Extension;

    public long SizeBytes => Record.SizeBytes;

    public string SizeText => FormatBytes(SizeBytes);

    public string UploadedBy => Record.UploadedBy;

    public DateTime UploadedAt => Record.UploadedAt;

    public string ChangedBy => Record.ChangedBy;

    public DateTime? ChangedAt => Record.ChangedAt;

    public bool IsActive => Record.IsActive;

    public string IsActiveText { get; init; } = string.Empty;

    public string FullPath { get; init; } = string.Empty;

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024.0:0.0} KB";
        }

        return $"{bytes / 1024.0 / 1024.0:0.0} MB";
    }
}
