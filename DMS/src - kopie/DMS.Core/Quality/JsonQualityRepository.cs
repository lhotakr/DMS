using System.Text.Json;

namespace DMS.Core.Quality;

public sealed class JsonQualityRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly QualityStoragePaths _paths;

    public JsonQualityRepository(QualityStoragePaths paths)
    {
        _paths = paths;
        _paths.EnsureDirectories();
    }

    public IReadOnlyList<QualityArticle> LoadArticles()
    {
        return LoadList<QualityArticle>(_paths.QualityArticlesFilePath);
    }

    public IReadOnlyList<QualityPrintVersion> LoadPrintVersions()
    {
        return LoadList<QualityPrintVersion>(_paths.QualityPrintVersionsFilePath);
    }

    public IReadOnlyList<QualityOrder> LoadOrders()
    {
        return LoadList<QualityOrder>(_paths.QualityOrdersFilePath);
    }

    public void SaveArticles(IEnumerable<QualityArticle> articles)
    {
        SaveList(_paths.QualityArticlesFilePath, articles);
    }

    public void SavePrintVersions(IEnumerable<QualityPrintVersion> printVersions)
    {
        SaveList(_paths.QualityPrintVersionsFilePath, printVersions);
    }

    public void SaveOrders(IEnumerable<QualityOrder> orders)
    {
        SaveList(_paths.QualityOrdersFilePath, orders);
    }

    private static List<T> LoadList<T>(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new List<T>();
        }

        var json = File.ReadAllText(filePath);

        return JsonSerializer.Deserialize<List<T>>(json, JsonOptions)
            ?? new List<T>();
    }

    private static void SaveList<T>(string filePath, IEnumerable<T> items)
    {
        var directory = Path.GetDirectoryName(filePath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(items.ToList(), JsonOptions);

        File.WriteAllText(filePath, json);
    }

    public IReadOnlyList<QualityCustomer> LoadCustomers()
    {
        return LoadList<QualityCustomer>(
            _paths.QualityCustomersFilePath);
    }

    public IReadOnlyList<QualityLookupItem> LoadColorTypes()
    {
        return LoadList<QualityLookupItem>(
            _paths.QualityColorTypesFilePath);
    }

    public void SaveColorTypes(
        IEnumerable<QualityLookupItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        SaveList(
            _paths.QualityColorTypesFilePath,
            items);
    }

    public IReadOnlyList<QualityLookupItem> LoadGlassTreatments()
    {
        return LoadList<QualityLookupItem>(
            _paths.QualityGlassTreatmentsFilePath);
    }

    public void SaveGlassTreatments(
        IEnumerable<QualityLookupItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        SaveList(
            _paths.QualityGlassTreatmentsFilePath,
            items);
    }

    public IReadOnlyList<QualityLookupItem> LoadQualityClasses()
    {
        return LoadList<QualityLookupItem>(
            _paths.QualityClassesFilePath);
    }

    public void SaveQualityClasses(
        IEnumerable<QualityLookupItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        SaveList(
            _paths.QualityClassesFilePath,
            items);
    }

    public void SaveCustomers(
        IEnumerable<QualityCustomer> customers)
    {
        ArgumentNullException.ThrowIfNull(customers);

        SaveList(
            _paths.QualityCustomersFilePath,
            customers);
    }
}