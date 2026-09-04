using DMS.Integration.Mes.Models;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;

namespace DMS.Integration.Mes.Services;

public sealed class MesProbeService
{
    private static readonly byte[] PingPayload = Encoding.ASCII.GetBytes(
        "DMS-MES availability probe 0000");

    public async Task<IReadOnlyList<MesProbeResult>> ProbeAsync(
        IReadOnlyList<MesDevice> devices,
        TimeSpan timeout,
        int maxParallelism,
        CancellationToken cancellationToken)
    {
        if (devices.Count == 0)
        {
            return Array.Empty<MesProbeResult>();
        }

        maxParallelism = Math.Clamp(maxParallelism, 1, 64);
        using var semaphore = new SemaphoreSlim(maxParallelism, maxParallelism);

        var tasks = devices.Select(async device =>
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await ProbeOneAsync(
                        device,
                        timeout,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                semaphore.Release();
            }
        }).ToArray();

        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static async Task<MesProbeResult> ProbeOneAsync(
        MesDevice device,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var target = device.Address?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(target))
        {
            return MesProbeResult.Unknown(device, "Missing address.");
        }

        var timeoutMs = Math.Clamp(
            (int)timeout.TotalMilliseconds,
            250,
            30000);

        try
        {
            using var ping = new Ping();
            var options = new PingOptions(ttl: 64, dontFragment: false);

            Task<PingReply> pingTask = IPAddress.TryParse(target, out var ipAddress)
                ? ping.SendPingAsync(
                    ipAddress,
                    timeoutMs,
                    PingPayload,
                    options)
                : ping.SendPingAsync(
                    target,
                    timeoutMs,
                    PingPayload,
                    options);

            var reply = await pingTask
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            if (reply.Status == IPStatus.Success)
            {
                return new MesProbeResult
                {
                    Device = device,
                    State = "Online",
                    IsOnline = true,
                    ResponseTimeMs = reply.RoundtripTime,
                    CheckedAt = DateTime.Now
                };
            }

            return new MesProbeResult
            {
                Device = device,
                State = "Offline",
                IsOnline = false,
                FailureReason = BuildFailureReason(reply),
                CheckedAt = DateTime.Now
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PingException ex)
        {
            return new MesProbeResult
            {
                Device = device,
                State = "Error",
                IsOnline = false,
                FailureReason = ex.InnerException?.Message ?? ex.Message,
                CheckedAt = DateTime.Now
            };
        }
        catch (Exception ex)
        {
            return new MesProbeResult
            {
                Device = device,
                State = "Error",
                IsOnline = false,
                FailureReason = ex.Message,
                CheckedAt = DateTime.Now
            };
        }
    }

    private static string BuildFailureReason(PingReply reply)
    {
        var address = reply.Address?.ToString();
        return string.IsNullOrWhiteSpace(address)
            ? reply.Status.ToString()
            : $"{reply.Status}; ReplyAddress={address}";
    }
}
