using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace DMS.Desktop.Theming;

public sealed class DmsUiProfileService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _uiRoot;
    private readonly string _profilesRoot;
    private readonly string _activePath;

    public DmsUiProfileService(string uiRoot)
    {
        _uiRoot = uiRoot;
        _profilesRoot = Path.Combine(_uiRoot, "Profiles");
        _activePath = Path.Combine(_uiRoot, "active-profile.json");
    }

    public string UiRoot => _uiRoot;
    public string ProfilesRoot => _profilesRoot;
    public string ActiveProfilePath => _activePath;

    public IReadOnlyList<DmsUiProfileSummary> GetProfiles()
    {
        if (!Directory.Exists(_profilesRoot))
        {
            return Array.Empty<DmsUiProfileSummary>();
        }

        var result = new List<DmsUiProfileSummary>();

        foreach (var path in Directory.EnumerateFiles(
                     _profilesRoot,
                     "*.json",
                     SearchOption.TopDirectoryOnly))
        {
            try
            {
                var profile = ReadProfile(path);
                result.Add(new DmsUiProfileSummary(
                    profile.Code,
                    profile.Name,
                    profile.Version,
                    profile.ModifiedAt));
            }
            catch
            {
                // One damaged profile must not make SYS14 unusable.
            }
        }

        return result
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public DmsUiProfile EnsureDefaultProfile(string user)
    {
        var existing = GetProfiles();

        if (existing.Count > 0)
        {
            return LoadProfile(existing[0].Code);
        }

        var profile = new DmsUiProfile
        {
            Code = "DMS_DEFAULT",
            Name = "DMS Default",
            Description = "Factory theme with optional administrator overrides.",
            Version = 1,
            ModifiedBy = user,
            ModifiedAt = DateTime.Now
        };

        SaveProfile(profile);
        return profile;
    }

    public DmsUiProfile LoadProfile(string code)
    {
        var normalized = NormalizeCode(code);
        var path = GetProfilePath(normalized);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"UI profile '{normalized}' does not exist.",
                path);
        }

        return ReadProfile(path);
    }

    public DmsUiProfile? LoadActiveProfile()
    {
        if (!File.Exists(_activePath))
        {
            return null;
        }

        try
        {
            using var reader = new StreamReader(
                _activePath,
                detectEncodingFromByteOrderMarks: true);

            var active =
                JsonSerializer.Deserialize<DmsUiActiveProfile>(
                    reader.ReadToEnd(),
                    JsonOptions);

            if (active is null ||
                string.IsNullOrWhiteSpace(active.ProfileCode))
            {
                return null;
            }

            var path = GetProfilePath(active.ProfileCode);

            return File.Exists(path)
                ? ReadProfile(path)
                : null;
        }
        catch
        {
            return null;
        }
    }

    public string GetActiveProfileCode() =>
        LoadActiveProfile()?.Code ?? string.Empty;

    public void SaveProfile(DmsUiProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        profile.Code = NormalizeCode(profile.Code);

        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            profile.Name = profile.Code;
        }

        profile.Modules = new Dictionary<string, DmsUiLayer>(
            profile.Modules
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .ToDictionary(
                    pair => NormalizeCode(pair.Key),
                    pair => NormalizeLayer(pair.Value),
                    StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        profile.Transactions = new Dictionary<string, DmsUiLayer>(
            profile.Transactions
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .ToDictionary(
                    pair => NormalizeCode(pair.Key),
                    pair => NormalizeLayer(pair.Value),
                    StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        profile.Global = NormalizeLayer(profile.Global);
        profile.ModifiedAt = DateTime.Now;

        Directory.CreateDirectory(_profilesRoot);

        WriteJsonAtomic(
            GetProfilePath(profile.Code),
            profile);
    }

    public void SetActiveProfile(string? profileCode)
    {
        Directory.CreateDirectory(_uiRoot);

        var value = string.IsNullOrWhiteSpace(profileCode)
            ? string.Empty
            : NormalizeCode(profileCode);

        if (!string.IsNullOrWhiteSpace(value) &&
            !File.Exists(GetProfilePath(value)))
        {
            throw new InvalidOperationException(
                $"UI profile '{value}' cannot be activated because it does not exist.");
        }

        WriteJsonAtomic(
            _activePath,
            new DmsUiActiveProfile
            {
                ProfileCode = value
            });
    }

    public DmsUiProfile CreateProfile(
        string code,
        string name,
        string user)
    {
        var normalized = NormalizeCode(code);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("UI profile code is required.");
        }

        if (File.Exists(GetProfilePath(normalized)))
        {
            throw new InvalidOperationException(
                $"UI profile '{normalized}' already exists.");
        }

        var profile = new DmsUiProfile
        {
            Code = normalized,
            Name = string.IsNullOrWhiteSpace(name)
                ? normalized
                : name.Trim(),
            Version = 1,
            ModifiedBy = user,
            ModifiedAt = DateTime.Now
        };

        SaveProfile(profile);
        return profile;
    }

    public DmsUiProfile CloneProfile(
        DmsUiProfile source,
        string newCode,
        string newName,
        string user)
    {
        var clone = source.Clone();
        clone.Code = NormalizeCode(newCode);
        clone.Name = string.IsNullOrWhiteSpace(newName)
            ? clone.Code
            : newName.Trim();
        clone.Version = 1;
        clone.ModifiedBy = user;
        clone.ModifiedAt = DateTime.Now;

        if (string.IsNullOrWhiteSpace(clone.Code))
        {
            throw new InvalidOperationException("UI profile code is required.");
        }

        if (File.Exists(GetProfilePath(clone.Code)))
        {
            throw new InvalidOperationException(
                $"UI profile '{clone.Code}' already exists.");
        }

        SaveProfile(clone);
        return clone;
    }

    public void DeleteProfile(string code)
    {
        var normalized = NormalizeCode(code);

        if (string.Equals(
                GetActiveProfileCode(),
                normalized,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The active UI profile cannot be deleted. Activate another profile first.");
        }

        var path = GetProfilePath(normalized);

        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public void ExportProfile(
        DmsUiProfile profile,
        string outputPath)
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            $"dms-ui-export-{Guid.NewGuid():N}");

        Directory.CreateDirectory(tempRoot);

        try
        {
            var manifest = new
            {
                format = "DMS-UI-PROFILE",
                formatVersion = 1,
                exportedAt = DateTime.Now,
                profileCode = profile.Code,
                profileVersion = profile.Version
            };

            File.WriteAllText(
                Path.Combine(tempRoot, "manifest.json"),
                JsonSerializer.Serialize(manifest, JsonOptions),
                new UTF8Encoding(false));

            File.WriteAllText(
                Path.Combine(tempRoot, "profile.json"),
                JsonSerializer.Serialize(profile, JsonOptions),
                new UTF8Encoding(false));

            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            ZipFile.CreateFromDirectory(
                tempRoot,
                outputPath,
                CompressionLevel.Optimal,
                includeBaseDirectory: false);
        }
        finally
        {
            try
            {
                Directory.Delete(
                    tempRoot,
                    recursive: true);
            }
            catch
            {
            }
        }
    }

    public DmsUiProfile ImportProfile(
        string archivePath,
        string user)
    {
        using var archive = ZipFile.OpenRead(archivePath);

        var profileEntry = archive.GetEntry("profile.json")
                           ?? throw new InvalidOperationException(
                               "The archive does not contain profile.json.");

        DmsUiProfile? profile;

        using (var reader = new StreamReader(
                   profileEntry.Open(),
                   Encoding.UTF8,
                   detectEncodingFromByteOrderMarks: true))
        {
            profile = JsonSerializer.Deserialize<DmsUiProfile>(
                reader.ReadToEnd(),
                JsonOptions);
        }

        if (profile is null)
        {
            throw new InvalidOperationException(
                "The imported UI profile is empty or invalid.");
        }

        var baseCode = NormalizeCode(profile.Code);

        if (string.IsNullOrWhiteSpace(baseCode))
        {
            baseCode = "IMPORTED";
        }

        var code = baseCode;
        var suffix = 1;

        while (File.Exists(GetProfilePath(code)))
        {
            code = $"{baseCode}_IMPORT_{suffix:00}";
            suffix++;
        }

        profile.Code = code;
        profile.Name = string.IsNullOrWhiteSpace(profile.Name)
            ? code
            : profile.Name;
        profile.Version = Math.Max(1, profile.Version);
        profile.ModifiedBy = user;
        profile.ModifiedAt = DateTime.Now;

        SaveProfile(profile);
        return profile;
    }

    public string GetProfilePath(string code) =>
        Path.Combine(
            _profilesRoot,
            $"{NormalizeCode(code)}.json");

    public static string NormalizeCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var chars = value
            .Trim()
            .ToUpperInvariant()
            .Select(ch =>
                char.IsLetterOrDigit(ch) ||
                ch is '_' or '-'
                    ? ch
                    : '_')
            .ToArray();

        return new string(chars)
            .Trim('_');
    }

    private static DmsUiLayer NormalizeLayer(DmsUiLayer? layer)
    {
        layer ??= new DmsUiLayer();

        layer.Resources = new Dictionary<string, string>(
            layer.Resources
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .ToDictionary(
                    pair => pair.Key.Trim(),
                    pair => pair.Value?.Trim() ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        layer.Properties = layer.Properties
            .Where(item => item is not null)
            .Select(item =>
            {
                item.Id = string.IsNullOrWhiteSpace(item.Id)
                    ? Guid.NewGuid().ToString("N")
                    : item.Id.Trim();

                item.SelectorKind =
                    string.IsNullOrWhiteSpace(item.SelectorKind)
                        ? "TYPE"
                        : item.SelectorKind.Trim().ToUpperInvariant();

                item.Selector = item.Selector?.Trim() ?? string.Empty;
                item.Property = item.Property?.Trim() ?? string.Empty;
                item.Value = item.Value?.Trim() ?? string.Empty;

                return item;
            })
            .ToList();

        layer.AdvancedXaml ??= string.Empty;

        return layer;
    }

    private static DmsUiProfile ReadProfile(string path)
    {
        using var reader = new StreamReader(
            path,
            detectEncodingFromByteOrderMarks: true);

        var profile = JsonSerializer.Deserialize<DmsUiProfile>(
            reader.ReadToEnd(),
            JsonOptions)
            ?? throw new InvalidOperationException(
                $"UI profile is empty: {path}");

        profile.Global = NormalizeLayer(profile.Global);

        profile.Modules = new Dictionary<string, DmsUiLayer>(
            (profile.Modules ?? new Dictionary<string, DmsUiLayer>())
            .ToDictionary(
                pair => NormalizeCode(pair.Key),
                pair => NormalizeLayer(pair.Value),
                StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        profile.Transactions = new Dictionary<string, DmsUiLayer>(
            (profile.Transactions ?? new Dictionary<string, DmsUiLayer>())
            .ToDictionary(
                pair => NormalizeCode(pair.Key),
                pair => NormalizeLayer(pair.Value),
                StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        profile.Code = NormalizeCode(profile.Code);

        return profile;
    }

    private static void WriteJsonAtomic<T>(
        string path,
        T value)
    {
        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = path + $".tmp-{Guid.NewGuid():N}";
        var json = JsonSerializer.Serialize(value, JsonOptions);

        File.WriteAllText(
            temp,
            json,
            new UTF8Encoding(true));

        if (File.Exists(path))
        {
            var backup = path + ".bak";

            File.Copy(
                path,
                backup,
                overwrite: true);

            File.Move(
                temp,
                path,
                overwrite: true);
        }
        else
        {
            File.Move(temp, path);
        }
    }
}
