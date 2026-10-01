using DMS.Core.Scheduling;
using DMS.Scheduler.Configuration;
using DMS.Scheduler.Runtime;
using Spectre.Console;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace DMS.Scheduler.ConsoleUi;

public sealed class SchedulerConsoleApp
{
    private readonly SchedulerSettingsRepository _settingsRepository;

    public SchedulerConsoleApp(
        SchedulerSettingsRepository settingsRepository)
    {
        _settingsRepository =
            settingsRepository;
    }

    public async Task<int> RunAsync()
    {
        while (true)
        {
            AnsiConsole.Clear();

            var settings =
                await _settingsRepository.LoadAsync();

            var processManager =
                new SchedulerProcessManager();

            await RenderHeaderAsync(
                settings,
                processManager);

            var choice =
                AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("[bold]Choose action[/]")
                        .AddChoices(
                            "Start scheduler",
                            "Stop scheduler",
                            "Restart scheduler",
                            processManager.IsAutostartEnabled()
                                ? "Disable autostart"
                                : "Enable autostart",
                            "Jobs view",
                            "Run job",
                            "Settings",
                            "View log",
                            "Exit"));

            try
            {
                switch (choice)
                {
                    case "Start scheduler":
                        processManager.StartBackground();
                        AnsiConsole.MarkupLine(
                            "[green]Scheduler started.[/]");
                        Pause();
                        break;

                    case "Stop scheduler":
                        processManager.Stop();
                        AnsiConsole.MarkupLine(
                            "[green]Scheduler stopped.[/]");
                        Pause();
                        break;

                    case "Restart scheduler":
                        processManager.Restart();
                        AnsiConsole.MarkupLine(
                            "[green]Scheduler restarted.[/]");
                        Pause();
                        break;

                    case "Enable autostart":
                        processManager.EnableAutostart();
                        AnsiConsole.MarkupLine(
                            "[green]Autostart enabled for the current Windows user.[/]");
                        Pause();
                        break;

                    case "Disable autostart":
                        processManager.DisableAutostart();
                        AnsiConsole.MarkupLine(
                            "[green]Autostart disabled.[/]");
                        Pause();
                        break;

                    case "Jobs view":
                        await ShowJobsAsync(settings);
                        break;

                    case "Run job":
                        await RunJobAsync(settings);
                        break;

                    case "Settings":
                        await EditSettingsAsync(settings);
                        break;

                    case "View log":
                        ViewLog(settings);
                        break;

                    case "Exit":
                        return 0;
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine(
                    $"[red]Error:[/] {Markup.Escape(ex.Message)}");

                Pause();
            }
        }
    }

