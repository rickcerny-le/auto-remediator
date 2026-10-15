using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Scheduling;

namespace AutoRemediator.Infrastructure.Tests;

public class DueEvaluatorTests
{
    private static readonly DateTimeOffset Today = new(2026, 3, 10, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Never_scheduled_is_due()
        => Assert.True(DueEvaluator.IsDue("0 3 * * *", null, Today.AddHours(1)));

    [Fact]
    public void Never_scheduled_is_due_whatever_the_schedule()
        => Assert.True(DueEvaluator.IsDue("0 3 * * 1", null, Today.AddHours(10)));

    [Fact]
    public void Daily_3am_is_due_at_3_00_30_and_not_at_2_59()
    {
        var last = Today.AddDays(-1).AddHours(3).AddSeconds(10);

        Assert.True(DueEvaluator.IsDue("0 3 * * *", last, Today.AddHours(3).AddSeconds(30)));
        Assert.False(DueEvaluator.IsDue("0 3 * * *", last, Today.AddHours(2).AddMinutes(59)));
    }

    [Fact]
    public void Built_in_default_is_not_due_at_23_59_and_due_at_00_00_30()
    {
        var last = Today.AddDays(-1).AddSeconds(10);

        Assert.False(DueEvaluator.IsDue(BuiltInDefaults.Schedule, last, Today.AddMinutes(-1)));
        Assert.True(DueEvaluator.IsDue(BuiltInDefaults.Schedule, last, Today.AddSeconds(30)));
    }

    [Fact]
    public void After_missed_days_one_claim_makes_the_next_tick_not_due()
    {
        var now = Today.AddSeconds(30);

        Assert.True(DueEvaluator.IsDue(BuiltInDefaults.Schedule, Today.AddDays(-3), now));

        // The run sets last = now, so the following tick is not due: one run, no backfill.
        Assert.False(DueEvaluator.IsDue(BuiltInDefaults.Schedule, now, now.AddMinutes(15)));
    }

    [Fact]
    public void An_invalid_cron_is_not_due()
    {
        Assert.False(DueEvaluator.IsDue("not a cron", null, Today));
        Assert.False(DueEvaluator.IsDue("0 0 * * * *", Today.AddDays(-1), Today));
        Assert.Equal(DueResult.InvalidCron, DueEvaluator.Evaluate("", null, Today));
    }
}
