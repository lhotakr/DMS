using DMS.Scheduler.Configuration;
using DMS.Scheduler.Runtime;

namespace DMS.Scheduler.CommandLine;

public static class SchedulerCli
{
    public static async Task<int> ExecuteAsync(
        string[] args,
        SchedulerSettingsRepository settingsRepository)
    {
        if (args.Length == 0)
            return -1;

        var settings = await settingsRepository.LoadAsync();
        var command = args[0].Trim().ToLowerInvariant();

        if (command == "background")
        {
            SchedulerProcessManager.LaunchDetachedBackground();
            return 0;
        }

        if (command == "run")
        {
            var processManager = new SchedulerProcessManager();
            processManager.RegisterCurrentProcess();

            try
            {
                return await RunLoopAsync(
                    settings,
                    args.Skip(1).Any(
                        x => string.Equals(
                            x,
                            "--background",
                            StringComparison.OrdinalIgnoreCase)));
            }
            finally
            {
                processManager.UnregisterCurrentProcess();
            }
        }

        if (command == "scheduler" && args.Length >= 2)
        {
            var action = args[1].Trim().ToLowerInvariant();
            var manager = new SchedulerProcessManager();

            switch (action)
            {
                case "start":
                    manager.StartBackground();
                    Console.WriteLine("Scheduler started.");
                    return 0;
                case "stop":
                    manager.Stop();
                    Console.WriteLine("Scheduler stopped.");
                    return 0;
                case "restart":
                    manager.Restart();
                    Console.WriteLine("Scheduler restarted.");
                    return 0;
                case "status":
                    Console.WriteLine(
                        manager.IsRunning()
                            ? $"Running (PID {manager.GetRunningProcessId()})"
                            : "Stopped");
                    return 0;
                case "autostart-on":
                    manager.EnableAutostart();
                    Console.WriteLine("Autostart enabled.");
                    return 0;
                case "autostart-off":
                    manager.DisableAutostart();
                    Console.WriteLine("Autostart disabled.");
                    return 0;
            }
        }

        if (command == "jobs")
        {
            var runtime = SchedulerRuntimeFactory.Create(settings);
            var file = await runtime.JobRepository.LoadAsync();

            foreach (var job in file.Jobs)
            {
                Console.WriteLine(
                    $"{(job.IsEnabled ? "[ON] " : "[OFF]")} " +
                    $"{job.Id} | {job.Name} | {job.CronExpression}");
            }

            return 0;
        }

        if (command == "job" &&
            args.Length >= 3 &&
            string.Equals(
                args[1],
                "run",
                StringComparison.OrdinalIgnoreCase))
        {
            var runtime = SchedulerRuntimeFactory.Create(settings);
            var file = await runtime.JobRepository.LoadAsync();
            var token = args[2];

            var job = file.Jobs.FirstOrDefault(
                x =>
                    string.Equals(
                        x.Id,
                        token,
                        StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        x.Name,
                        token,
                        StringComparison.OrdinalIgnoreCase));

            if (job is null)
            {
                Console.Error.WriteLine($"Job '{token}' was not found.");
                return 4;
            }

            var result = await runtime.Runner.RunAsync(
                job,
                DateTime.Now);

            if (!result.Success)
            {
                Console.Error.WriteLine(result.Error);
                return 5;
            }

            foreach (var destination in result.Destinations)
                Console.WriteLine(destination);

            return 0;
        }

        Console.Error.WriteLine("Unknown command.");
        return 2;
    }

    private static async Task<int> RunLoopAsync(
        SchedulerSettings settings,
        bool background)
    {
        var runtime = SchedulerRuntimeFactory.Create(settings);

        using var cts = new CancellationTokenSource();

        ConsoleCancelEventHandler? handler = null;

        if (!background)
        {
            handler = (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            Console.CancelKeyPress += handler;
        }

        runtime.Logger.Write(
            $"USER_SCHEDULER_START Environment={settings.EnvironmentName}; DataRoot={settings.DataRoot}; Background={background}");

        try
        {
            await runtime.Engine.RunLoopAsync(
                TimeSpan.FromSeconds(settings.PollingSeconds),
                cts.Token);
        }
        catch (OperationCanceledException)
            when (cts.IsCancellationRequested)
        {
        }
        finally
        {
            if (handler is not null)
                Console.CancelKeyPress -= handler;

            runtime.Logger.Write("USER_SCHEDULER_STOP");
        }

        return 0;
    }
}
