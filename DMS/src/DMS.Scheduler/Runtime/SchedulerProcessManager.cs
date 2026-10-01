using DMS.Scheduler.Configuration;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace DMS.Scheduler.Runtime;

/// <summary>
/// Controls the scheduler as a normal per-user process.
/// No Windows Service and no administrator rights are required.
/// </summary>
public sealed class SchedulerProcessManager
{
    private const string RunKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";

    private const string RunValueName =
        "DMS.Scheduler";

    private readonly string _pidFile;

    public SchedulerProcessManager()
    {
        var runtimeDirectory =
            SchedulerSettingsRepository.GetRuntimeDirectory();

        Directory.CreateDirectory(
            runtimeDirectory);

        _pidFile =
            Path.Combine(
                runtimeDirectory,
                "scheduler.pid");
    }

    public bool IsRunning()
    {
        var process =
            TryGetRunningProcess();

        return process is not null;
    }

    public int? GetRunningProcessId()
    {
        using var process =
            TryGetRunningProcess();

        return process?.Id;
    }

    public void StartBackground()
    {
        if (IsRunning())
            return;

        var executable =
            GetSchedulerExecutable();

        var psi =
            new ProcessStartInfo
            {
                FileName =
                    executable,
                Arguments =
                    "run --background",
                UseShellExecute =
                    false,
                CreateNoWindow =
                    true,
                WorkingDirectory =
                    AppContext.BaseDirectory
            };

        var process =
            Process.Start(
                psi)
            ?? throw new InvalidOperationException(
                "Could not start DMS Scheduler.");

        File.WriteAllText(
            _pidFile,
            process.Id.ToString());
    }

    public void Stop()
    {
        using var process =
            TryGetRunningProcess();

        if (process is null)
        {
            DeletePidFile();
            return;
        }

        try
        {
            process.Kill(
                entireProcessTree: true);

            process.WaitForExit(
                5000);
        }
        finally
        {
            DeletePidFile();
        }
    }

    public void Restart()
    {
        Stop();
        StartBackground();
    }

    public bool IsAutostartEnabled()
    {
        using var key =
            Registry.CurrentUser.OpenSubKey(
                RunKeyPath,
                writable: false);

        var value =
            key?.GetValue(
                RunValueName)
            as string;

        return !string.IsNullOrWhiteSpace(
            value);
    }

    public void EnableAutostart()
    {
        var executable =
            GetSchedulerExecutable();

        // "background" immediately starts the hidden "run --background"
        // process and exits, so no persistent console window is left open.
        var command =
            $"\"{executable}\" background";

        using var key =
            Registry.CurrentUser.CreateSubKey(
                RunKeyPath,
                writable: true);

        key.SetValue(
            RunValueName,
            command,
            RegistryValueKind.String);
    }

    public void DisableAutostart()
    {
        using var key =
            Registry.CurrentUser.OpenSubKey(
                RunKeyPath,
                writable: true);

        key?.DeleteValue(
            RunValueName,
            throwOnMissingValue: false);
    }

    public static void LaunchDetachedBackground()
    {
        var executable =
            GetSchedulerExecutable();

        var psi =
            new ProcessStartInfo
            {
                FileName =
                    executable,
                Arguments =
                    "run --background",
                UseShellExecute =
                    false,
                CreateNoWindow =
                    true,
                WorkingDirectory =
                    AppContext.BaseDirectory
            };

        Process.Start(
            psi);
    }

    public void RegisterCurrentProcess()
    {
        File.WriteAllText(
            _pidFile,
            Environment.ProcessId.ToString());
    }

    public void UnregisterCurrentProcess()
    {
        if (!File.Exists(_pidFile))
            return;

        var text =
            File.ReadAllText(
                _pidFile);

        if (int.TryParse(
                text,
                out var pid)
            && pid == Environment.ProcessId)
        {
            DeletePidFile();
        }
    }

    private Process? TryGetRunningProcess()
    {
        if (!File.Exists(_pidFile))
            return null;

        var text =
            File.ReadAllText(
                _pidFile);

        if (!int.TryParse(
                text,
                out var pid))
        {
            DeletePidFile();
            return null;
        }

        try
        {
            var process =
                Process.GetProcessById(
                    pid);

            if (process.HasExited)
            {
                process.Dispose();
                DeletePidFile();
                return null;
            }

            var expectedName =
                Path.GetFileNameWithoutExtension(
                    GetSchedulerExecutable());

            if (!string.Equals(
                    process.ProcessName,
                    expectedName,
                    StringComparison.OrdinalIgnoreCase))
            {
                process.Dispose();
                DeletePidFile();
                return null;
            }

            return process;
        }
        catch
        {
            DeletePidFile();
            return null;
        }
    }

    private static string GetSchedulerExecutable()
    {
        var path =
            Environment.ProcessPath;

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException(
                "Could not determine DMS Scheduler executable path.");
        }

        if (string.Equals(
                Path.GetFileName(path),
                "dotnet.exe",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Background/autostart mode requires DMS.Scheduler.exe. " +
                "Build the project and launch the generated EXE instead of dotnet run.");
        }

        return Path.GetFullPath(
            path);
    }

    private void DeletePidFile()
    {
        try
        {
            if (File.Exists(_pidFile))
                File.Delete(_pidFile);
        }
        catch
        {
            // A stale pid file is harmless and will be repaired on next status check.
        }
    }
}
