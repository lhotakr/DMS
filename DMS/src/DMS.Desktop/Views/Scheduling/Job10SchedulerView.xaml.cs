using DMS.Core.Scheduling;
using DMS.Desktop.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.Scheduling;

public partial class Job10SchedulerView : UserControl
{
    private readonly string _jobsPath;
    private readonly string _templatesPath;
    private readonly string _mesSettingsPath;
    private readonly DmsLogger _logger;
    private readonly string _user;
    private readonly Func<string, string> _translate;

    private readonly ScheduledJobRepository _jobRepository;
    private readonly ScheduledReportTemplateRepository _templateRepository;

    private ScheduledJobFile _jobFile = new();
    private ScheduledReportTemplateFile _templateFile = new();

    private ScheduledJobDefinition? _selectedJob;
    private ScheduledJobDeliveryDefinition? _selectedDelivery;
    private bool _uiUpdating;

    public Job10SchedulerView(
        string jobsPath,
        string templatesPath,
        string mesSettingsPath,
        DmsLogger logger,
        string user,
        Func<string, string>? translate = null)
    {
        InitializeComponent();

        _jobsPath = jobsPath
            ?? throw new ArgumentNullException(nameof(jobsPath));

        _templatesPath = templatesPath
            ?? throw new ArgumentNullException(nameof(templatesPath));

        _mesSettingsPath = mesSettingsPath
            ?? throw new ArgumentNullException(nameof(mesSettingsPath));

        _logger = logger
            ?? throw new ArgumentNullException(nameof(logger));

        _user = user ?? string.Empty;
        _translate = translate ?? (key => key);

        _jobRepository =
            new ScheduledJobRepository(
                _jobsPath);

        _templateRepository =
            new ScheduledReportTemplateRepository(
                _templatesPath);

        ApplyLocalization();

        Loaded += Job10SchedulerView_Loaded;
    }

    private string T(
        string key,
        string fallback)
    {
        var translated =
            _translate(key);

        if (string.IsNullOrWhiteSpace(translated))
            return fallback;

        translated =
            translated.Trim();

        // Localizer returns [[KEY]] when a translation is missing.
        // JOB10 must show its explicit fallback text instead of the placeholder.
        if (string.Equals(
                translated,
                key,
                StringComparison.Ordinal)
            || string.Equals(
                translated,
                $"[[{key}]]",
                StringComparison.Ordinal)
            || (translated.StartsWith(
                    "[[",
                    StringComparison.Ordinal)
                && translated.EndsWith(
                    "]]",
                    StringComparison.Ordinal)))
        {
            return fallback;
        }

        return translated;
    }

