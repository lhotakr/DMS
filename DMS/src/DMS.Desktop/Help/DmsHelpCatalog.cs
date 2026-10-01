using DMS.Core.Transactions;
using DMS.Desktop.Localization;
using System.IO;
using System.Text.RegularExpressions;

namespace DMS.Desktop.Help;

public sealed class DmsHelpCatalog
{
    private readonly string _rootPath;
    private readonly string _activeCulture;
    private readonly IReadOnlyList<TransactionDefinition> _definitions;
    private readonly Func<string, string> _translate;
    private readonly Dictionary<string, DmsHelpDocument> _documents = new(StringComparer.OrdinalIgnoreCase);

    public DmsHelpCatalog(
        string rootPath,
        string activeCulture,
        IEnumerable<TransactionDefinition> definitions,
        Func<string, string> translate)
    {
        _rootPath = rootPath ?? string.Empty;
        _activeCulture = string.IsNullOrWhiteSpace(activeCulture) ? "en-US" : activeCulture;
        _definitions = definitions?.ToList() ?? new List<TransactionDefinition>();
        _translate = translate ?? throw new ArgumentNullException(nameof(translate));

        Load();
    }

    public string ActiveCulture => _activeCulture;

    public IReadOnlyList<DmsHelpDocument> GetAllDocuments()
    {
        return _documents.Values
            .OrderBy(item => item.IsTransaction ? 1 : 0)
            .ThenBy(item => item.Category, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.SortOrder)
            .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public DmsHelpDocument? GetDocument(string? topicId)
    {
        if (string.IsNullOrWhiteSpace(topicId))
        {
            return GetAllDocuments().FirstOrDefault(item =>
                       string.Equals(item.TopicId, "GUIDE-START", StringComparison.OrdinalIgnoreCase))
                   ?? GetAllDocuments().FirstOrDefault();
        }

        return _documents.TryGetValue(NormalizeTopic(topicId), out var document)
            ? document
            : null;
    }

    public IReadOnlyList<DmsHelpDocument> Search(string? query)
    {
        var text = query?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return GetAllDocuments();
        }

        return GetAllDocuments()
            .Where(item =>
                Contains(item.TopicId, text) ||
                Contains(item.Title, text) ||
                Contains(item.Category, text) ||
                Contains(item.Markdown, text))
            .ToList();
    }

    private void Load()
    {
        // English is the stable fallback. The selected culture overrides documents with the same topic ID.
        LoadCulture("en-US", overwrite: true);

        if (!string.Equals(_activeCulture, "en-US", StringComparison.OrdinalIgnoreCase))
        {
            LoadCulture(_activeCulture, overwrite: true);
        }

        // Every visible transaction always has at least a generated reference page.
        foreach (var definition in _definitions)
        {
            if (_documents.ContainsKey(definition.Code))
            {
                continue;
            }

            _documents[definition.Code] = CreateGeneratedTransactionDocument(definition);
        }
    }

