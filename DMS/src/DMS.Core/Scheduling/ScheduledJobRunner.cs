using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DMS.Core.Scheduling;

public sealed class ScheduledJobRunner
{
    private readonly IReadOnlyDictionary<string, IDmsScheduledReport> _reports;
    private readonly IReadOnlyDictionary<string, IReportDelivery> _deliveries;
    private readonly ScheduledReportTemplateRepository? _templateRepository;
    private readonly Action<string>? _log;

    // Compatibility constructor.
    public ScheduledJobRunner(
        IEnumerable<IDmsScheduledReport> reports,
        IEnumerable<IReportDelivery> deliveries,
        Action<string>? log = null)
        : this(
            reports,
            deliveries,
            templateRepository: null,
            log)
    {
    }

    // Current JOB10 / SchedulerRuntimeFactory constructor.
    public ScheduledJobRunner(
        IEnumerable<IDmsScheduledReport> reports,
        IEnumerable<IReportDelivery> deliveries,
        ScheduledReportTemplateRepository? templateRepository,
        Action<string>? log = null)
    {
        _reports =
            reports.ToDictionary(
                x => x.Code,
                StringComparer.OrdinalIgnoreCase);

        _deliveries =
            deliveries.ToDictionary(
                x => x.Type,
                StringComparer.OrdinalIgnoreCase);

        _templateRepository = templateRepository;
        _log = log;
    }

    public async Task<ScheduledJobRunResult> RunAsync(
        ScheduledJobDefinition job,
        DateTime scheduledAtLocal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        var startedUtc = DateTime.UtcNow;

        try
        {
            ScheduledReportTemplate? template = null;

            if (_templateRepository is not null
                && !string.IsNullOrWhiteSpace(
                    job.TemplateId))
            {
                var templateFile =
                    await _templateRepository.LoadAsync();

                template =
                    templateFile.Templates.FirstOrDefault(
                        x => string.Equals(
                            x.Id,
                            job.TemplateId,
                            StringComparison.OrdinalIgnoreCase));

                if (template is null)
                {
                    var templateError =
                        $"Template '{job.TemplateId}' was not found.";

                    Log(
                        $"JOB_FAIL Id={job.Id}; Error={templateError}");

                    return ScheduledJobRunResult.Fail(
                        job.Id,
                        startedUtc,
                        DateTime.UtcNow,
                        templateError);
                }
            }

            // JOB10 is template-driven. The job itself may intentionally have
            // an empty ReportCode; the executable report is defined by the
            // central template (ExecutorCode). Keep legacy fallbacks so older
            // jobs/templates continue to work.
            var reportCandidates =
                new[]
                {
                    template?.ExecutorCode,
                    job.ReportCode,
                    template?.Source
                }
                .Where(
                    x => !string.IsNullOrWhiteSpace(x))
                .Select(
                    x => x!.Trim())
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

            IDmsScheduledReport? report = null;
            string? executorCode = null;

            foreach (var candidate in reportCandidates)
            {
                if (_reports.TryGetValue(
                        candidate,
                        out report))
                {
                    executorCode = candidate;
                    break;
                }
            }

            Log(
                $"JOB_START Id={job.Id}; Name={job.Name}; TemplateId={job.TemplateId}; Executor={executorCode ?? "<none>"}; TemplateReport={template?.ReportCode ?? job.ReportCode}; Scheduled={scheduledAtLocal:O}");

            if (report is null)
            {
                var error =
                    $"No registered scheduler report executor found. " +
                    $"TemplateId='{job.TemplateId}', " +
                    $"ExecutorCode='{template?.ExecutorCode}', " +
                    $"JobReportCode='{job.ReportCode}', " +
                    $"TemplateSource='{template?.Source}'.";

                Log(
                    $"JOB_FAIL Id={job.Id}; Error={error}");

                return ScheduledJobRunResult.Fail(
                    job.Id,
                    startedUtc,
                    DateTime.UtcNow,
                    error);
            }

            var generated =
                await report.GenerateAsync(
                    new ReportGenerationContext
                    {
                        Job = job,
                        Template = template,
                        ScheduledAtLocal =
                            scheduledAtLocal,
                        StartedAtUtc =
                            startedUtc
                    },
                    cancellationToken);

            Log(
                $"REPORT_GENERATED JobId={job.Id}; Report={generated.ReportCode}; Rows={generated.RowCount}; Bytes={generated.Content.Length}");

            var destinations =
                new List<string>();

            var deliveryErrors =
                new List<string>();

            foreach (var definition in
                     job.Deliveries.Where(
                         x => x.IsEnabled))
            {
                cancellationToken
                    .ThrowIfCancellationRequested();

                if (!_deliveries.TryGetValue(
                        definition.Type,
                        out var delivery))
                {
                    var error =
                        $"Delivery '{definition.Type}' is not registered.";

                    deliveryErrors.Add(error);

                    Log(
                        $"DELIVERY_FAIL JobId={job.Id}; Type={definition.Type}; Error={error}");

                    continue;
                }

                try
                {
                    var result =
                        await delivery.DeliverAsync(
                            job,
                            template,
                            definition,
                            generated,
                            scheduledAtLocal,
                            cancellationToken);

                    if (!result.Success)
                    {
                        var error =
                            $"Delivery '{definition.Type}' failed: {result.Error}";

                        deliveryErrors.Add(
                            error);

                        Log(
                            $"DELIVERY_FAIL JobId={job.Id}; Type={definition.Type}; Destination={result.Destination}; Error={result.Error}");

                        continue;
                    }

                    destinations.Add(
                        result.Destination);

                    Log(
                        $"REPORT_DELIVERED JobId={job.Id}; Type={definition.Type}; Destination={result.Destination}");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    var error =
                        $"Delivery '{definition.Type}' failed: {ex.Message}";

                    deliveryErrors.Add(error);

                    Log(
                        $"DELIVERY_FAIL JobId={job.Id}; Type={definition.Type}; Error={ex.Message}");
                }
            }

            var finishedUtc =
                DateTime.UtcNow;

            if (deliveryErrors.Count > 0)
            {
                var error =
                    string.Join(
                        " | ",
                        deliveryErrors);

                Log(
                    $"JOB_PARTIAL_FAIL Id={job.Id}; Delivered={destinations.Count}; Failed={deliveryErrors.Count}; Error={error}");

                return ScheduledJobRunResult.Fail(
                    job.Id,
                    startedUtc,
                    finishedUtc,
                    error);
            }

            Log(
                $"JOB_OK Id={job.Id}; Delivered={destinations.Count}; DurationMs={(finishedUtc - startedUtc).TotalMilliseconds:0}");

            return ScheduledJobRunResult.Ok(
                job.Id,
                startedUtc,
                finishedUtc,
                destinations);
        }
        catch (OperationCanceledException)
        {
            Log(
                $"JOB_CANCEL Id={job.Id}");

            throw;
        }
        catch (Exception ex)
        {
            var finishedUtc =
                DateTime.UtcNow;

            Log(
                $"JOB_FAIL Id={job.Id}; Error={ex.Message}");

            return ScheduledJobRunResult.Fail(
                job.Id,
                startedUtc,
                finishedUtc,
                ex.Message);
        }
    }

    private void Log(
        string message) =>
        _log?.Invoke(message);
}

// IMPORTANT:
// ScheduledJobRunResult deliberately DOES NOT live in this file.
// The project already has Scheduling/ScheduledJobRunResult.cs.
// Keeping a second definition caused CS0101/CS0111/CS0229.
