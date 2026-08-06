using System;

namespace DMS.Core.Documents;

public sealed class DmsArticleDocumentRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string ArticleNumber { get; set; } = string.Empty;

    public string StoredFileName { get; set; } = string.Empty;

    public string OriginalFileName { get; set; } = string.Empty;

    public string DocumentKind { get; set; } = "Document";

    public string Description { get; set; } = string.Empty;

    public string Extension { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string Sha256 { get; set; } = string.Empty;

    public string UploadedBy { get; set; } = string.Empty;

    public DateTime UploadedAt { get; set; } = DateTime.Now;

    public string ChangedBy { get; set; } = string.Empty;

    public DateTime? ChangedAt { get; set; }

    public bool IsActive { get; set; } = true;
}
