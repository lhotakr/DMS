using System.IO;
using System.Text.Json;
using DMS.Desktop.Models;

namespace DMS.Desktop.Repositories;

public sealed class JsonArticleRepository
{
    private readonly string _filePath;

    public JsonArticleRepository(string filePath)
    {
        _filePath = filePath;
    }

    public IReadOnlyList<DmsArticle> LoadAll()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new List<DmsArticle>();
            }

            var json = File.ReadAllText(_filePath);

            return JsonSerializer.Deserialize<List<DmsArticle>>(
                       json,
                       new JsonSerializerOptions
                       {
                           PropertyNameCaseInsensitive = true
                       })
                   ?? new List<DmsArticle>();
        }
        catch
        {
            return new List<DmsArticle>();
        }
    }

    public DmsArticle? FindBySapNumber(string sapArticleNumber)
    {
        return LoadAll()
            .FirstOrDefault(article =>
                string.Equals(
                    article.SapArticleNumber,
                    sapArticleNumber,
                    StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<DmsArticle> Search(
        string? sapArticleNumber,
        string? oldMaterialNumber,
        string? description,
        string? decorationCode)
    {
        return LoadAll()
            .Where(article =>
                Contains(article.SapArticleNumber, sapArticleNumber) &&
                Contains(article.OldMaterialNumber, oldMaterialNumber) &&
                Contains(article.Description, description) &&
                HasDecoration(article, decorationCode))
            .OrderBy(article => article.SapArticleNumber)
            .ToList();
    }

    public void Save(DmsArticle article)
    {
        var articles = LoadAll().ToList();

        var existingIndex = articles.FindIndex(item =>
            string.Equals(
                item.SapArticleNumber,
                article.SapArticleNumber,
                StringComparison.OrdinalIgnoreCase));

        if (existingIndex >= 0)
        {
            articles[existingIndex] = article;
        }
        else
        {
            articles.Add(article);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

        var json = JsonSerializer.Serialize(
            articles.OrderBy(item => item.SapArticleNumber).ToList(),
            new JsonSerializerOptions
            {
                WriteIndented = true
            });

        File.WriteAllText(_filePath, json);
    }

    private static bool Contains(string? value, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return (value ?? string.Empty)
            .Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasDecoration(DmsArticle article, string? decorationCode)
    {
        if (string.IsNullOrWhiteSpace(decorationCode))
        {
            return true;
        }

        return string.Equals(
            article.DecorationCode,
            decorationCode.Trim(),
            StringComparison.OrdinalIgnoreCase);
    }
}