using System.Globalization;
using System.IO;
using System.Text.Json;

namespace DMS.Desktop.Localization;

public sealed class DmsLocalizationService
{
    private readonly string _localizationRootPath;

    private DmsLocalizationIndex _index = new();
    private Dictionary<string, string> _defaultDictionary = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _activeDictionary = new(StringComparer.OrdinalIgnoreCase);

    public string ActiveCulture { get; private set; } = "en-US";

    public DmsLocalizationService(string localizationRootPath)
    {
        _localizationRootPath = localizationRootPath;
    }

    public IReadOnlyList<DmsSupportedCulture> SupportedCultures => _index.SupportedCultures;

    public void Load(string? languageMode, string? userCultureName)
    {
        _index = LoadIndex();

        var defaultCulture = string.IsNullOrWhiteSpace(_index.DefaultCulture)
            ? "en-US"
            : _index.DefaultCulture;

        var selectedCulture = ResolveCulture(languageMode, userCultureName, defaultCulture);

        ActiveCulture = selectedCulture;

        _defaultDictionary = LoadDictionary(defaultCulture);
        _activeDictionary = string.Equals(selectedCulture, defaultCulture, StringComparison.OrdinalIgnoreCase)
            ? _defaultDictionary
            : LoadDictionary(selectedCulture);
    }

    public string Translate(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        if (_activeDictionary.TryGetValue(key, out var activeValue))
        {
            return activeValue;
        }

        if (_defaultDictionary.TryGetValue(key, out var defaultValue))
        {
            return defaultValue;
        }

        return $"[[{key}]]";
    }

    public string Translate(string key, params object[] args)
    {
        var template = Translate(key);

        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, args);
        }
        catch
        {
            return template;
        }
    }

    private DmsLocalizationIndex LoadIndex()
    {
        var path = Path.Combine(_localizationRootPath, "localization.index.json");

        if (!File.Exists(path))
        {
            return new DmsLocalizationIndex
            {
                DefaultCulture = "en-US",
                SupportedCultures =
                {
                    new DmsSupportedCulture
                    {
                        Culture = "en-US",
                        DisplayName = "English",
                        IsDefault = true
                    }
                }
            };
        }

        var json = File.ReadAllText(path);

        return JsonSerializer.Deserialize<DmsLocalizationIndex>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new DmsLocalizationIndex();
    }

    private Dictionary<string, string> LoadDictionary(string cultureName)
    {
        var path = Path.Combine(_localizationRootPath, $"{cultureName}.json");

        if (!File.Exists(path))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var json = File.ReadAllText(path);

        return JsonSerializer.Deserialize<Dictionary<string, string>>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private string ResolveCulture(string? languageMode, string? userCultureName, string defaultCulture)
    {
        if (string.Equals(languageMode, "Manual", StringComparison.OrdinalIgnoreCase)
            && IsSupportedCulture(userCultureName))
        {
            return userCultureName!;
        }

        var systemCulture = CultureInfo.CurrentUICulture.Name;

        if (IsSupportedCulture(systemCulture))
        {
            return systemCulture;
        }

        var neutralSystemCulture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

        var matchingCulture = _index.SupportedCultures.FirstOrDefault(item =>
            item.Culture.StartsWith(neutralSystemCulture + "-", StringComparison.OrdinalIgnoreCase));

        if (matchingCulture is not null)
        {
            return matchingCulture.Culture;
        }

        return defaultCulture;
    }

    private bool IsSupportedCulture(string? cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName))
        {
            return false;
        }

        return _index.SupportedCultures.Any(item =>
            string.Equals(item.Culture, cultureName, StringComparison.OrdinalIgnoreCase));
    }
}
