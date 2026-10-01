using DMS.Core.Scheduling;
using DMS.Scheduler.Configuration;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

namespace DMS.Scheduler.Delivery;

public sealed class EmailReportDelivery : IReportDelivery
{
    private readonly SmtpSettings _settings;
    private readonly Action<string>? _log;

    public EmailReportDelivery(
        SmtpSettings settings,
        Action<string>? log = null)
    {
        _settings =
            settings
            ?? throw new ArgumentNullException(
                nameof(settings));

        _log = log;
    }

    public string Type => "Email";

    public async Task<ReportDeliveryResult> DeliverAsync(
        ScheduledJobDefinition job,
        ScheduledReportTemplate? template,
        ScheduledJobDeliveryDefinition delivery,
        GeneratedReport report,
        DateTime executionLocalTime,
        CancellationToken cancellationToken = default)
    {
        const string destination = "email";

        try
        {
            if (!_settings.IsEnabled)
            {
                throw new InvalidOperationException(
                    "SMTP delivery is disabled.");
            }

            if (string.IsNullOrWhiteSpace(
                    _settings.Server))
            {
                throw new InvalidOperationException(
                    "SMTP server is empty.");
            }

            if (string.IsNullOrWhiteSpace(
                    _settings.SenderAddress))
            {
                throw new InvalidOperationException(
                    "SMTP sender address is empty.");
            }

            var recipients =
                delivery.Recipients
                    .Where(
                        x => !string.IsNullOrWhiteSpace(x))
                    .Select(
                        x => x.Trim())
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .ToArray();

            if (recipients.Length == 0)
            {
                throw new InvalidOperationException(
                    "Email delivery does not contain any recipient.");
            }

            using var message =
                new MailMessage
                {
                    From =
                        new MailAddress(
                            _settings.SenderAddress,
                            _settings.SenderDisplayName),

                    Subject =
                        DeliveryTokenRenderer.Render(
                            delivery.SubjectTemplate,
                            job,
                            template,
                            report,
                            executionLocalTime),

                    Body =
                        $"Automaticky generovaný report DMS.{Environment.NewLine}{Environment.NewLine}" +
                        $"Úloha: {job.Name}{Environment.NewLine}" +
                        $"Šablona: {template?.Name ?? "-"}{Environment.NewLine}" +
                        $"Report: {report.ReportCode}{Environment.NewLine}" +
                        $"Čas: {executionLocalTime:dd.MM.yyyy HH:mm}",

                    IsBodyHtml = false
                };

            foreach (var recipient in recipients)
            {
                message.To.Add(recipient);
            }

            var stream =
                new MemoryStream(
                    report.Content,
                    writable: false);

            var attachment =
                new Attachment(
                    stream,
                    DeliveryTokenRenderer.GetFileName(
                        job,
                        template,
                        report,
                        executionLocalTime),
                    report.ContentType);

            message.Attachments.Add(
                attachment);

            using var client =
                new SmtpClient(
                    _settings.Server,
                    _settings.Port)
                {
                    EnableSsl = _settings.UseSsl,
                    DeliveryMethod = SmtpDeliveryMethod.Network,

                    // IMPORTANT:
                    // FASTEC config says Server authentication = False.
                    // Anonymous relay means NO Windows/default credentials.
                    UseDefaultCredentials = false
                };

            if (_settings.UseAuthentication)
            {
                var password =
                    Environment.GetEnvironmentVariable(
                        _settings.PasswordEnvironmentVariable)
                    ?? string.Empty;

                if (string.IsNullOrWhiteSpace(password))
                {
                    throw new InvalidOperationException(
                        $"SMTP password environment variable '{_settings.PasswordEnvironmentVariable}' is empty.");
                }

                client.Credentials =
                    new NetworkCredential(
                        _settings.Login,
                        password);
            }
            else
            {
                client.Credentials = null;
            }

            _log?.Invoke(
                $"EMAIL_START JobId={job.Id}; Recipients={recipients.Length}; " +
                $"Server={_settings.Server}:{_settings.Port}; SSL={_settings.UseSsl}; " +
                $"Auth={_settings.UseAuthentication}; Sender={_settings.SenderAddress}");

            cancellationToken.ThrowIfCancellationRequested();

            await client.SendMailAsync(
                message,
                cancellationToken);

            var resultDestination =
                "email:"
                + string.Join(
                    ';',
                    recipients);

            _log?.Invoke(
                $"EMAIL_OK JobId={job.Id}; Destination={resultDestination}");

            return ReportDeliveryResult.Ok(
                resultDestination);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SmtpException ex)
        {
            var details =
                BuildExceptionMessage(ex);

            _log?.Invoke(
                $"EMAIL_FAIL JobId={job.Id}; SMTP={ex.StatusCode}; Error={details}");

            return ReportDeliveryResult.Fail(
                destination,
                $"SMTP {ex.StatusCode}: {details}");
        }
        catch (Exception ex)
        {
            var details =
                BuildExceptionMessage(ex);

            _log?.Invoke(
                $"EMAIL_FAIL JobId={job.Id}; Error={details}");

            return ReportDeliveryResult.Fail(
                destination,
                details);
        }
    }

    private static string BuildExceptionMessage(
        Exception exception)
    {
        var parts =
            new System.Collections.Generic.List<string>();

        Exception? current = exception;

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
}
