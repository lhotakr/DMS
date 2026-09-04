using System;
using System.Linq;

namespace DMS.Desktop.Logging;

public sealed class DmsLogEntry
{
    public DateTime Timestamp { get; set; }

    public string TimestampText => Timestamp.ToString("HH:mm:ss.fff");

    public string Level { get; set; } = string.Empty;

    public string OperationId { get; set; } = string.Empty;

    public string CorrelationId { get; set; } = string.Empty;

    public string Module { get; set; } = string.Empty;

    public string PersonId { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public string Area { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string Entity { get; set; } = string.Empty;

    public string EntityId { get; set; } = string.Empty;

    public string Field { get; set; } = string.Empty;

    public string OldValue { get; set; } = string.Empty;

    public string NewValue { get; set; } = string.Empty;

    public string User { get; set; } = string.Empty;

    public string Roles { get; set; } = string.Empty;

    public string DurationMs { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public string File { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string RawLine { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable summary shown in LOG03.
    /// Keep this text in English. UI labels are localized, but log content must be stable.
    /// </summary>
    public string HumanText
    {
        get
        {
            return Level switch
            {
                "TRANSACTION" => string.IsNullOrWhiteSpace(Text)
                    ? "Transaction input"
                    : $"Transaction input: {Text}",

                "TX_START" => string.IsNullOrWhiteSpace(Code)
                    ? "Transaction started"
                    : $"Transaction {Code} started",

                "TX_OK" => string.IsNullOrWhiteSpace(Code)
                    ? "Transaction completed"
                    : $"Transaction {Code} completed",

                "TX_ERROR" or "TX_FAIL" => string.IsNullOrWhiteSpace(Code)
                    ? BuildMessage("Transaction failed", NormalizeLogText(Message))
                    : BuildMessage($"Transaction {Code} failed", NormalizeLogText(Message)),

                "TX_DENIED" => string.IsNullOrWhiteSpace(Code)
                    ? BuildMessage("Transaction denied", NormalizeLogText(Reason))
                    : BuildMessage($"Transaction {Code} denied", NormalizeLogText(Reason)),

                "TX_VALIDATION" => string.IsNullOrWhiteSpace(Code)
                    ? BuildMessage("Transaction validation failed", NormalizeLogText(Reason))
                    : BuildMessage($"Transaction {Code} validation failed", NormalizeLogText(Reason)),

                "ADMIN" => JoinNonEmpty(": ", Area, Action),
                "CONFIG_CHANGED" => BuildFrameworkSummary("Configuration changed"),
                "WORKFLOW_CHANGED" => BuildFrameworkSummary("Workflow changed"),
                "SECURITY_CHANGED" => BuildFrameworkSummary("Security changed"),
                "FRAMEWORK_DIAGNOSTIC" => BuildFrameworkSummary("Framework diagnostic"),
                "FRAMEWORK_HEALTH" => BuildFrameworkSummary("Framework health check"),
                "DATA" => JoinNonEmpty(": ", Area, JoinNonEmpty(" ", Action, Entity, EntityId)),
                "AUDIT" => BuildAuditChangeText(),
                "AUDIT_CREATE" => BuildAuditCreateText(),
                "AUDIT_DELETE" => BuildAuditDeleteText(),
                "APP" => BuildMessage("Application", NormalizeLogText(Action)),
                "DOCUMENT" => BuildMessage(Action, File),
                "INFO" => NormalizeLogText(Message),
                "WARN" => NormalizeLogText(Message),
                "ERROR" => NormalizeLogText(Message),
                _ => NormalizeLogText(Message)
            };
        }
    }

    private string BuildAuditChangeText()
    {
        var target = JoinNonEmpty(" ", Entity, EntityId);

        if (string.IsNullOrWhiteSpace(target) && string.IsNullOrWhiteSpace(Field))
        {
            return NormalizeLogText(Message);
        }

        if (string.IsNullOrWhiteSpace(Field))
        {
            return JoinNonEmpty(": ", Area, $"{target} changed".Trim());
        }

        return JoinNonEmpty(": ", Area, $"{target}, field {Field} changed".Trim(' ', ','));
    }

    private string BuildAuditCreateText()
    {
        var target = JoinNonEmpty(" ", Entity, EntityId);
        return string.IsNullOrWhiteSpace(target)
            ? NormalizeLogText(Message)
            : JoinNonEmpty(": ", Area, $"created {target}");
    }

    private string BuildAuditDeleteText()
    {
        var target = JoinNonEmpty(" ", Entity, EntityId);
        return string.IsNullOrWhiteSpace(target)
            ? NormalizeLogText(Message)
            : JoinNonEmpty(": ", Area, $"deleted {target}");
    }

    private static string BuildMessage(string prefix, string value)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return value ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return prefix;
        }

        return $"{prefix}: {value}";
    }

    private static string JoinNonEmpty(string separator, params string[] values)
    {
        return string.Join(
            separator,
            values.Where(item => !string.IsNullOrWhiteSpace(item)));
    }

    /// <summary>
    /// Normalizes older Czech log messages for display in LOG03.
    /// New code should write English log messages directly through DmsLogger.
    /// RawLine intentionally stays unchanged so support can still copy the exact original line.
    /// </summary>
    private static string NormalizeLogText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value.Trim();

        if (text.Equals("DMS klient spuštěn.", StringComparison.OrdinalIgnoreCase))
        {
            return "DMS client started.";
        }

        if (text.StartsWith("Lokalizace načtena ze složky:", StringComparison.OrdinalIgnoreCase))
        {
            return ReplacePrefix(text, "Lokalizace načtena ze složky:", "Localization loaded from folder:");
        }

        if (text.StartsWith("Header logo načteno:", StringComparison.OrdinalIgnoreCase))
        {
            return ReplacePrefix(text, "Header logo načteno:", "Header logo loaded:");
        }

        if (text.StartsWith("Aktuální uživatel:", StringComparison.OrdinalIgnoreCase))
        {
            return ReplacePrefix(text, "Aktuální uživatel:", "Current user:");
        }

        if (text.StartsWith("Uložen artikl ", StringComparison.OrdinalIgnoreCase))
        {
            return ReplacePrefix(text, "Uložen artikl ", "Article saved: ");
        }

        if (text.StartsWith("Neznámá transakce:", StringComparison.OrdinalIgnoreCase))
        {
            return ReplacePrefix(text, "Neznámá transakce:", "Unknown transaction:");
        }

        if (text.StartsWith("Chyba transakce ", StringComparison.OrdinalIgnoreCase))
        {
            text = ReplacePrefix(text, "Chyba transakce ", "Transaction error ");
            text = text.Replace("Neznámá transakce:", "Unknown transaction:", StringComparison.OrdinalIgnoreCase);
            return text;
        }

        if (text.StartsWith("Zamítnuté spuštění transakce ", StringComparison.OrdinalIgnoreCase))
        {
            text = ReplacePrefix(text, "Zamítnuté spuštění transakce ", "Transaction execution denied ");
            text = text.Replace("Neznámá transakce:", "Unknown transaction:", StringComparison.OrdinalIgnoreCase);
            return text;
        }

        if (text.StartsWith("Spuštěna transakce ", StringComparison.OrdinalIgnoreCase))
        {
            return ReplacePrefix(text, "Spuštěna transakce ", "Transaction started: ");
        }

        if (text.StartsWith("Transakce ", StringComparison.OrdinalIgnoreCase) &&
            text.EndsWith(" dokončena", StringComparison.OrdinalIgnoreCase))
        {
            var code = text["Transakce ".Length..^" dokončena".Length].Trim();
            return string.IsNullOrWhiteSpace(code)
                ? "Transaction completed"
                : $"Transaction completed: {code}";
        }

        if (text.StartsWith("Zamítnuté spuštění ", StringComparison.OrdinalIgnoreCase))
        {
            return ReplacePrefix(text, "Zamítnuté spuštění ", "Transaction denied: ");
        }

        if (text.StartsWith("Validační chyba transakce ", StringComparison.OrdinalIgnoreCase))
        {
            return ReplacePrefix(text, "Validační chyba transakce ", "Transaction validation failed: ");
        }

        if (text.StartsWith("Aplikace:", StringComparison.OrdinalIgnoreCase))
        {
            return ReplacePrefix(text, "Aplikace:", "Application:");
        }

        if (text.Equals("Dokument", StringComparison.OrdinalIgnoreCase))
        {
            return "Document";
        }

        return text;
    }

    private string BuildFrameworkSummary(string title)
    {
        var subject = JoinNonEmpty(" ", Module, Area, Entity, EntityId);
        var summary = string.IsNullOrWhiteSpace(subject)
            ? title
            : $"{title}: {subject}";

        return BuildMessage(summary, NormalizeLogText(Detail));
    }

    private static string ReplacePrefix(string text, string oldPrefix, string newPrefix)
    {
        return newPrefix + text[oldPrefix.Length..];
    }
}