    private async void Job10SchedulerView_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        Loaded -= Job10SchedulerView_Loaded;
        await LoadAsync();
    }

    private async Task LoadAsync(
        string? selectJobId = null)
    {
        try
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(_jobsPath)
                ?? AppContext.BaseDirectory);

            _jobFile =
                await _jobRepository.LoadAsync();

            _templateFile =
                await _templateRepository.LoadAsync();

            _uiUpdating = true;

            try
            {
                CmbTemplate.ItemsSource =
                    _templateFile.Templates
                        .OrderBy(
                            x => x.Name,
                            StringComparer.CurrentCultureIgnoreCase)
                        .ToList();

                var rows =
                    _jobFile.Jobs
                        .OrderBy(
                            x => x.Name,
                            StringComparer.CurrentCultureIgnoreCase)
                        .Select(
                            x =>
                                new Job10JobRow
                                {
                                    Id = x.Id,
                                    IsEnabled = x.IsEnabled,
                                    Name = x.Name,
                                    TemplateName =
                                        ResolveTemplateName(
                                            x.TemplateId),
                                    CronExpression =
                                        x.CronExpression
                                })
                        .ToList();

                GridJobs.ItemsSource =
                    rows;

                Job10JobRow? selectedRow = null;

                if (!string.IsNullOrWhiteSpace(selectJobId))
                {
                    selectedRow =
                        rows.FirstOrDefault(
                            x =>
                                string.Equals(
                                    x.Id,
                                    selectJobId,
                                    StringComparison.OrdinalIgnoreCase));
                }

                selectedRow ??=
                    rows.FirstOrDefault();

                GridJobs.SelectedItem =
                    selectedRow;
            }
            finally
            {
                _uiUpdating = false;
            }

            if (GridJobs.SelectedItem is Job10JobRow row)
            {
                SelectJobById(
                    row.Id);
            }
            else
            {
                _selectedJob = null;
                ClearEditor();
            }

            TxtStatus.Text =
                string.Format(
                    T(
                        "JOB10.Status.LoadedV6",
                        "Loaded jobs: {0}; templates: {1}."),
                    _jobFile.Jobs.Count,
                    _templateFile.Templates.Count);

            TxtJobsFile.Text =
                $"{T("JOB10.JobsFile", "Jobs file")}: {_jobsPath}";
        }
        catch (Exception ex)
        {
            _logger.Error(
                "JOB10 v6 load failed.",
                ex);

            TxtStatus.Text =
                ex.Message;
        }
    }

    private string ResolveTemplateName(
        string templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            return T(
                "JOB10.Template.Legacy",
                "(legacy job)");
        }

        return _templateFile.Templates
                   .FirstOrDefault(
                       x =>
                           string.Equals(
                               x.Id,
                               templateId,
                               StringComparison.OrdinalIgnoreCase))
                   ?.Name
               ?? T(
                   "JOB10.Template.Missing",
                   "(missing template)");
    }

    private void GridJobs_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_uiUpdating)
            return;

        if (GridJobs.SelectedItem
            is not Job10JobRow row)
        {
            _selectedJob = null;
            ClearEditor();
            return;
        }

        SelectJobById(
            row.Id);
    }

    private void SelectJobById(
        string id)
    {
        _selectedJob =
            _jobFile.Jobs.FirstOrDefault(
                x =>
                    string.Equals(
                        x.Id,
                        id,
                        StringComparison.OrdinalIgnoreCase));

        if (_selectedJob is null)
        {
            ClearEditor();
            return;
        }

        PopulateEditor(
            _selectedJob);
    }

    private void PopulateEditor(
        ScheduledJobDefinition job)
    {
        _uiUpdating = true;

        try
        {
            ChkEnabled.IsChecked =
                job.IsEnabled;

            TxtName.Text =
                job.Name;

            TxtCron.Text =
                job.CronExpression;

            TxtTimeZone.Text =
                string.IsNullOrWhiteSpace(job.TimeZoneId)
                    ? "Europe/Prague"
                    : job.TimeZoneId;

            TxtFileNameTemplate.Text =
                string.IsNullOrWhiteSpace(job.OutputFileNameTemplate)
                    ? "{TemplateName}_{yyyy-MM-dd}_{HHmmss}.{Extension}"
                    : job.OutputFileNameTemplate;

            CmbTemplate.SelectedItem =
                _templateFile.Templates.FirstOrDefault(
                    x =>
                        string.Equals(
                            x.Id,
                            job.TemplateId,
                            StringComparison.OrdinalIgnoreCase));

            LoadDeliveries(job);
        }
        finally
        {
            _uiUpdating = false;
        }

        UpdateTemplateSummary();
        UpdateNextRun();
    }

    private void ClearEditor()
    {
        _uiUpdating = true;

        try
        {
            ChkEnabled.IsChecked = false;
            TxtName.Text = string.Empty;
            CmbTemplate.SelectedItem = null;
            TxtCron.Text = "0 6 * * 1-5";
            TxtTimeZone.Text = "Europe/Prague";
            TxtFileNameTemplate.Text =
                "{TemplateName}_{yyyy-MM-dd}_{HHmmss}.{Extension}";

            _selectedDelivery = null;
            LstDeliveries.ItemsSource = null;
            ClearDeliveryEditor();
        }
        finally
        {
            _uiUpdating = false;
        }

        UpdateTemplateSummary();
        UpdateNextRun();
    }

    private async void BtnAdd_Click(
        object sender,
        RoutedEventArgs e)
    {
        var template =
            CmbTemplate.SelectedItem
                as ScheduledReportTemplate
            ?? _templateFile.Templates
                .OrderBy(
                    x => x.Name,
                    StringComparer.CurrentCultureIgnoreCase)
                .FirstOrDefault();

        var job =
            new ScheduledJobDefinition
            {
                Name =
                    T(
                        "JOB10.NewJob",
                        "New scheduled job"),
                TemplateId =
                    template?.Id
                    ?? string.Empty,
                CronExpression =
                    "0 6 * * 1-5",
                TimeZoneId =
                    "Europe/Prague",
                IsEnabled =
                    false,
                OutputFileNameTemplate =
                    "{TemplateName}_{yyyy-MM-dd}_{HHmmss}.{Extension}",
                Deliveries =
                    new List<ScheduledJobDeliveryDefinition>
                    {
                        new()
                        {
                            Type =
                                "ServerFolder",
                            IsEnabled =
                                true,
                            TargetPath =
                                Path.Combine(
                                    Path.GetDirectoryName(
                                        _jobsPath)
                                    ?? string.Empty,
                                    "Output")
                        }
                    }
            };

        _jobFile.Jobs.Add(
            job);

        await _jobRepository.SaveAsync(
            _jobFile);

        _logger.AdminAction(
            "JOB10",
            "AUDIT_CREATE",
            _user,
            $"JobId={job.Id}; Name={job.Name}; TemplateId={job.TemplateId}");

        await LoadAsync(
            job.Id);
    }

    private async void BtnDelete_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_selectedJob is null)
            return;

        var deleted =
            _selectedJob;

        _jobFile.Jobs.RemoveAll(
            x =>
                string.Equals(
                    x.Id,
                    deleted.Id,
                    StringComparison.OrdinalIgnoreCase));

        await _jobRepository.SaveAsync(
            _jobFile);

        _logger.AdminAction(
            "JOB10",
            "AUDIT_DELETE",
            _user,
            $"JobId={deleted.Id}; Name={deleted.Name}; TemplateId={deleted.TemplateId}");

        _selectedJob = null;

        await LoadAsync();
    }

    private async void BtnSave_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_selectedJob is null)
            return;

        try
        {
            var before =
                DescribeJob(
                    _selectedJob);

            ApplyEditorToSelected();

            CronSchedule.Parse(
                _selectedJob.CronExpression);

            if (string.IsNullOrWhiteSpace(
                    _selectedJob.TemplateId))
            {
                throw new InvalidOperationException(
                    T(
                        "JOB10.Template.Required",
                        "Select a report template."));
            }

            await _jobRepository.SaveAsync(
                _jobFile);

            _logger.AdminAction(
                "JOB10",
                "AUDIT",
                _user,
                $"JobId={_selectedJob.Id}; Before={before}; After={DescribeJob(_selectedJob)}");

            TxtStatus.Text =
                T(
                    "JOB10.Status.Saved",
                    "Job saved.");

            await LoadAsync(
                _selectedJob.Id);
        }
        catch (Exception ex)
        {
            TxtStatus.Text =
                ex.Message;
        }
    }

    private async void BtnRunNow_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_selectedJob is null)
            return;

        try
        {
            BtnRunNow.IsEnabled = false;

            ApplyEditorToSelected();

            CronSchedule.Parse(
                _selectedJob.CronExpression);

            if (string.IsNullOrWhiteSpace(
                    _selectedJob.TemplateId))
            {
                throw new InvalidOperationException(
                    T(
                        "JOB10.Template.Required",
                        "Select a report template."));
            }

            await _jobRepository.SaveAsync(
                _jobFile);

            _logger.AdminAction(
                "JOB10",
                "RunNow",
                _user,
                $"JobId={_selectedJob.Id}; Name={_selectedJob.Name}; TemplateId={_selectedJob.TemplateId}");

            var startInfo =
                CreateSchedulerProcessStartInfo(
                    _selectedJob.Id);

            using var process =
                Process.Start(
                    startInfo)
                ?? throw new InvalidOperationException(
                    "Could not start DMS.Scheduler.");

            var stdoutTask =
                process.StandardOutput.ReadToEndAsync();

            var stderrTask =
                process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            var stdout =
                await stdoutTask;

            var stderr =
                await stderrTask;

            if (process.ExitCode == 0)
            {
                TxtStatus.Text =
                    T(
                        "JOB10.Status.RunOk",
                        "Job finished successfully.");
            }
            else
            {
                var diagnostic =
                    string.Join(
                        Environment.NewLine,
                        new[]
                        {
                stdout?.Trim(),
                stderr?.Trim()
                        }
                        .Where(x => !string.IsNullOrWhiteSpace(x)));

                TxtStatus.Text =
                    $"{T("JOB10.Status.RunFailed", "Job failed.")} " +
                    $"ExitCode={process.ExitCode}" +
                    (string.IsNullOrWhiteSpace(diagnostic)
                        ? string.Empty
                        : Environment.NewLine + diagnostic);
            }

            if (!string.IsNullOrWhiteSpace(stdout))
            {
                _logger.Info(
                    $"JOB10 scheduler output: {stdout.Trim()}");
            }

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                _logger.Warning(
                    $"JOB10 scheduler error output: {stderr.Trim()}");
            }

            TxtStatus.Text =
                process.ExitCode == 0
                    ? T(
                        "JOB10.Status.RunOk",
                        "Job finished successfully.")
                    : $"{T("JOB10.Status.RunFailed", "Job failed.")} ExitCode={process.ExitCode}; {stderr}";
        }
        catch (Exception ex)
        {
            _logger.Error(
                "JOB10 Run now failed.",
                ex);

            TxtStatus.Text =
                ex.Message;
        }
        finally
        {
            BtnRunNow.IsEnabled =
                true;
        }
    }

    private void ApplyEditorToSelected()
    {
        if (_selectedJob is null)
            return;

        _selectedJob.IsEnabled =
            ChkEnabled.IsChecked == true;

        _selectedJob.Name =
            TxtName.Text.Trim();

        _selectedJob.TemplateId =
            (CmbTemplate.SelectedItem
                as ScheduledReportTemplate)
            ?.Id
            ?? string.Empty;

        _selectedJob.CronExpression =
            TxtCron.Text.Trim();

        _selectedJob.TimeZoneId =
            TxtTimeZone.Text.Trim();

        _selectedJob.OutputFileNameTemplate =
            TxtFileNameTemplate.Text.Trim();

        SaveSelectedDeliveryEditor();
    }

    private void LoadDeliveries(ScheduledJobDefinition job)
    {
        _selectedDelivery = null;
        var rows = job.Deliveries.Select((definition, index) => new Job10DeliveryRow(definition, index)).ToList();
        LstDeliveries.ItemsSource = rows;
        LstDeliveries.SelectedItem = rows.FirstOrDefault();
        _selectedDelivery = (LstDeliveries.SelectedItem as Job10DeliveryRow)?.Definition;
        LoadSelectedDeliveryEditor();
    }

    private void RefreshDeliveryList(ScheduledJobDeliveryDefinition? select = null)
    {
        if (_selectedJob is null)
        {
            LstDeliveries.ItemsSource = null;
            ClearDeliveryEditor();
            return;
        }

        var rows = _selectedJob.Deliveries.Select((definition, index) => new Job10DeliveryRow(definition, index)).ToList();
        LstDeliveries.ItemsSource = rows;
        var selected = select is null ? rows.FirstOrDefault() : rows.FirstOrDefault(x => ReferenceEquals(x.Definition, select));
        LstDeliveries.SelectedItem = selected;
        _selectedDelivery = selected?.Definition;
        LoadSelectedDeliveryEditor();
    }

    private void LstDeliveries_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_uiUpdating)
            return;
        SaveSelectedDeliveryEditor();
        _selectedDelivery = (LstDeliveries.SelectedItem as Job10DeliveryRow)?.Definition;
        LoadSelectedDeliveryEditor();
    }

    private void LoadSelectedDeliveryEditor()
    {
        _uiUpdating = true;
        try
        {
            ClearDeliveryEditor();
            if (_selectedDelivery is null)
                return;

            ChkDeliveryEnabled.IsChecked = _selectedDelivery.IsEnabled;
            TxtDeliveryType.Text = GetDeliveryDisplayName(_selectedDelivery.Type);

            if (IsDeliveryType(_selectedDelivery, "ServerFolder"))
            {
                PanelServerDelivery.Visibility = Visibility.Visible;
                TxtTargetPath.Text = _selectedDelivery.TargetPath;
            }
            else if (IsDeliveryType(_selectedDelivery, "Email"))
            {
                PanelEmailDelivery.Visibility = Visibility.Visible;
                TxtEmailRecipients.Text = string.Join("; ", _selectedDelivery.Recipients);
                TxtEmailSubject.Text = _selectedDelivery.SubjectTemplate;
            }
            else if (IsDeliveryType(_selectedDelivery, "SharePoint"))
            {
                PanelSharePointDelivery.Visibility = Visibility.Visible;
                TxtSharePointTarget.Text = _selectedDelivery.TargetPath;
            }
        }
        finally
        {
            _uiUpdating = false;
        }
    }

    private void ClearDeliveryEditor()
    {
        ChkDeliveryEnabled.IsChecked = false;
        TxtDeliveryType.Text = "—";
        PanelServerDelivery.Visibility = Visibility.Collapsed;
        PanelEmailDelivery.Visibility = Visibility.Collapsed;
        PanelSharePointDelivery.Visibility = Visibility.Collapsed;
        TxtTargetPath.Text = string.Empty;
        TxtEmailRecipients.Text = string.Empty;
        TxtEmailSubject.Text = string.Empty;
        TxtSharePointTarget.Text = string.Empty;
    }

    private void SaveSelectedDeliveryEditor()
    {
        if (_uiUpdating || _selectedDelivery is null)
            return;

        _selectedDelivery.IsEnabled = ChkDeliveryEnabled.IsChecked == true;

        if (IsDeliveryType(_selectedDelivery, "ServerFolder"))
            _selectedDelivery.TargetPath = TxtTargetPath.Text.Trim();
        else if (IsDeliveryType(_selectedDelivery, "Email"))
        {
            _selectedDelivery.Recipients = TxtEmailRecipients.Text
                .Split(new[] { ';', ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            _selectedDelivery.SubjectTemplate = TxtEmailSubject.Text.Trim();
        }
        else if (IsDeliveryType(_selectedDelivery, "SharePoint"))
            _selectedDelivery.TargetPath = TxtSharePointTarget.Text.Trim();
    }

    private void BtnAddServerDelivery_Click(object sender, RoutedEventArgs e)
    {
        AddDelivery(new ScheduledJobDeliveryDefinition
        {
            Type = "ServerFolder",
            IsEnabled = true,
            TargetPath = Path.Combine(Path.GetDirectoryName(_jobsPath) ?? string.Empty, "Output")
        });
    }

    private void BtnAddEmailDelivery_Click(object sender, RoutedEventArgs e)
    {
        AddDelivery(new ScheduledJobDeliveryDefinition
        {
            Type = "Email",
            IsEnabled = true,
            SubjectTemplate = "DMS report - {ReportCode} - {yyyy-MM-dd}"
        });
    }

    private void BtnAddSharePointDelivery_Click(object sender, RoutedEventArgs e)
    {
        AddDelivery(new ScheduledJobDeliveryDefinition { Type = "SharePoint", IsEnabled = true });
    }

    private void AddDelivery(ScheduledJobDeliveryDefinition delivery)
    {
        if (_selectedJob is null)
            return;
        SaveSelectedDeliveryEditor();
        _selectedJob.Deliveries.Add(delivery);
        RefreshDeliveryList(delivery);
    }

    private void BtnDuplicateDelivery_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedJob is null || _selectedDelivery is null)
            return;
        SaveSelectedDeliveryEditor();
        var copy = new ScheduledJobDeliveryDefinition
        {
            Type = _selectedDelivery.Type,
            IsEnabled = _selectedDelivery.IsEnabled,
            TargetPath = _selectedDelivery.TargetPath,
            SubjectTemplate = _selectedDelivery.SubjectTemplate,
            Recipients = _selectedDelivery.Recipients.ToList()
        };
        _selectedJob.Deliveries.Add(copy);
        RefreshDeliveryList(copy);
    }

    private void BtnDeleteDelivery_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedJob is null || _selectedDelivery is null)
            return;
        _selectedJob.Deliveries.Remove(_selectedDelivery);
        _selectedDelivery = null;
        RefreshDeliveryList();
    }

    private static bool IsDeliveryType(ScheduledJobDeliveryDefinition delivery, string type) =>
        string.Equals(delivery.Type, type, StringComparison.OrdinalIgnoreCase);

    private static string GetDeliveryDisplayName(string type) =>
        type switch
        {
            "ServerFolder" => "Server folder",
            "Email" => "E-mail",
            "SharePoint" => "SharePoint",
            _ => type
        };

    private void CmbTemplate_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_uiUpdating)
            return;

        UpdateTemplateSummary();
    }

    private void UpdateTemplateSummary()
    {
        var template =
            CmbTemplate.SelectedItem
                as ScheduledReportTemplate;

        if (template is null)
        {
            TxtSummarySource.Text = "—";
            TxtSummaryReport.Text = "—";
            TxtSummaryPeriod.Text = "—";
            TxtSummaryWorkcenters.Text = "—";
            TxtSummaryFilters.Text = "—";
            return;
        }

        TxtSummarySource.Text =
            template.Source;

        TxtSummaryReport.Text =
            template.ReportCode;

        TxtSummaryPeriod.Text =
            BuildPeriodSummary(
                template);

        TxtSummaryWorkcenters.Text =
            template.SelectAllWorkcenters
                ? T("JOB10.Template.AllWorkcenters", "All")
                : string.Join(
                    Environment.NewLine,
                    template.WorkcenterCodes
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => $"• {x.Trim()}"));


        var filters =
            new List<string>();

        if (!string.IsNullOrWhiteSpace(template.ShiftCode))
            filters.Add($"Shift={template.ShiftCode}");

        if (!string.IsNullOrWhiteSpace(template.Article))
            filters.Add($"Article={template.Article}");

        if (!string.IsNullOrWhiteSpace(template.Order))
            filters.Add($"Order={template.Order}");

        if (!string.IsNullOrWhiteSpace(template.Operation))
            filters.Add($"Operation={template.Operation}");

        TxtSummaryFilters.Text =
            filters.Count == 0
                ? T(
                    "JOB10.Template.NoExtraFilters",
                    "None")
                : string.Join(
                    "; ",
                    filters);
    }

    private string BuildPeriodSummary(
        ScheduledReportTemplate template)
    {
        var quick =
            template.QuickPeriodCode?.Trim()
            ?? string.Empty;

        var label =
            quick.ToUpperInvariant() switch
            {
                "TODAY" =>
                    T(
                        "JOB10.Period.Today",
                        "Today"),
                "YESTERDAY" =>
                    T(
                        "JOB10.Period.Yesterday",
                        "Yesterday"),
                "LAST7" =>
                    T(
                        "JOB10.Period.Last7",
                        "Last 7 days"),
                "THISWEEK" =>
                    T(
                        "JOB10.Period.ThisWeek",
                        "This week"),
                _ =>
                    template.From.HasValue
                    && template.To.HasValue
                        ? $"{template.From:dd.MM.yyyy} – {template.To:dd.MM.yyyy}"
                        : T(
                            "JOB10.Period.Custom",
                            "Custom")
            };

        if (!string.IsNullOrWhiteSpace(template.TimeFrom)
            || !string.IsNullOrWhiteSpace(template.TimeTo))
        {
            label +=
                $" {template.TimeFrom}–{template.TimeTo}";
        }

        return label;
    }

    private void TxtCron_LostFocus(
        object sender,
        RoutedEventArgs e)
    {
        UpdateNextRun();
    }

    private void UpdateNextRun()
    {
        try
        {
            var schedule =
                CronSchedule.Parse(
                    TxtCron.Text.Trim());

            var timeZone =
                SchedulerTimeZone.Resolve(
                    TxtTimeZone.Text.Trim());

            var nowLocal =
                TimeZoneInfo.ConvertTimeFromUtc(
                    DateTime.UtcNow,
                    timeZone);

            var next =
                schedule.GetNextOccurrence(
                    DateTime.SpecifyKind(
                        nowLocal,
                        DateTimeKind.Unspecified));

            TxtNextRun.Text =
                next.HasValue
                    ? $"{T("JOB10.NextRun", "Next run")}: {next.Value:dd.MM.yyyy HH:mm} ({timeZone.Id})"
                    : T(
                        "JOB10.NextRun.NotFound",
                        "Next run could not be calculated.");
        }
        catch (Exception ex)
        {
            TxtNextRun.Text =
                $"{T("JOB10.Cron.Invalid", "Invalid Cron")}: {ex.Message}";
        }
    }

    private async void BtnReload_Click(
        object sender,
        RoutedEventArgs e)
    {
        await LoadAsync(
            _selectedJob?.Id);
    }

    private ProcessStartInfo CreateSchedulerProcessStartInfo(
        string jobId)
    {
        var arguments =
            BuildSchedulerArguments(jobId);

        // 1) Published deployment: prefer an actual apphost EXE next to Desktop.
        var localExe =
            Path.Combine(
                AppContext.BaseDirectory,
                "DMS.Scheduler.exe");

        if (File.Exists(localExe))
        {
            return CreateRedirectedStartInfo(
                localExe,
                arguments);
        }

        var srcRoot =
            FindDevelopmentSrcRoot();

        if (!string.IsNullOrWhiteSpace(srcRoot))
        {
            var schedulerProject =
                Path.Combine(
                    srcRoot,
                    "DMS.Scheduler",
                    "DMS.Scheduler.csproj");

            var schedulerBin =
                Path.Combine(
                    srcRoot,
                    "DMS.Scheduler",
                    "bin");

            if (Directory.Exists(schedulerBin))
            {
                // 2) Development: always prefer an apphost EXE.
                // A loose project-reference DLL copied into another output folder
                // has no matching runtimeconfig and MUST NOT be launched with dotnet.
                var builtExe =
                    Directory
                        .EnumerateFiles(
                            schedulerBin,
                            "DMS.Scheduler.exe",
                            SearchOption.AllDirectories)
                        .Where(
                            path =>
                                File.Exists(
                                    Path.Combine(
                                        Path.GetDirectoryName(path)
                                        ?? string.Empty,
                                        "DMS.Scheduler.runtimeconfig.json")))
                        .OrderByDescending(
                            File.GetLastWriteTimeUtc)
                        .FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(builtExe))
                {
                    return CreateRedirectedStartInfo(
                        builtExe,
                        arguments);
                }

                // 3) DLL fallback is allowed only when the matching runtimeconfig
                // exists in the SAME directory. This avoids the hostpolicy.dll error.
                var builtDll =
                    Directory
                        .EnumerateFiles(
                            schedulerBin,
                            "DMS.Scheduler.dll",
                            SearchOption.AllDirectories)
                        .Where(
                            path =>
                                File.Exists(
                                    Path.Combine(
                                        Path.GetDirectoryName(path)
                                        ?? string.Empty,
                                        "DMS.Scheduler.runtimeconfig.json")))
                        .OrderByDescending(
                            File.GetLastWriteTimeUtc)
                        .FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(builtDll))
                {
                    return CreateRedirectedStartInfo(
                        "dotnet",
                        $"\"{builtDll}\" {arguments}");
                }
            }

            // 4) Last DEV fallback: run the project itself. This also guarantees
            // the runtimeconfig is generated from the current project settings.
            if (File.Exists(schedulerProject))
            {
                var startInfo =
                    CreateRedirectedStartInfo(
                        "dotnet",
                        $"run --project \"{schedulerProject}\" --no-launch-profile -- {arguments}");

                startInfo.WorkingDirectory =
                    Path.GetDirectoryName(schedulerProject)
                    ?? srcRoot;

                return startInfo;
            }
        }

        // Published DLL fallback: only valid when runtimeconfig exists beside it.
        var localDll =
            Path.Combine(
                AppContext.BaseDirectory,
                "DMS.Scheduler.dll");

        var localRuntimeConfig =
            Path.Combine(
                AppContext.BaseDirectory,
                "DMS.Scheduler.runtimeconfig.json");

        if (File.Exists(localDll)
            && File.Exists(localRuntimeConfig))
        {
            return CreateRedirectedStartInfo(
                "dotnet",
                $"\"{localDll}\" {arguments}");
        }

        throw new FileNotFoundException(
            "DMS.Scheduler executable/runtimeconfig could not be found. " +
            "Build DMS.Scheduler as an executable project before using Run now.");
    }


    private static ProcessStartInfo CreateRedirectedStartInfo(
    string fileName,
    string arguments)
    {
        return new ProcessStartInfo
        {
            FileName =
                fileName,
            Arguments =
                arguments,
            UseShellExecute =
                false,
            RedirectStandardOutput =
                true,
            RedirectStandardError =
                true,
            CreateNoWindow =
                true
        };
    }

    private static string? FindDevelopmentSrcRoot()
    {
        var directory =
            new DirectoryInfo(
                AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (string.Equals(
                    directory.Name,
                    "src",
                    StringComparison.OrdinalIgnoreCase)
                &&
                Directory.Exists(
                    Path.Combine(
                        directory.FullName,
                        "DMS.Desktop"))
                &&
                Directory.Exists(
                    Path.Combine(
                        directory.FullName,
                        "DMS.Scheduler")))
            {
                return directory.FullName;
            }

            directory =
                directory.Parent;
        }

        return null;
    }


    private static string BuildSchedulerArguments(
        string jobId)
    {
        return
            $"job run \"{jobId}\"";
    }

    private static string DescribeJob(
        ScheduledJobDefinition job)
    {
        var deliveries =
            string.Join(
                ",",
                job.Deliveries.Select(
                    x => $"{x.Type}:{x.IsEnabled}"));

        return
            $"Enabled={job.IsEnabled}, Name={job.Name}, TemplateId={job.TemplateId}, Cron={job.CronExpression}, TimeZone={job.TimeZoneId}, Deliveries={deliveries}";
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text =
            T(
                "JOB10.Title",
                "JOB10 – Automatic jobs");

        TxtSubtitle.Text =
            T(
                "JOB10.SubtitleV6",
                "Schedule published MES/SAP report templates.");

        TxtEditorTitle.Text =
            T(
                "JOB10.Editor.Title",
                "Job settings");

        BtnAdd.Content =
            T(
                "JOB10.Button.Add",
                "New job");

        BtnDelete.Content =
            T(
                "JOB10.Button.Delete",
                "Delete");

        BtnSave.Content =
            T(
                "JOB10.Button.Save",
                "Save");

        BtnRunNow.Content =
            T(
                "JOB10.Button.RunNow",
                "Run now");

        BtnReload.Content =
            T(
                "JOB10.Button.Reload",
                "Reload");

        ChkEnabled.Content =
            T(
                "JOB10.Field.Enabled",
                "Enabled");

        TxtDeliveriesTitle.Text = T("JOB10.Delivery.Title", "Doručení");
        BtnAddServerDelivery.Content = T("JOB10.Delivery.AddServer", "+ Složka");
        BtnAddEmailDelivery.Content = T("JOB10.Delivery.AddEmail", "+ E-mail");
        BtnAddSharePointDelivery.Content = T("JOB10.Delivery.AddSharePoint", "+ SharePoint");
        BtnDuplicateDelivery.Content = T("JOB10.Delivery.Duplicate", "Duplikovat");
        BtnDeleteDelivery.Content = T("JOB10.Delivery.Delete", "Smazat");
        ChkDeliveryEnabled.Content = T("JOB10.Delivery.Enabled", "Doručení aktivní");
        LblDeliveryType.Text = T("JOB10.Delivery.Type", "Typ");
        LblTargetPath.Text = T("JOB10.Field.TargetPath", "Cílová složka");
        LblEmailRecipients.Text = T("JOB10.Delivery.EmailRecipients", "Příjemci (oddělit ; )");
        LblEmailSubject.Text = T("JOB10.Delivery.EmailSubject", "Předmět");
        LblSharePointTarget.Text = T("JOB10.Delivery.SharePointTarget", "SharePoint cesta / složka");
        TxtSharePointHint.Text = T("JOB10.Delivery.SharePointHint", "Globální SharePoint připojení se nastavuje v DMS Scheduleru; zde je cíl konkrétního reportu.");

        LblName.Text =
            T(
                "JOB10.Field.Name",
                "Name");

        LblTemplate.Text =
            T(
                "JOB10.Field.Template",
                "Report template");

        LblCron.Text =
            T(
                "JOB10.Field.Cron",
                "Cron");

        LblTimeZone.Text =
            T(
                "JOB10.Field.TimeZone",
                "Time zone");

        LblFileNameTemplate.Text =
            T(
                "JOB10.Field.FileNameTemplate",
                "File name template");

        ColEnabled.Header =
            T(
                "JOB10.Column.Enabled",
                "Enabled");

        ColName.Header =
            T(
                "JOB10.Column.Name",
                "Name");

        ColTemplate.Header =
            T(
                "JOB10.Column.Template",
                "Report template");

        ColCron.Header =
            T(
                "JOB10.Column.Cron",
                "Schedule");

        LblSummarySource.Text =
            T(
                "JOB10.Template.Source",
                "Source");

        LblSummaryReport.Text =
            T(
                "JOB10.Template.Report",
                "Report");

        LblSummaryPeriod.Text =
            T(
                "JOB10.Template.Period",
                "Period");

        LblSummaryWorkcenters.Text =
            T(
                "JOB10.Template.Workcenters",
                "Work centers");

        LblSummaryFilters.Text =
            T(
                "JOB10.Template.Filters",
                "Filters");

        TxtCronHelp.Text =
            T(
                "JOB10.Cron.Help",
                "Format: minute hour day-of-month month day-of-week. Example: 0 6 * * 1-5 = Mon–Fri at 06:00.");
    }
    private sealed class Job10DeliveryRow
    {
        public Job10DeliveryRow(ScheduledJobDeliveryDefinition definition, int index)
        {
            Definition = definition;
            Index = index;
        }

        public ScheduledJobDeliveryDefinition Definition { get; }
        public int Index { get; }
        public string DisplayName => $"{Index + 1}. {GetDeliveryDisplayName(Definition.Type)}";

        public string Summary
        {
            get
            {
                if (IsDeliveryType(Definition, "ServerFolder") || IsDeliveryType(Definition, "SharePoint"))
                    return Definition.TargetPath;
                if (IsDeliveryType(Definition, "Email"))
                    return Definition.Recipients.Count == 0 ? "Bez příjemců" : string.Join("; ", Definition.Recipients.Take(2)) + (Definition.Recipients.Count > 2 ? " …" : string.Empty);
                return string.Empty;
            }
        }
    }

}
