using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DMS.Core.Scheduling;

public sealed class ScheduledReportTemplateRepository
{
    private readonly string _filePath;

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

    public ScheduledReportTemplateRepository(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Report template file path is required.", nameof(filePath));

        _filePath = filePath;
    }

    public async Task<ScheduledReportTemplateFile> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
            return new ScheduledReportTemplateFile();

        await using var stream = File.OpenRead(_filePath);

        return await JsonSerializer.DeserializeAsync<ScheduledReportTemplateFile>(
                   stream,
                   JsonOptions,
                   cancellationToken)
               ?? new ScheduledReportTemplateFile();
    }

    public async Task SaveAsync(
        ScheduledReportTemplateFile data,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var tempPath = _filePath + ".tmp";

        await using (var stream = new FileStream(
                         tempPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                data,
                JsonOptions,
                cancellationToken);
        }

        File.Move(tempPath, _filePath, overwrite: true);
    }
}
