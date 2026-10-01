using DMS.Scheduler.Configuration;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;

namespace DMS.Scheduler.Service;

public sealed class SchedulerServiceInstaller
{
    private readonly SchedulerSettings _settings;

    public SchedulerServiceInstaller(SchedulerSettings settings)
    {
        _settings = settings;
    }

    public bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);

        return principal.IsInRole(
            WindowsBuiltInRole.Administrator);
    }

    public bool IsInstalled() =>
        RunSc(
            $"query \"{_settings.ServiceName}\"",
            false).ExitCode == 0;

    public void Install(string executablePath)
    {
        if (!IsAdministrator())
            throw new InvalidOperationException(
                "Administrator rights are required.");

        executablePath = Path.GetFullPath(executablePath);

        if (!File.Exists(executablePath))
            throw new FileNotFoundException(
                "DMS.Scheduler.exe was not found.",
                executablePath);

        if (IsInstalled())
            throw new InvalidOperationException(
                $"Service '{_settings.ServiceName}' is already installed.");

        var serviceCommand =
            $"\\\"{executablePath}\\\" service-host";

        RunSc(
            $"create \"{_settings.ServiceName}\" " +
            $"binPath= \"{serviceCommand}\" " +
            $"start= auto " +
            $"DisplayName= \"DMS Scheduler\"");

        RunSc(
            $"description \"{_settings.ServiceName}\" " +
            "\"DMS scheduled MES/SAP report service.\"");
    }

    public void Uninstall()
    {
        if (!IsAdministrator())
            throw new InvalidOperationException(
                "Administrator rights are required.");

        if (!IsInstalled())
            return;

        RunSc(
            $"stop \"{_settings.ServiceName}\"",
            false);

        RunSc(
            $"delete \"{_settings.ServiceName}\"");
    }

    public static string GetCurrentSchedulerExecutable()
    {
        var path = Environment.ProcessPath;

        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException(
                "Could not resolve current executable.");

        if (string.Equals(
                Path.GetFileName(path),
                "dotnet.exe",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Service cannot be installed from 'dotnet run'. " +
                "Build DMS.Scheduler and run DMS.Scheduler.exe directly.");
        }

        return path;
    }

    public static bool RelaunchElevated(
        params string[] arguments)
    {
        var psi =
            new ProcessStartInfo
            {
                FileName = GetCurrentSchedulerExecutable(),
                Arguments =
                    string.Join(
                        " ",
                        arguments.Select(Quote)),
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            };

        try
        {
            Process.Start(psi);
            return true;
        }
        catch (Win32Exception ex)
            when (ex.NativeErrorCode == 1223)
        {
            return false;
        }
    }

    private static string Quote(string value) =>
        value.Contains(' ')
            ? $"\"{value.Replace("\"", "\\\"")}\""
            : value;

    private static ScResult RunSc(
        string arguments,
        bool throwOnError = true)
    {
        var psi =
            new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

        using var process =
            Process.Start(psi)
            ?? throw new InvalidOperationException(
                "Could not start sc.exe.");

        var stdout =
            process.StandardOutput.ReadToEnd();

        var stderr =
            process.StandardError.ReadToEnd();

        process.WaitForExit();

        var result =
            new ScResult(
                process.ExitCode,
                stdout,
                stderr);

        if (throwOnError &&
            result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"sc.exe failed ({result.ExitCode}). " +
                $"{result.StdOut} {result.StdErr}");
        }

        return result;
    }

    private sealed record ScResult(
        int ExitCode,
        string StdOut,
        string StdErr);
}
