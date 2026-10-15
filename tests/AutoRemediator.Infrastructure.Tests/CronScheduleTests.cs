using AutoRemediator.Infrastructure.Scheduling;

namespace AutoRemediator.Infrastructure.Tests;

public class CronScheduleTests
{
    private static CronSchedule Parse(string expression)
    {
        Assert.True(CronSchedule.TryParse(expression, out var schedule, out var error), error);
        return schedule!;
    }

    [Fact]
    public void Valid_five_field_expression_is_accepted_and_trimmed()
    {
        var schedule = Parse("  0 3 * * *  ");

        Assert.Equal("0 3 * * *", schedule.Expression);
    }

    [Fact]
    public void Daily_macro_is_accepted()
    {
        var schedule = Parse("@daily");

        Assert.Equal(new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero),
            schedule.NextAfter(new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero)));
    }

    [Theory]
    [InlineData("not a cron")]
    [InlineData("99 * * * *")]
    [InlineData("* * *")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Invalid_expressions_are_rejected(string? expression)
    {
        Assert.False(CronSchedule.TryParse(expression, out var schedule, out var error));
        Assert.Null(schedule);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Six_fields_are_rejected_with_the_seconds_message()
    {
        Assert.False(CronSchedule.TryParse("0 0 3 * * *", out var schedule, out var error));

        Assert.Null(schedule);
        Assert.Equal("Seconds are not supported; use 5 fields (minute hour day-of-month month day-of-week), UTC.", error);
    }

    [Theory]
    [InlineData("*/5 * * * *")]
    [InlineData("* * * * *")]
    [InlineData("0,10 * * * *")]
    public void Expressions_more_frequent_than_fifteen_minutes_are_rejected(string expression)
    {
        Assert.False(CronSchedule.TryParse(expression, out _, out var error));

        Assert.Equal("Schedules more frequent than every 15 minutes are not supported.", error);
    }

    [Theory]
    [InlineData("*/15 * * * *")]
    [InlineData("0,15,30,45 * * * *")]
    [InlineData("0 */6 * * *")]
    [InlineData("0 9 * * 1")]
    [InlineData("0 0 29 2 *")]
    public void Expressions_at_or_above_the_minimum_interval_are_accepted(string expression)
    {
        Parse(expression);
    }

    [Fact]
    public void NextAfter_is_exclusive()
    {
        var schedule = Parse("0 0 * * *");
        var onOccurrence = new DateTimeOffset(2026, 10, 14, 0, 0, 0, TimeSpan.Zero);

        Assert.Equal(onOccurrence.AddDays(1), schedule.NextAfter(onOccurrence));
        Assert.Equal(onOccurrence.AddDays(1), schedule.NextAfter(onOccurrence.AddSeconds(1)));
    }

    [Fact]
    public void NextAfter_crosses_a_month_boundary_in_utc()
    {
        var schedule = Parse("30 23 * * *");
        // 2026-10-31 23:45 at +02:00 is 21:45 UTC, so the next 23:30 UTC is the same UTC day.
        var local = new DateTimeOffset(2026, 10, 31, 23, 45, 0, TimeSpan.FromHours(2));
        var next = schedule.NextAfter(local);

        Assert.Equal(new DateTimeOffset(2026, 10, 31, 23, 30, 0, TimeSpan.Zero), next);
        Assert.Equal(TimeSpan.Zero, next!.Value.Offset);

        var afterMonthEnd = schedule.NextAfter(new DateTimeOffset(2026, 10, 31, 23, 30, 0, TimeSpan.Zero));
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 23, 30, 0, TimeSpan.Zero), afterMonthEnd);
        Assert.Equal(TimeSpan.Zero, afterMonthEnd!.Value.Offset);
    }

    [Fact]
    public void Next_returns_the_requested_number_of_occurrences_in_order()
    {
        var schedule = Parse("0 0 1 * *");

        var next = schedule.Next(new DateTimeOffset(2026, 10, 14, 0, 0, 0, TimeSpan.Zero), 3);

        Assert.Equal(
            [
                new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero),
            ],
            next);
    }

    [Fact]
    public void Next_with_zero_count_is_empty()
    {
        Assert.Empty(Parse("0 0 * * *").Next(DateTimeOffset.UnixEpoch, 0));
    }

    [Fact]
    public void Due_calculation_helper_next_after_last_scheduled_is_at_or_before_now()
    {
        // The scheduler's due rule: next occurrence after the last scheduled time <= now.
        var schedule = Parse("0 3 * * *");
        var last = new DateTimeOffset(2026, 10, 14, 3, 0, 0, TimeSpan.Zero);

        Assert.True(schedule.NextAfter(last) <= new DateTimeOffset(2026, 10, 15, 3, 0, 0, TimeSpan.Zero));
        Assert.False(schedule.NextAfter(last) <= new DateTimeOffset(2026, 10, 15, 2, 59, 0, TimeSpan.Zero));
    }
}
