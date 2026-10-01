using Azure.Core;
using Azure.Identity;
using DMS.Core.Scheduling;
using DMS.Scheduler.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DMS.Scheduler.Delivery;

public sealed class SharePointReportDelivery : IReportDelivery
{
    private static readonly string[] AppGraphScopes =
        ["https://graph.microsoft.com/.default"];

    private static readonly string[] InteractiveGraphScopes =
        ["https://graph.microsoft.com/Sites.ReadWrite.All"];

    private readonly SharePointSettings _settings;
    private readonly HttpClient _http;
    private readonly Action<string>? _log;

    public SharePointReportDelivery(
        SharePointSettings settings,
        HttpClient? httpClient = null,
        Action<string>? log = null)
    {
        _settings =
            settings
            ?? throw new ArgumentNullException(
                nameof(settings));

        _http =
            httpClient
            ?? new HttpClient();

        _log = log;
    }

    public string Type => "SharePoint";

    public async Task<ReportDeliveryResult> DeliverAsync(
        ScheduledJobDefinition job,
        ScheduledReportTemplate? template,
        ScheduledJobDeliveryDefinition delivery,
        GeneratedReport report,
        DateTime executionLocalTime,
        CancellationToken cancellationToken = default)
    {
        var destination =
            string.IsNullOrWhiteSpace(
                delivery.TargetPath)
                ? "sharepoint"
                : delivery.TargetPath;

        try
        {
            if (!_settings.IsEnabled)
            {
                throw new InvalidOperationException(
                    "SharePoint delivery is disabled.");
            }

            var target =
                ResolveTarget(
                    delivery.TargetPath,
                    _settings.SiteUrl,
                    _settings.DocumentLibrary);

            var credential =
                CreateCredential();

            var isClientSecret =
                string.Equals(
                    _settings.AuthenticationMode,
                    "ClientSecret",
                    StringComparison.OrdinalIgnoreCase);

            var scopes =
                isClientSecret
                    ? AppGraphScopes
                    : InteractiveGraphScopes;

            _log?.Invoke(
                $"SP_AUTH_START JobId={job.Id}; Mode={_settings.AuthenticationMode}; " +
                $"Tenant={(string.IsNullOrWhiteSpace(_settings.TenantId) ? "<home>" : _settings.TenantId)}; " +
                $"ClientId={(string.IsNullOrWhiteSpace(_settings.ClientId) ? "<sdk-default>" : _settings.ClientId)}; " +
                $"Site={target.SiteUrl}");

            var token =
                await credential.GetTokenAsync(
                    new TokenRequestContext(
                        scopes),
                    cancellationToken);

            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    token.Token);

            var fileName =
                DeliveryTokenRenderer.GetFileName(
                    job,
                    template,
                    report,
                    executionLocalTime);

            _log?.Invoke(
                $"SP_UPLOAD_START JobId={job.Id}; Library={target.LibraryName}; " +
                $"Folder={target.FolderPath}; File={fileName}");

            var siteId =
                await ResolveSiteIdAsync(
                    target.SiteUrl,
                    cancellationToken);

            var driveId =
                await ResolveDriveIdAsync(
                    siteId,
                    target.LibraryName,
                    cancellationToken);

            if (!string.IsNullOrWhiteSpace(
                    target.FolderPath))
            {
                await EnsureFolderPathAsync(
                    driveId,
                    target.FolderPath,
                    cancellationToken);
            }

            var uploadSegments =
                string.IsNullOrWhiteSpace(
                    target.FolderPath)
                    ? new[] { fileName }
                    : target.FolderPath
                        .Split(
                            '/',
                            StringSplitOptions.RemoveEmptyEntries
                            | StringSplitOptions.TrimEntries)
                        .Append(fileName)
                        .ToArray();

            var encodedPath =
                string.Join(
                    '/',
                    uploadSegments.Select(
                        Uri.EscapeDataString));

            var uploadUrl =
                $"https://graph.microsoft.com/v1.0/drives/{Uri.EscapeDataString(driveId)}/root:/{encodedPath}:/content";

            using var content =
                new ByteArrayContent(
                    report.Content);

            content.Headers.ContentType =
                new MediaTypeHeaderValue(
                    report.ContentType);

            using var response =
                await _http.PutAsync(
                    uploadUrl,
                    content,
                    cancellationToken);

            var payload =
                await response.Content.ReadAsStringAsync(
                    cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Graph upload failed ({(int)response.StatusCode} {response.StatusCode}): {payload}");
            }

            using var json =
                JsonDocument.Parse(
                    payload);

            var webUrl =
                json.RootElement.TryGetProperty(
                    "webUrl",
                    out var property)
                    ? property.GetString()
                      ?? string.Empty
                    : string.Empty;

            var resultDestination =
                string.IsNullOrWhiteSpace(
                    webUrl)
                    ? $"sharepoint:{target.LibraryName}/{target.FolderPath}/{fileName}"
                    : webUrl;

            _log?.Invoke(
                $"SP_UPLOAD_OK JobId={job.Id}; Destination={resultDestination}");

            return ReportDeliveryResult.Ok(
                resultDestination);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var details =
                BuildExceptionMessage(
                    ex);

            _log?.Invoke(
                $"SP_UPLOAD_FAIL JobId={job.Id}; Target={destination}; Error={details}");

            return ReportDeliveryResult.Fail(
                destination,
                details);
        }
    }

    private TokenCredential CreateCredential()
    {
        if (string.Equals(
                _settings.AuthenticationMode,
                "ClientSecret",
                StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(
                    _settings.TenantId))
            {
                throw new InvalidOperationException(
                    "SharePoint TenantId is required for ClientSecret authentication.");
            }

            if (string.IsNullOrWhiteSpace(
                    _settings.ClientId))
            {
                throw new InvalidOperationException(
                    "SharePoint ClientId is required for ClientSecret authentication.");
            }

            var secret =
                Environment.GetEnvironmentVariable(
                    _settings.ClientSecretEnvironmentVariable)
                ?? string.Empty;

            if (string.IsNullOrWhiteSpace(
                    secret))
            {
                throw new InvalidOperationException(
                    $"SharePoint client secret environment variable '{_settings.ClientSecretEnvironmentVariable}' is empty.");
            }

            return new ClientSecretCredential(
                _settings.TenantId,
                _settings.ClientId,
                secret);
        }

        if (string.Equals(
                _settings.AuthenticationMode,
                "InteractiveBrowser",
                StringComparison.OrdinalIgnoreCase))
        {
            var options =
                new InteractiveBrowserCredentialOptions
                {
                    TokenCachePersistenceOptions =
                        new TokenCachePersistenceOptions
                        {
                            Name =
                                "DMS.Scheduler.SharePoint"
                        }
                };

            // TenantId is optional for interactive login.
            // When omitted Azure.Identity authenticates against the user's home tenant.
            if (!string.IsNullOrWhiteSpace(
                    _settings.TenantId))
            {
                options.TenantId =
                    _settings.TenantId;
            }

            // A custom ClientId is recommended, but Azure.Identity can use
            // its SDK development application when none is supplied.
            if (!string.IsNullOrWhiteSpace(
                    _settings.ClientId))
            {
                options.ClientId =
                    _settings.ClientId;
            }

            return new InteractiveBrowserCredential(
                options);
        }

        throw new InvalidOperationException(
            $"Unsupported SharePoint AuthenticationMode '{_settings.AuthenticationMode}'. " +
            "Use InteractiveBrowser or ClientSecret.");
    }

    private async Task<string> ResolveSiteIdAsync(
        string siteUrl,
        CancellationToken cancellationToken)
    {
        var uri =
            new Uri(
                siteUrl);

        var sitePath =
            uri.AbsolutePath.TrimEnd('/');

        var url =
            $"https://graph.microsoft.com/v1.0/sites/{uri.Host}:{sitePath}";

        using var response =
            await _http.GetAsync(
                url,
                cancellationToken);

        var payload =
            await response.Content.ReadAsStringAsync(
                cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Could not resolve SharePoint site ({(int)response.StatusCode} {response.StatusCode}): {payload}");
        }

        using var json =
            JsonDocument.Parse(
                payload);

        return json.RootElement
                   .GetProperty("id")
                   .GetString()
               ?? throw new InvalidOperationException(
                   "Microsoft Graph did not return a SharePoint site id.");
    }

    private async Task<string> ResolveDriveIdAsync(
        string siteId,
        string libraryName,
        CancellationToken cancellationToken)
    {
        using var response =
            await _http.GetAsync(
                $"https://graph.microsoft.com/v1.0/sites/{Uri.EscapeDataString(siteId)}/drives",
                cancellationToken);

        var payload =
            await response.Content.ReadAsStringAsync(
                cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Could not list SharePoint document libraries ({(int)response.StatusCode} {response.StatusCode}): {payload}");
        }

        using var json =
            JsonDocument.Parse(
                payload);

        foreach (var drive in
                 json.RootElement
                     .GetProperty("value")
                     .EnumerateArray())
        {
            var name =
                drive.GetProperty("name")
                    .GetString();

            if (string.Equals(
                    name,
                    libraryName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return drive
                           .GetProperty("id")
                           .GetString()
                       ?? throw new InvalidOperationException(
                           $"Document library '{libraryName}' has no drive id.");
            }
        }

        throw new InvalidOperationException(
            $"SharePoint document library '{libraryName}' was not found.");
    }

    private async Task EnsureFolderPathAsync(
        string driveId,
        string folderPath,
        CancellationToken cancellationToken)
    {
        var current =
            string.Empty;

        foreach (var segment in
                 folderPath.Split(
                     '/',
                     StringSplitOptions.RemoveEmptyEntries
                     | StringSplitOptions.TrimEntries))
        {
            var parent =
                current;

            current =
                string.IsNullOrWhiteSpace(
                    current)
                    ? segment
                    : $"{current}/{segment}";

            var encodedCurrent =
                string.Join(
                    '/',
                    current.Split('/')
                        .Select(
                            Uri.EscapeDataString));

            using var check =
                await _http.GetAsync(
                    $"https://graph.microsoft.com/v1.0/drives/{Uri.EscapeDataString(driveId)}/root:/{encodedCurrent}",
                    cancellationToken);

            if (check.IsSuccessStatusCode)
                continue;

            var childrenUrl =
                string.IsNullOrWhiteSpace(
                    parent)
                    ? $"https://graph.microsoft.com/v1.0/drives/{Uri.EscapeDataString(driveId)}/root/children"
                    : $"https://graph.microsoft.com/v1.0/drives/{Uri.EscapeDataString(driveId)}/root:/{string.Join('/', parent.Split('/').Select(Uri.EscapeDataString))}:/children";

            var body =
                new Dictionary<string, object?>
                {
                    ["name"] =
                        segment,
                    ["folder"] =
                        new { },
                    ["@microsoft.graph.conflictBehavior"] =
                        "fail"
                };

            using var create =
                await _http.PostAsJsonAsync(
                    childrenUrl,
                    body,
                    cancellationToken);

            if (!create.IsSuccessStatusCode
                && create.StatusCode
                    != HttpStatusCode.Conflict)
            {
                var payload =
                    await create.Content.ReadAsStringAsync(
                        cancellationToken);

                throw new InvalidOperationException(
                    $"Could not create SharePoint folder '{current}' ({(int)create.StatusCode} {create.StatusCode}): {payload}");
            }
        }
    }

    private static SharePointTarget ResolveTarget(
        string targetPath,
        string defaultSiteUrl,
        string defaultLibrary)
    {
        if (Uri.TryCreate(
                targetPath,
                UriKind.Absolute,
                out var fullUri)
            && (fullUri.Scheme
                    == Uri.UriSchemeHttps
                || fullUri.Scheme
                    == Uri.UriSchemeHttp))
        {
            var query =
                ParseQueryString(
                    fullUri.Query);

            if (query.TryGetValue(
                    "id",
                    out var idValue)
                && !string.IsNullOrWhiteSpace(
                    idValue))
            {
                var decodedPath =
                    Uri.UnescapeDataString(
                        idValue);

                return ResolveFromSharePointPath(
                    fullUri,
                    decodedPath,
                    defaultLibrary);
            }

            return ResolveFromSharePointPath(
                fullUri,
                Uri.UnescapeDataString(
                    fullUri.AbsolutePath),
                defaultLibrary);
        }

        if (string.IsNullOrWhiteSpace(
                defaultSiteUrl))
        {
            throw new InvalidOperationException(
                "SharePoint TargetPath is not a full URL and global SiteUrl is empty.");
        }

        return new SharePointTarget(
            defaultSiteUrl.TrimEnd('/'),
            string.IsNullOrWhiteSpace(
                defaultLibrary)
                ? "Shared Documents"
                : defaultLibrary,
            NormalizeFolderPath(
                targetPath));
    }

    private static SharePointTarget ResolveFromSharePointPath(
        Uri sourceUri,
        string path,
        string defaultLibrary)
    {
        var segments =
            path.Split(
                    '/',
                    StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries)
                .Select(
                    Uri.UnescapeDataString)
                .ToArray();

        var containerIndex =
            Array.FindIndex(
                segments,
                x => string.Equals(
                         x,
                         "sites",
                         StringComparison.OrdinalIgnoreCase)
                     || string.Equals(
                         x,
                         "teams",
                         StringComparison.OrdinalIgnoreCase));

        if (containerIndex < 0
            || containerIndex + 1
                >= segments.Length)
        {
            throw new InvalidOperationException(
                "The SharePoint URL does not contain /sites/<site> or /teams/<site>.");
        }

        var siteUrl =
            $"{sourceUri.Scheme}://{sourceUri.Host}/{segments[containerIndex]}/{segments[containerIndex + 1]}";

        var library =
            containerIndex + 2
                < segments.Length
                ? segments[containerIndex + 2]
                : defaultLibrary;

        if (string.Equals(
                library,
                "Forms",
                StringComparison.OrdinalIgnoreCase))
        {
            library =
                defaultLibrary;
        }

        if (string.IsNullOrWhiteSpace(
                library))
        {
            library =
                "Shared Documents";
        }

        var folderStart =
            containerIndex + 3;

        var folder =
            folderStart
                < segments.Length
                ? string.Join(
                    '/',
                    segments.Skip(
                        folderStart))
                : string.Empty;

        if (folder.StartsWith(
                "Forms/",
                StringComparison.OrdinalIgnoreCase))
        {
            folder =
                string.Empty;
        }

        return new SharePointTarget(
            siteUrl,
            library,
            NormalizeFolderPath(
                folder));
    }

    private static Dictionary<string, string> ParseQueryString(
        string query)
    {
        var result =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var pair in
                 query.TrimStart('?')
                     .Split(
                         '&',
                         StringSplitOptions.RemoveEmptyEntries))
        {
            var parts =
                pair.Split(
                    '=',
                    2);

            var key =
                Uri.UnescapeDataString(
                    parts[0]);

            var value =
                parts.Length > 1
                    ? Uri.UnescapeDataString(
                        parts[1])
                    : string.Empty;

            result[key] =
                value;
        }

        return result;
    }

    private static string NormalizeFolderPath(
        string? value) =>
        (value ?? string.Empty)
            .Replace(
                '\\',
                '/')
            .Trim('/');

    private static string BuildExceptionMessage(
        Exception exception)
    {
        var parts =
            new List<string>();

        Exception? current =
            exception;

        while (current is not null)
        {
            if (!string.IsNullOrWhiteSpace(
                    current.Message))
            {
                parts.Add(
                    current.Message.Trim());
            }

            current =
                current.InnerException;
        }

        return string.Join(
            " --> ",
            parts.Distinct());
    }

    private sealed record SharePointTarget(
        string SiteUrl,
        string LibraryName,
        string FolderPath);
}
