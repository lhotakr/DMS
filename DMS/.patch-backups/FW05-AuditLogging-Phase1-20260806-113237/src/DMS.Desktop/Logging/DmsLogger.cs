using System.Diagnostics;
using System.IO;
using System.Text;

namespace DMS.Desktop.Logging;

public sealed class DmsLogger
{
    private readonly string _logsRootPath;
    private readonly object _lock = new();

    public DmsLogger(string logsRootPath)
    {
        _logsRootPath = logsRootPath;
    }

    public static string NewOperationId()
    {
        return Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    }

    public void Info(string message)
    {
        Write("INFO", message, null);
    }

    public void Warning(string message)
    {
        Write("WARN", message, null);
    }

    public void Error(string message, Exception? exception = null)
    {
        Write("ERROR", message, exception);
    }

    public void Transaction(string transactionText, string user)
    {
        Write("TRANSACTION", $"User={Safe(user)}; Text={Safe(transactionText)}", null);
    }

    public void TransactionStarted(
        string operationId,
        string transactionCode,
        string transactionText,
        string user,
        IEnumerable<string>? userRoles = null)
    {
        Write(
            "TX_START",
            $"OperationId={operationId}; Code={Safe(transactionCode)}; Text={Safe(transactionText)}; User={Safe(user)}; Roles={SafeJoin(userRoles)}",
            null);
    }

    public void TransactionSucceeded(
        string operationId,
        string transactionCode,
        string user,
        TimeSpan duration)
    {
        Write(
            "TX_OK",
            $"OperationId={operationId}; Code={Safe(transactionCode)}; User={Safe(user)}; DurationMs={duration.TotalMilliseconds:0}",
            null);
    }

    public void TransactionFailed(
        string operationId,
        string transactionCode,
        string user,
        string message,
        Exception? exception = null)
    {
        Write(
            "TX_ERROR",
            $"OperationId={operationId}; Code={Safe(transactionCode)}; User={Safe(user)}; Message={Safe(message)}",
            exception);
    }

    public void TransactionDenied(
        string operationId,
        string transactionCode,
        string user,
        string reason)
    {
        Write(
            "TX_DENIED",
            $"OperationId={operationId}; Code={Safe(transactionCode)}; User={Safe(user)}; Reason={Safe(reason)}",
            null);
    }

    public void TransactionValidationFailed(
        string operationId,
        string transactionCode,
        string user,
        string reason)
    {
        Write(
            "TX_VALIDATION",
            $"OperationId={operationId}; Code={Safe(transactionCode)}; User={Safe(user)}; Reason={Safe(reason)}",
            null);
    }

    public void AdminAction(
        string area,
        string action,
        string user,
        string detail)
    {
        Write(
            "ADMIN",
            $"Area={Safe(area)}; Action={Safe(action)}; User={Safe(user)}; Detail={Safe(detail)}",
            null);
    }

    public void DataChanged(
        string area,
        string entity,
        string action,
        string user,
        string detail)
    {
        Write(
            "DATA",
            $"Area={Safe(area)}; Entity={Safe(entity)}; Action={Safe(action)}; User={Safe(user)}; Detail={Safe(detail)}",
            null);
    }

    public void OpenDocument(string filePath, string user)
    {
        Write(
            "DOCUMENT",
            $"User={Safe(user)}; Action=OpenDocument; File={Safe(filePath)}",
            null);
    }

    public void AppStarted(string user, string logsRootPath)
    {
        Write(
            "APP",
            $"Action=Started; User={Safe(user)}; LogsRootPath={Safe(logsRootPath)}",
            null);
    }

    public void AppClosed(string user)
    {
        Write(
            "APP",
            $"Action=Closed; User={Safe(user)}",
            null);
    }

    private void Write(string level, string message, Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(_logsRootPath);

            var filePath = Path.Combine(
                _logsRootPath,
                $"dms-{DateTime.Now:yyyy-MM-dd}.log");

            var line = BuildLogLine(level, message, exception);

            lock (_lock)
            {
                File.AppendAllText(filePath, line, Encoding.UTF8);
            }
        }
        catch
        {
            // Logger nesmí nikdy shodit aplikaci.
        }
    }

    private static string BuildLogLine(string level, string message, Exception? exception)
    {
        var builder = new StringBuilder();

        builder.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
        builder.Append(" | ");
        builder.Append(level);
        builder.Append(" | ");
        builder.Append(message);

        if (exception is not null)
        {
            builder.Append(" | ");
            builder.Append(exception.GetType().Name);
            builder.Append(": ");
            builder.Append(Safe(exception.Message));

            if (!string.IsNullOrWhiteSpace(exception.StackTrace))
            {
                builder.Append(" | StackTrace=");
                builder.Append(Safe(exception.StackTrace));
            }
        }

        builder.AppendLine();

        return builder.ToString();
    }

    private static string Safe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Replace("|", "/")
            .Trim();
    }

    private static string SafeJoin(IEnumerable<string>? values)
    {
        if (values is null)
        {
            return string.Empty;
        }

        return Safe(string.Join(",", values));
    }

    public void AuditChange(
    string area,
    string entity,
    string entityId,
    string field,
    string? oldValue,
    string? newValue,
    string user)
    {
        Write(
            "AUDIT",
            $"Area={Safe(area)}; Entity={Safe(entity)}; EntityId={Safe(entityId)}; Field={Safe(field)}; Old={Safe(oldValue)}; New={Safe(newValue)}; User={Safe(user)}",
            null);
    }

    public void AuditCreated(
        string area,
        string entity,
        string entityId,
        string user,
        string detail)
    {
        Write(
            "AUDIT_CREATE",
            $"Area={Safe(area)}; Entity={Safe(entity)}; EntityId={Safe(entityId)}; User={Safe(user)}; Detail={Safe(detail)}",
            null);
    }

    public void AuditDeleted(
        string area,
        string entity,
        string entityId,
        string user,
        string detail)
    {
        Write(
            "AUDIT_DELETE",
            $"Area={Safe(area)}; Entity={Safe(entity)}; EntityId={Safe(entityId)}; User={Safe(user)}; Detail={Safe(detail)}",
            null);
    }
}