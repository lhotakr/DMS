using System;
using System.Collections.Generic;

namespace DMS.Core.Documents;

public sealed class DmsArticleDocumentIndex
{
    public string ArticleNumber { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public List<DmsArticleDocumentRecord> Documents { get; set; } = new();
}
