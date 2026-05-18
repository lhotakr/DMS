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
        Write("TRANSACTION", $"Uživatel: {user}; Transakce: {transactionText}", null);
    }

    public void OpenDocument(string filePath, string user)
    {
        Write("DOCUMENT", $"Uživatel: {user}; Otevření dokumentu: {filePath}", null);
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
            builder.Append(exception.Message);
        }

        builder.AppendLine();

        return builder.ToString();
    }
}