using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DMS.Core.Documents;

public sealed class DmsArticleDocumentIndexService
{
    public const string IndexFileName = "_dms-document-index.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly HashSet<string> SupportedExtensions = new(
        new[]
        {
            ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".png", ".jpg", ".jpeg",
            ".txt", ".msg", ".eml", ".ppt", ".pptx"
        },
        StringComparer.OrdinalIgnoreCase);

    public string ArticleFolderPath { get; }

    public string IndexFilePath => Path.Combine(ArticleFolderPath, IndexFileName);

    public DmsArticleDocumentIndexService(string articleFolderPath)
    {
        ArticleFolderPath = articleFolderPath;
    }

    public DmsArticleDocumentIndex Load(string articleNumber)
    {
        if (!File.Exists(IndexFilePath))
        {
            return new DmsArticleDocumentIndex
            {
                ArticleNumber = articleNumber,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
        }

        var json = File.ReadAllText(IndexFilePath, Encoding.UTF8);

        var index = JsonSerializer.Deserialize<DmsArticleDocumentIndex>(json, JsonOptions)
                    ?? new DmsArticleDocumentIndex();

        index.ArticleNumber = string.IsNullOrWhiteSpace(index.ArticleNumber)
            ? articleNumber
            : index.ArticleNumber;

        index.Documents ??= new List<DmsArticleDocumentRecord>();

        return index;
    }

    public void Save(DmsArticleDocumentIndex index)
    {
        Directory.CreateDirectory(ArticleFolderPath);

        index.UpdatedAt = DateTime.Now;

        var json = JsonSerializer.Serialize(index, JsonOptions);
        File.WriteAllText(IndexFilePath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public List<DmsArticleDocumentRecord> LoadDisplayRecords(string articleNumber)
    {
        var index = Load(articleNumber);
        var records = index.Documents.ToList();

        if (!Directory.Exists(ArticleFolderPath))
        {
            return records;
        }

        foreach (var file in EnumerateSupportedFiles(ArticleFolderPath))
        {
            var storedFileName = Path.GetFileName(file);

            if (records.Any(item =>
                    string.Equals(item.StoredFileName, storedFileName, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            records.Add(CreateVirtualRecord(articleNumber, file));
        }

        return records
            .Where(item => item.IsActive)
            .OrderBy(item => item.DocumentKind)
            .ThenBy(item => item.StoredFileName)
            .ToList();
    }

    public DmsArticleDocumentIndex LoadAndIndexPhysicalFiles(
        string articleNumber,
        string userName,
        out List<DmsArticleDocumentRecord> createdRecords)
    {
        Directory.CreateDirectory(ArticleFolderPath);

        var index = Load(articleNumber);
        createdRecords = new List<DmsArticleDocumentRecord>();

        foreach (var file in EnumerateSupportedFiles(ArticleFolderPath))
        {
            var storedFileName = Path.GetFileName(file);

            if (index.Documents.Any(item =>
                    string.Equals(item.StoredFileName, storedFileName, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var record = CreateVirtualRecord(articleNumber, file);
            record.UploadedBy = userName;
            record.ChangedBy = userName;
            record.ChangedAt = record.UploadedAt;
            index.Documents.Add(record);
            createdRecords.Add(record);
        }

        if (createdRecords.Count > 0)
        {
            Save(index);
        }

        return index;
    }

    public string CopyNewDocument(
        DmsArticleDocumentIndex index,
        string sourceFilePath,
        string articleNumber,
        string documentKind,
        string description,
        string userName)
    {
        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException("Source document file was not found.", sourceFilePath);
        }

        Directory.CreateDirectory(ArticleFolderPath);

        var originalFileName = Path.GetFileName(sourceFilePath);
        var storedFileName = GetUniqueStoredFileName(originalFileName);
        var targetPath = Path.Combine(ArticleFolderPath, storedFileName);

        File.Copy(sourceFilePath, targetPath, overwrite: false);

        var info = new FileInfo(targetPath);
        var now = DateTime.Now;

        var record = new DmsArticleDocumentRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            ArticleNumber = articleNumber,
            StoredFileName = storedFileName,
            OriginalFileName = originalFileName,
            DocumentKind = string.IsNullOrWhiteSpace(documentKind) ? DetectDocumentKind(originalFileName) : documentKind,
            Description = description.Trim(),
            Extension = info.Extension,
            SizeBytes = info.Length,
            Sha256 = CalculateSha256(targetPath),
            UploadedBy = userName,
            UploadedAt = now,
            ChangedBy = userName,
            ChangedAt = now,
            IsActive = true
        };

        index.Documents.Add(record);
        Save(index);

        return record.Id;
    }

    public DmsArticleDocumentRecord ReplaceDocumentFile(
        DmsArticleDocumentIndex index,
        string documentId,
        string sourceFilePath,
        string userName)
    {
        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException("Source document file was not found.", sourceFilePath);
        }

        var record = FindRequired(index, documentId);
        var targetPath = GetPhysicalPath(record);

        Directory.CreateDirectory(ArticleFolderPath);

        if (File.Exists(targetPath))
        {
            var backupPath = targetPath + $".{DateTime.Now:yyyyMMddHHmmss}.bak";
            File.Copy(targetPath, backupPath, overwrite: false);
        }

        File.Copy(sourceFilePath, targetPath, overwrite: true);

        var info = new FileInfo(targetPath);
        record.OriginalFileName = Path.GetFileName(sourceFilePath);
        record.Extension = info.Extension;
        record.SizeBytes = info.Length;
        record.Sha256 = CalculateSha256(targetPath);
        record.ChangedBy = userName;
        record.ChangedAt = DateTime.Now;

        Save(index);
        return record;
    }

    public DmsArticleDocumentRecord UpdateMetadata(
        DmsArticleDocumentIndex index,
        string documentId,
        string documentKind,
        string description,
        bool isActive,
        string userName)
    {
        var record = FindRequired(index, documentId);

        record.DocumentKind = string.IsNullOrWhiteSpace(documentKind)
            ? "Document"
            : documentKind.Trim();

        record.Description = description.Trim();
        record.IsActive = isActive;
        record.ChangedBy = userName;
        record.ChangedAt = DateTime.Now;

        Save(index);
        return record;
    }

    public DmsArticleDocumentRecord ArchiveDocument(
        DmsArticleDocumentIndex index,
        string documentId,
        string userName)
    {
        var record = FindRequired(index, documentId);
        record.IsActive = false;
        record.ChangedBy = userName;
        record.ChangedAt = DateTime.Now;
        Save(index);
        return record;
    }

    public string GetPhysicalPath(DmsArticleDocumentRecord record)
    {
        return Path.Combine(ArticleFolderPath, record.StoredFileName);
    }

    public static string DetectDocumentKind(string fileName)
    {
        var name = fileName.ToLowerInvariant();

        if (name.Contains("massblatt") || name.Contains("mas") || name.Contains("maß") || name.Contains("mb"))
            return "Massblatt";
        if (name.Contains("vykres") || name.Contains("výkres") || name.Contains("drawing") || name.Contains("zeichnung"))
            return "Drawing";
        if (name.Contains("tisk") || name.Contains("print"))
            return "Print area";
        if (name.Contains("bal") || name.Contains("verpack"))
            return "Packaging instruction";
        if (name.Contains("recept") || name.Contains("recipe") || name.Contains("rez"))
            return "Recipe";
        if (name.Contains("zak") || name.Contains("zakázka") || name.Contains("order"))
            return "Sample order";
        if (name.Contains("obeznik") || name.Contains("oběžník") || name.Contains("circular"))
            return "Circular";
        if (name.Contains("check") || name.Contains("checklist"))
            return "Checklist";
        if (name.Contains("kalk") || name.Contains("calculation"))
            return "Calculation";
        if (name.Contains("eml") || name.Contains("email"))
            return "Email";
        if (name.Contains("schválení") || name.Contains("schv") || name.Contains("approval"))
            return "Approval";
        if (name.Contains("musterbegleitschein") || name.Contains("muster"))
            return "Musterbegleitschein";
        if (name.Contains("výr") || name.Contains("vyr") || name.Contains("production"))
            return "Production approval";
        if (name.Contains("podeps") || name.Contains("signed"))
            return "Signed document";

        return "Document";
    }

    private static IEnumerable<string> EnumerateSupportedFiles(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return Enumerable.Empty<string>();
        }

        return Directory
            .EnumerateFiles(folder, "*.*", SearchOption.TopDirectoryOnly)
            .Where(file => !string.Equals(Path.GetFileName(file), IndexFileName, StringComparison.OrdinalIgnoreCase))
            .Where(file => SupportedExtensions.Contains(Path.GetExtension(file)));
    }

    private DmsArticleDocumentRecord FindRequired(DmsArticleDocumentIndex index, string documentId)
    {
        var record = index.Documents.FirstOrDefault(item =>
            string.Equals(item.Id, documentId, StringComparison.OrdinalIgnoreCase));

        if (record is null)
        {
            throw new InvalidOperationException($"Document index record was not found: {documentId}");
        }

        return record;
    }

    private DmsArticleDocumentRecord CreateVirtualRecord(string articleNumber, string file)
    {
        var info = new FileInfo(file);

        return new DmsArticleDocumentRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            ArticleNumber = articleNumber,
            StoredFileName = info.Name,
            OriginalFileName = info.Name,
            DocumentKind = DetectDocumentKind(info.Name),
            Extension = info.Extension,
            SizeBytes = info.Length,
            Sha256 = CalculateSha256(file),
            UploadedBy = "UNKNOWN",
            UploadedAt = info.CreationTime,
            ChangedBy = "UNKNOWN",
            ChangedAt = info.LastWriteTime,
            IsActive = true
        };
    }

    private string GetUniqueStoredFileName(string originalFileName)
    {
        var safeName = MakeSafeFileName(originalFileName);
        var extension = Path.GetExtension(safeName);
        var baseName = Path.GetFileNameWithoutExtension(safeName);
        var candidate = safeName;
        var counter = 1;

        while (File.Exists(Path.Combine(ArticleFolderPath, candidate)))
        {
            candidate = $"{baseName}_{DateTime.Now:yyyyMMddHHmmss}_{counter}{extension}";
            counter++;
        }

        return candidate;
    }

    private static string MakeSafeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(fileName.Length);

        foreach (var ch in fileName)
        {
            builder.Append(invalid.Contains(ch) ? '_' : ch);
        }

        return builder.ToString().Trim();
    }

    private static string CalculateSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }
}
