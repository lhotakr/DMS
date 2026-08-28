using System.IO;

namespace DMS.Desktop.Configuration;

/// <summary>
/// Central policy for DMS shared storage paths.
///
/// The DMS client must not depend on per-user mapped drive letters such as
/// Z: or Y:. The canonical shared location is the UNC namespace below.
///
/// Legacy mapped paths under \SAP\DMS-db are transparently converted to UNC
/// at runtime, so older local/server configuration remains readable during
/// migration.
/// </summary>
public static class DmsStoragePathPolicy
{
    public const string DefaultShareRootPath = @"\\cze-sfs01\Data";
    public const string DmsStorageRelativePath = @"SAP\DMS-db";

    public static string DefaultStorageRootPath =>
        Path.Combine(
            DefaultShareRootPath,
            DmsStorageRelativePath);

    public static string GetEnvironmentRoot(
        string? environment)
    {
        var normalizedEnvironment =
            string.IsNullOrWhiteSpace(environment)
                ? "DEV"
                : environment.Trim().ToUpperInvariant();

        return Path.Combine(
            DefaultStorageRootPath,
            normalizedEnvironment);
    }

    /// <summary>
    /// Rewrites a legacy mapped DMS path such as
    /// Z:\SAP\DMS-db\DEV\Config
    /// or
    /// Y:\SAP\DMS-db\DEV\Config
    /// to
    /// \\cze-sfs01\Data\SAP\DMS-db\DEV\Config.
    ///
    /// Paths outside the DMS storage namespace are left unchanged.
    /// </summary>
    public static string CanonicalizeDmsPath(
        string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var value = Environment
            .ExpandEnvironmentVariables(path.Trim())
            .Replace('/', '\\');

        const string marker = @"\SAP\DMS-db";

        if (value.Length >= 3 &&
            char.IsLetter(value[0]) &&
            value[1] == ':' &&
            value[2] == '\\')
        {
            var markerIndex =
                value.IndexOf(
                    marker,
                    StringComparison.OrdinalIgnoreCase);

            if (markerIndex >= 2)
            {
                var relativeDmsPath =
                    value.Substring(markerIndex + 1);

                return Path.Combine(
                    DefaultShareRootPath,
                    relativeDmsPath);
            }
        }

        return value;
    }

    public static string GetEnvironmentRootFromConfigurationPath(
        string? configurationPath,
        string fallbackEnvironment = "DEV")
    {
        var canonical =
            CanonicalizeDmsPath(configurationPath);

        if (string.IsNullOrWhiteSpace(canonical))
        {
            return GetEnvironmentRoot(
                fallbackEnvironment);
        }

        var directory = canonical;

        if (Path.HasExtension(directory))
        {
            directory =
                Path.GetDirectoryName(directory)
                ?? string.Empty;
        }

        if (string.Equals(
                Path.GetFileName(directory),
                "Config",
                StringComparison.OrdinalIgnoreCase))
        {
            var parent =
                Directory.GetParent(directory);

            if (parent is not null)
            {
                return parent.FullName;
            }
        }

        return string.IsNullOrWhiteSpace(directory)
            ? GetEnvironmentRoot(fallbackEnvironment)
            : directory;
    }

    public static void Normalize(
        DmsAppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.Environment =
            string.IsNullOrWhiteSpace(settings.Environment)
                ? "DEV"
                : settings.Environment.Trim().ToUpperInvariant();

        settings.StorageRootPath =
            CanonicalizeDmsPath(
                settings.StorageRootPath);

        if (string.IsNullOrWhiteSpace(
                settings.StorageRootPath))
        {
            settings.StorageRootPath =
                DefaultStorageRootPath;
        }

        var environmentRoot =
            CanonicalizeDmsPath(
                settings.EnvironmentRootPath);

        if (string.IsNullOrWhiteSpace(
                environmentRoot))
        {
            var configuredRoot =
                GetEnvironmentRootFromConfigurationPath(
                    settings.ConfigurationRootPath,
                    settings.Environment);

            environmentRoot =
                !string.IsNullOrWhiteSpace(
                    configuredRoot)
                    ? configuredRoot
                    : Path.Combine(
                        settings.StorageRootPath,
                        settings.Environment);
        }

        // A legacy mapped root is always converted to the canonical UNC share.
        environmentRoot =
            CanonicalizeDmsPath(
                environmentRoot);

        settings.EnvironmentRootPath =
            environmentRoot;

        // These values are intentionally derived from the one environment root.
        // Older JSON properties stay supported, but cannot drift onto Z:/Y:.
        settings.ConfigurationRootPath =
            Path.Combine(
                environmentRoot,
                "Config");

        settings.DataRootPath =
            Path.Combine(
                environmentRoot,
                "Data");

        settings.DocumentsRootPath =
            Path.Combine(
                environmentRoot,
                "Documents");

        settings.LogsRootPath =
            Path.Combine(
                environmentRoot,
                "Logs");

        settings.BrandingRootPath =
            Path.Combine(
                environmentRoot,
                "Branding");

        settings.ArticlesDataPath =
            Path.Combine(
                settings.DataRootPath,
                "articles.json");
    }
}