    private void LoadCulture(string culture, bool overwrite)
    {
        var culturePath = Path.Combine(_rootPath, culture);
        if (!Directory.Exists(culturePath))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(culturePath, "*.md", SearchOption.AllDirectories))
        {
            try
            {
                var markdown = File.ReadAllText(path);
                var fileTopic = Path.GetFileNameWithoutExtension(path);
                var topic = ReadMetadata(markdown, "topic") ?? fileTopic;
                topic = NormalizeTopic(topic);

                var definition = _definitions.FirstOrDefault(item =>
                    string.Equals(item.Code, topic, StringComparison.OrdinalIgnoreCase));

                var isTransactionFolder = path
                    .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Any(segment => string.Equals(segment, "Transactions", StringComparison.OrdinalIgnoreCase));

                // Transaction documentation follows the same visibility/role filtering as the transaction itself.
                if (isTransactionFolder && definition is null)
                {
                    continue;
                }

                var isTransaction = definition is not null;

                var title = ReadFirstHeading(markdown)
                            ?? (definition is null
                                ? topic
                                : $"{definition.Code} - {DmsTransactionText.Name(definition, _translate)}");

                var category = NormalizeCategory(
                    definition is not null
                        ? DmsTransactionText.Module(definition, _translate)
                        : ReadMetadata(markdown, "category")
                          ?? GetGuideCategory(culture));

                var sortOrder = int.TryParse(ReadMetadata(markdown, "order"), out var parsedOrder)
                    ? parsedOrder
                    : isTransaction ? 100 : 50;

                var document = new DmsHelpDocument
                {
                    TopicId = topic,
                    Title = title,
                    Category = category,
                    SortOrder = sortOrder,
                    Markdown = RemoveMetadataComments(markdown),
                    Culture = culture,
                    SourcePath = path,
                    IsGenerated = false,
                    IsTransaction = isTransaction,
                    TransactionCode = definition?.Code ?? (isTransaction ? topic : string.Empty)
                };

                if (overwrite || !_documents.ContainsKey(topic))
                {
                    _documents[topic] = document;
                }
            }
            catch
            {
                // A malformed help page must never prevent DMS from starting.
            }
        }
    }

    private DmsHelpDocument CreateGeneratedTransactionDocument(TransactionDefinition definition)
    {
        var name = DmsTransactionText.Name(definition, _translate);
        var module = DmsTransactionText.Module(definition, _translate);
        var description = DmsTransactionText.Description(definition, _translate);
        var isCzech = _activeCulture.StartsWith("cs", StringComparison.OrdinalIgnoreCase);

        var purposeHeading = isCzech ? "Účel" : "Purpose";
        var startHeading = isCzech ? "Spuštění" : "Start";
        var accessHeading = isCzech ? "Oprávnění" : "Access";
        var autoNote = isCzech
            ? "Tato stránka byla vytvořena automaticky z definice transakce. Podrobnější dokumentaci lze doplnit jako Markdown soubor bez změny aplikačního kódu."
            : "This page was generated automatically from the transaction definition. A richer page can be added as a Markdown file without changing application code.";

        var roles = definition.Roles.Count == 0
            ? (isCzech ? "Všichni uživatelé s přístupem k transakci." : "All users who can access the transaction.")
            : string.Join(", ", definition.Roles);

        var markdown = $"""
# {definition.Code} - {name}

## {purposeHeading}
{description}

## {startHeading}
`{definition.Code}`

## {accessHeading}
{roles}

> {autoNote}
""";

        return new DmsHelpDocument
        {
            TopicId = definition.Code,
            Title = $"{definition.Code} - {name}",
            Category = NormalizeCategory(module),
            SortOrder = 100,
            Markdown = markdown,
            Culture = _activeCulture,
            IsGenerated = true,
            IsTransaction = true,
            TransactionCode = definition.Code
        };
    }


    private static string NormalizeCategory(string? category)
    {
        var value = category?.Trim() ?? string.Empty;

        // QAMENU historically used the module name "QUALITY" while the rest
        // of the Quality transactions use "Quality". Help must present one
        // logical module regardless of source casing.
        if (string.Equals(value, "QUALITY", StringComparison.OrdinalIgnoreCase))
        {
            return "Quality";
        }

        return value;
    }

    private string GetGuideCategory(string culture)
    {
        return culture.StartsWith("cs", StringComparison.OrdinalIgnoreCase)
            ? "Uživatelská příručka"
            : "User guide";
    }

    private static string NormalizeTopic(string topic)
    {
        return topic.Trim().Replace(' ', '-').ToUpperInvariant();
    }

    private static bool Contains(string? value, string query)
    {
        return value?.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string? ReadMetadata(string markdown, string name)
    {
        var match = Regex.Match(
            markdown,
            $@"<!--\s*dms-{Regex.Escape(name)}\s*:\s*(.*?)\s*-->",
            RegexOptions.IgnoreCase);

        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string? ReadFirstHeading(string markdown)
    {
        foreach (var rawLine in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                return line[2..].Trim();
            }
        }

        return null;
    }

    private static string RemoveMetadataComments(string markdown)
    {
        return Regex.Replace(
            markdown,
            @"^\s*<!--\s*dms-[^>]+-->\s*$",
            string.Empty,
            RegexOptions.Multiline | RegexOptions.IgnoreCase).Trim();
    }
}
