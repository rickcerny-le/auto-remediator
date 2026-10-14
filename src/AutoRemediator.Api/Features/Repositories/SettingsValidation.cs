using AutoRemediator.Contracts.Dtos;
using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Scheduling;

namespace AutoRemediator.Api.Features.Repositories;

/// <summary>
/// Field-level validation for repository and global settings. Keys are camelCase field names so
/// the UI can read <c>errors[field][0]</c>.
/// </summary>
public static class SettingsValidation
{
    public const int MaxEntryLength = 200;
    public const string NeverFiresMessage = "This cron expression never fires.";

    public static Dictionary<string, string[]> Validate(RepositorySettingsDto settings)
    {
        var errors = new Dictionary<string, List<string>>();
        ValidateStrategy(settings.Strategy, errors);
        ValidateSchedule("schedule", settings.Schedule, errors);
        ValidateList("patterns", settings.Patterns, errors);
        ValidateList("excludes", settings.Excludes, errors);
        ValidateList("feeds", settings.Feeds, errors);
        ValidateList("ignore", settings.Ignore, errors);
        return Finish(errors);
    }

    public static Dictionary<string, string[]> Validate(TargetingSettingsDto settings)
    {
        var errors = new Dictionary<string, List<string>>();
        // The global strategy is required; unknown values are rejected rather than coerced.
        if (string.IsNullOrWhiteSpace(settings.Strategy))
        {
            Add(errors, "strategy", "Strategy must be Patch, Minor or Major.");
        }
        else
        {
            ValidateStrategy(settings.Strategy, errors);
        }

        ValidateSchedule("defaultSchedule", settings.DefaultSchedule, errors);
        ValidateList("patterns", settings.Patterns, errors);
        ValidateList("excludes", settings.Excludes, errors);
        ValidateList("feeds", settings.Feeds, errors);
        ValidateList("ignore", settings.Ignore, errors);
        return Finish(errors);
    }

    public static bool TryParseStrategy(string? value, out UpdateStrategy strategy) =>
        Enum.TryParse(value?.Trim(), ignoreCase: true, out strategy)
        && Enum.IsDefined(strategy)
        && !int.TryParse(value?.Trim(), out _);

    private static void ValidateStrategy(string? value, Dictionary<string, List<string>> errors)
    {
        if (value is not null && !TryParseStrategy(value, out _))
        {
            Add(errors, "strategy", "Strategy must be Patch, Minor or Major.");
        }
    }

    private static void ValidateSchedule(string field, string? value, Dictionary<string, List<string>> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!CronSchedule.TryParse(value, out var schedule, out var error))
        {
            Add(errors, field, error ?? "Invalid cron expression.");
        }
        else if (schedule!.NextAfter(DateTimeOffset.UtcNow) is null)
        {
            // Parses but can never fire (for example 0 0 31 2 *); it would silently never run.
            Add(errors, field, NeverFiresMessage);
        }
    }

    private static void ValidateList(string field, IReadOnlyList<string>? values, Dictionary<string, List<string>> errors)
    {
        if (values is null)
        {
            return;
        }

        foreach (var value in values)
        {
            var entry = value?.Trim();
            if (string.IsNullOrEmpty(entry))
            {
                Add(errors, field, "Entries must not be empty.");
            }
            else if (entry.Length > MaxEntryLength)
            {
                Add(errors, field, $"Entries must be at most {MaxEntryLength} characters.");
            }
            else if (entry.Contains(','))
            {
                Add(errors, field, $"Entry '{entry}' must not contain a comma; add entries separately.");
            }
        }
    }

    private static void Add(Dictionary<string, List<string>> errors, string field, string message)
    {
        if (!errors.TryGetValue(field, out var list))
        {
            errors[field] = list = [];
        }

        if (!list.Contains(message))
        {
            list.Add(message);
        }
    }

    private static Dictionary<string, string[]> Finish(Dictionary<string, List<string>> errors) =>
        errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
}
