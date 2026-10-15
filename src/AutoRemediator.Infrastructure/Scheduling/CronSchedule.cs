using Cronos;

namespace AutoRemediator.Infrastructure.Scheduling;

/// <summary>
/// A validated 5-field cron schedule evaluated in UTC (Cronos, <see cref="CronFormat.Standard"/>;
/// macros such as <c>@daily</c> are allowed).
/// </summary>
public sealed class CronSchedule
{
    /// <summary>
    /// Smallest allowed gap between consecutive occurrences (matches the scheduler tick).
    /// To drop the floor, remove <see cref="TooFrequent"/> and its call in <see cref="TryParse"/>.
    /// </summary>
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromMinutes(15);

    public const string SecondsNotSupported =
        "Seconds are not supported; use 5 fields (minute hour day-of-month month day-of-week), UTC.";

    public const string TooFrequentMessage = "Schedules more frequent than every 15 minutes are not supported.";

    /// <summary>Fixed instant so validation is deterministic.</summary>
    private static readonly DateTimeOffset ReferenceInstant = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const int OccurrencesChecked = 100;

    private readonly CronExpression _expression;

    private CronSchedule(string expression, CronExpression parsed)
    {
        Expression = expression;
        _expression = parsed;
    }

    /// <summary>The expression as given, trimmed.</summary>
    public string Expression { get; }

    public static bool TryParse(string? expression, out CronSchedule? schedule, out string? error)
    {
        schedule = null;
        error = null;

        var text = expression?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            error = "A cron expression is required.";
            return false;
        }

        // Macros (@daily) are one token; otherwise 6+ whitespace-separated fields means seconds.
        if (!text.StartsWith('@')
            && text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length >= 6)
        {
            error = SecondsNotSupported;
            return false;
        }

        CronExpression parsed;
        try
        {
            parsed = CronExpression.Parse(text, CronFormat.Standard);
        }
        catch (CronFormatException ex)
        {
            error = $"Invalid cron expression: {ex.Message}";
            return false;
        }

        if (TooFrequent(parsed))
        {
            error = TooFrequentMessage;
            return false;
        }

        schedule = new CronSchedule(text, parsed);
        return true;
    }

    /// <summary>Next occurrence strictly after <paramref name="utc"/>, or null if none.</summary>
    public DateTimeOffset? NextAfter(DateTimeOffset utc) =>
        _expression.GetNextOccurrence(utc.ToUniversalTime(), TimeZoneInfo.Utc, inclusive: false);

    /// <summary>The next <paramref name="count"/> occurrences strictly after <paramref name="fromUtc"/>.</summary>
    public IReadOnlyList<DateTimeOffset> Next(DateTimeOffset fromUtc, int count)
    {
        var result = new List<DateTimeOffset>();
        var cursor = fromUtc;
        while (result.Count < count && NextAfter(cursor) is { } next)
        {
            result.Add(next);
            cursor = next;
        }

        return result;
    }

    public override string ToString() => Expression;

    private static bool TooFrequent(CronExpression parsed)
    {
        var previous = parsed.GetNextOccurrence(ReferenceInstant, TimeZoneInfo.Utc, inclusive: false);
        for (var i = 1; i < OccurrencesChecked && previous is not null; i++)
        {
            var next = parsed.GetNextOccurrence(previous.Value, TimeZoneInfo.Utc, inclusive: false);
            if (next is null)
            {
                break;
            }

            if (next.Value - previous.Value < MinimumInterval)
            {
                return true;
            }

            previous = next;
        }

        return false;
    }
}
