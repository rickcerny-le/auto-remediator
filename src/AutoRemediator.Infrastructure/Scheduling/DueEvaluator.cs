namespace AutoRemediator.Infrastructure.Scheduling;

/// <summary>Outcome of evaluating a repository's schedule at a tick.</summary>
public enum DueResult
{
    NotDue,
    Due,

    /// <summary>The stored cron expression cannot be parsed (data that predates validation).</summary>
    InvalidCron,
}

/// <summary>
/// The due rule: a repository is due when it has never been scheduled, or when the next cron
/// occurrence after its last scheduled time is at or before now.
/// </summary>
public static class DueEvaluator
{
    public static DueResult Evaluate(string cron, DateTimeOffset? lastScheduledAtUtc, DateTimeOffset now)
    {
        if (!CronSchedule.TryParse(cron, out var schedule, out _) || schedule is null)
        {
            return DueResult.InvalidCron;
        }

        if (lastScheduledAtUtc is null)
        {
            return DueResult.Due;
        }

        return schedule.NextAfter(lastScheduledAtUtc.Value) is { } next && next <= now
            ? DueResult.Due
            : DueResult.NotDue;
    }

    /// <summary>True when due. An unparseable cron is not due (the caller logs it).</summary>
    public static bool IsDue(string cron, DateTimeOffset? lastScheduledAtUtc, DateTimeOffset now) =>
        Evaluate(cron, lastScheduledAtUtc, now) == DueResult.Due;
}