    private async Task RenderHeaderAsync(
        SchedulerSettings settings,
        SchedulerProcessManager processManager)
    {
        var schedulerText =
            processManager.IsRunning()
                ? $"[green]● Running[/] [grey](PID {processManager.GetRunningProcessId()})[/]"
                : "[grey]○ Stopped[/]";

        var autostartText =
            processManager.IsAutostartEnabled()
                ? "[green]✓ Enabled[/]"
                : "[grey]Disabled[/]";

        var jobsText =
            "unavailable";

        try
        {
            var runtime =
                SchedulerRuntimeFactory.Create(
                    settings);

            var file =
                await runtime.JobRepository.LoadAsync();

            jobsText =
                $"{file.Jobs.Count} configured / " +
                $"{file.Jobs.Count(x => x.IsEnabled)} enabled";
        }
        catch
        {
        }

        var grid =
            new Grid()
                .AddColumn()
                .AddColumn();

        grid.AddRow(
            "[bold]Scheduler[/]",
            schedulerText);

        grid.AddRow(
            "[bold]Autostart[/]",
            autostartText);

        grid.AddRow(
            "[bold]Jobs[/]",
            Markup.Escape(jobsText));

        grid.AddRow(
            "[bold]Environment[/]",
            Markup.Escape(settings.EnvironmentName));

        grid.AddRow(
            "[bold]Data root[/]",
            Markup.Escape(settings.DataRoot));

        var panel =
            new Panel(grid)
            {
                Header =
                    new PanelHeader(
                        " DMS Scheduler "),
                Border =
                    BoxBorder.Rounded
            };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    private static async Task ShowJobsAsync(
        SchedulerSettings settings)
    {
        var runtime =
            SchedulerRuntimeFactory.Create(
                settings);

        var file =
            await runtime.JobRepository.LoadAsync();

        var templates =
            await runtime.TemplateRepository.LoadAsync();

        var table =
            new Table()
                .Border(
                    TableBorder.Rounded)
                .AddColumn("On")
                .AddColumn("Name")
                .AddColumn("Template")
                .AddColumn("Cron")
                .AddColumn("Timezone");

        foreach (var job in file.Jobs)
        {
            var templateName =
                templates.Templates
                    .FirstOrDefault(
                        x =>
                            string.Equals(
                                x.Id,
                                job.TemplateId,
                                StringComparison.OrdinalIgnoreCase))
                    ?.Name
                ?? job.TemplateId;

            table.AddRow(
                job.IsEnabled
                    ? "[green]✓[/]"
                    : "[grey]✗[/]",
                Markup.Escape(job.Name),
                Markup.Escape(templateName),
                Markup.Escape(job.CronExpression),
                Markup.Escape(job.TimeZoneId));
        }

        AnsiConsole.Write(table);
        Pause();
    }

    private static async Task RunJobAsync(
        SchedulerSettings settings)
    {
        var runtime =
            SchedulerRuntimeFactory.Create(
                settings);

        var file =
            await runtime.JobRepository.LoadAsync();

        if (file.Jobs.Count == 0)
        {
            AnsiConsole.MarkupLine(
                "[yellow]No jobs configured.[/]");

            Pause();
            return;
        }

        var selected =
            AnsiConsole.Prompt(
                new SelectionPrompt<ScheduledJobDefinition>()
                    .Title("Select job")
                    .UseConverter(
                        x =>
                            string.IsNullOrWhiteSpace(x.Name)
                                ? x.Id
                                : x.Name)
                    .AddChoices(
                        file.Jobs));

        var result =
            await AnsiConsole.Status()
                .StartAsync(
                    $"Running {Markup.Escape(selected.Name)}...",
                    async _ =>
                        await runtime.Runner.RunAsync(
                            selected,
                            DateTime.Now));

        if (!result.Success)
        {
            AnsiConsole.MarkupLine(
                $"[red]Job failed:[/] {Markup.Escape(result.Error ?? "Unknown error")}");

            Pause();
            return;
        }

        AnsiConsole.MarkupLine(
            "[green]✓ Job finished successfully[/]");

        foreach (var destination in result.Destinations)
        {
            AnsiConsole.MarkupLine(
                $"  [grey]→[/] {Markup.Escape(destination)}");
        }

        Pause();
    }

    private async Task EditSettingsAsync(
        SchedulerSettings settings)
    {
        settings.EnvironmentName =
            AnsiConsole.Ask(
                "Environment:",
                settings.EnvironmentName);

        settings.DataRoot =
            AnsiConsole.Ask(
                "DMS data root:",
                settings.DataRoot);

        settings.PollingSeconds =
            AnsiConsole.Ask(
                "Polling interval in seconds:",
                settings.PollingSeconds);

        settings.Normalize();

        var table =
            new Table()
                .Border(TableBorder.Rounded)
                .AddColumn("Path")
                .AddColumn("Status");

        AddPathStatus(
            table,
            "Data root",
            settings.DataRoot,
            Directory.Exists);

        AddPathStatus(
            table,
            "jobs.json",
            settings.JobsPath,
            File.Exists);

        AddPathStatus(
            table,
            "report-templates.json",
            settings.TemplatesPath,
            File.Exists);

        AddPathStatus(
            table,
            "MES database settings",
            settings.MesDatabaseSettingsPath,
            File.Exists);

        AnsiConsole.Write(table);

        if (AnsiConsole.Confirm(
                "Save settings?",
                defaultValue: true))
        {
            await _settingsRepository.SaveAsync(
                settings);

            AnsiConsole.MarkupLine(
                $"[green]Saved:[/] {Markup.Escape(_settingsRepository.SettingsPath)}");
        }

        Pause();
    }

    private static void AddPathStatus(
        Table table,
        string name,
        string path,
        Func<string, bool> exists)
    {
        table.AddRow(
            Markup.Escape(name),
            exists(path)
                ? $"[green]✓[/] {Markup.Escape(path)}"
                : $"[red]✗[/] {Markup.Escape(path)}");
    }

    private static void ViewLog(
        SchedulerSettings settings)
    {
        if (!File.Exists(settings.LogPath))
        {
            AnsiConsole.MarkupLine(
                "[yellow]Log file does not exist yet.[/]");

            Pause();
            return;
        }

        var lines =
            File.ReadLines(
                    settings.LogPath)
                .TakeLast(40);

        var panel =
            new Panel(
                string.Join(
                    Environment.NewLine,
                    lines.Select(
                        Markup.Escape)))
            {
                Header =
                    new PanelHeader(
                        " Last 40 log lines "),
                Border =
                    BoxBorder.Rounded
            };

        AnsiConsole.Write(panel);
        Pause();
    }

    private static void Pause()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(
            "[grey]Press any key to continue...[/]");

        Console.ReadKey(
            intercept: true);
    }
}
