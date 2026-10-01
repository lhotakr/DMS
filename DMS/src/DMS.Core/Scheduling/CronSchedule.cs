using System;
using System.Collections.Generic;
using System.Globalization;

namespace DMS.Core.Scheduling;

/// <summary>
/// Dependency-free parser for standard 5-field cron expressions.
/// Supports *, number, comma list, range and step.
/// Sunday can be 0 or 7.
/// </summary>
public sealed class CronSchedule
{
    private readonly CronField _minute;
    private readonly CronField _hour;
    private readonly CronField _day;
    private readonly CronField _month;
    private readonly CronField _dayOfWeek;

    private CronSchedule(
        CronField minute,
        CronField hour,
        CronField day,
        CronField month,
        CronField dayOfWeek)
    {
        _minute = minute;
        _hour = hour;
        _day = day;
        _month = month;
        _dayOfWeek = dayOfWeek;
    }

    public static CronSchedule Parse(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            throw new FormatException("Cron expression is empty.");

        var parts = expression.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length != 5)
            throw new FormatException(
                $"Cron expression must contain 5 fields, but {parts.Length} were supplied.");

        return new CronSchedule(
            CronField.Parse(parts[0], 0, 59, false),
            CronField.Parse(parts[1], 0, 23, false),
            CronField.Parse(parts[2], 1, 31, false),
            CronField.Parse(parts[3], 1, 12, false),
            CronField.Parse(parts[4], 0, 7, true));
    }

    public bool IsMatch(DateTime localDateTime)
    {
        var dow = (int)localDateTime.DayOfWeek;

        return
            _minute.Contains(localDateTime.Minute) &&
            _hour.Contains(localDateTime.Hour) &&
            _day.Contains(localDateTime.Day) &&
            _month.Contains(localDateTime.Month) &&
            _dayOfWeek.Contains(dow);
    }

    public DateTime? GetNextOccurrence(
        DateTime afterLocal,
        int maxSearchDays = 366)
    {
        var candidate = new DateTime(
                afterLocal.Year,
                afterLocal.Month,
                afterLocal.Day,
                afterLocal.Hour,
                afterLocal.Minute,
                0,
                afterLocal.Kind)
            .AddMinutes(1);

        var limit = candidate.AddDays(Math.Max(1, maxSearchDays));

        while (candidate <= limit)
        {
            if (IsMatch(candidate))
                return candidate;

            candidate = candidate.AddMinutes(1);
        }

        return null;
    }

    private sealed class CronField
    {
        private readonly HashSet<int> _values;

        private CronField(HashSet<int> values)
        {
            _values = values;
        }

        public bool Contains(int value) =>
            _values.Contains(value);

        public static CronField Parse(
            string text,
            int min,
            int max,
            bool normalizeSunday)
        {
            var values = new HashSet<int>();

            foreach (var rawPart in text.Split(
                         ',',
                         StringSplitOptions.RemoveEmptyEntries))
            {
                ParsePart(
                    rawPart.Trim(),
                    min,
                    max,
                    normalizeSunday,
                    values);
            }

            if (values.Count == 0)
                throw new FormatException($"Cron field '{text}' contains no values.");

            return new CronField(values);
        }

        private static void ParsePart(
            string part,
            int min,
            int max,
            bool normalizeSunday,
            HashSet<int> result)
        {
            var step = 1;
            var rangeText = part;

            var slash = part.IndexOf('/');
            if (slash >= 0)
            {
                rangeText = part[..slash];

                if (!int.TryParse(
                        part[(slash + 1)..],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out step) ||
                    step <= 0)
                {
                    throw new FormatException($"Invalid cron step in '{part}'.");
                }
            }

            int start;
            int end;

            if (rangeText == "*")
            {
                start = min;
                end = max;
            }
            else
            {
                var dash = rangeText.IndexOf('-');

                if (dash >= 0)
                {
                    start = ParseNumber(rangeText[..dash], min, max);
                    end = ParseNumber(rangeText[(dash + 1)..], min, max);

                    if (end < start)
                        throw new FormatException($"Invalid cron range '{part}'.");
                }
                else
                {
                    start = ParseNumber(rangeText, min, max);
                    end = start;
                }
            }

            for (var value = start; value <= end; value += step)
            {
                result.Add(
                    normalizeSunday && value == 7
                        ? 0
                        : value);
            }
        }

        private static int ParseNumber(
            string text,
            int min,
            int max)
        {
            if (!int.TryParse(
                    text,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var value) ||
                value < min ||
                value > max)
            {
                throw new FormatException(
                    $"Cron value '{text}' is outside allowed range {min}-{max}.");
            }

            return value;
        }
    }
}
