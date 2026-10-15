using AutoRemediator.Domain;

namespace AutoRemediator.Domain.Tests;

public class EffectiveSettingsResolverTests
{
    private static TargetingSettings Global(
        string[]? patterns = null,
        string[]? excludes = null,
        string[]? feeds = null,
        UpdatePolicy? policy = null,
        string? schedule = null) =>
        new(patterns ?? ["Contoso.*"], excludes ?? [], feeds ?? ["https://feed/v3/index.json"], policy, schedule);

    private static string[] Values(IReadOnlyList<EffectiveItem> items) => items.Select(i => i.Value).ToArray();

    [Fact]
    public void All_inherit_equals_global_with_global_sources()
    {
        var global = Global(
            patterns: ["Contoso.*", "Fabrikam.*"],
            excludes: ["Contoso.Legacy"],
            feeds: ["https://a", "https://b"],
            policy: new UpdatePolicy(UpdateStrategy.Major, ["Tailspin.*"], AllowPrerelease: true));

        var effective = EffectiveSettingsResolver.Resolve(global, RepositorySettings.Inherit);

        Assert.True(effective.InheritGlobal);
        Assert.Equal(["Contoso.*", "Fabrikam.*"], Values(effective.Patterns));
        Assert.All(effective.Patterns, i => Assert.Equal(SettingSource.Global, i.Source));
        Assert.All(effective.Excludes, i => Assert.Equal(SettingSource.Global, i.Source));
        Assert.All(effective.Feeds, i => Assert.Equal(SettingSource.Global, i.Source));
        Assert.All(effective.Ignore, i => Assert.Equal(SettingSource.Global, i.Source));
        Assert.Equal(["Contoso.Legacy"], Values(effective.Excludes));
        Assert.Equal(["https://a", "https://b"], Values(effective.Feeds));
        Assert.Equal(["Tailspin.*"], Values(effective.Ignore));
        Assert.Equal(new EffectiveValue<UpdateStrategy>(UpdateStrategy.Major, SettingSource.Global), effective.Strategy);
        Assert.Equal(new EffectiveValue<bool>(true, SettingSource.Global), effective.AllowPrerelease);
        Assert.Empty(effective.Warnings);
    }

    [Fact]
    public void Repo_pattern_is_appended_after_global_with_repository_source()
    {
        var global = Global(patterns: ["Contoso.*"]);
        var repo = new RepositorySettings(patterns: ["Northwind.*"]);

        var effective = EffectiveSettingsResolver.Resolve(global, repo);

        Assert.Equal(
            [new EffectiveItem("Contoso.*", SettingSource.Global), new EffectiveItem("Northwind.*", SettingSource.Repository)],
            effective.Patterns);
    }

    [Fact]
    public void Duplicate_in_different_case_is_one_entry_marked_both_keeping_first_spelling()
    {
        var global = Global(patterns: ["Contoso.*", "Fabrikam.*"]);
        var repo = new RepositorySettings(patterns: ["CONTOSO.*", "Northwind.*"]);

        var effective = EffectiveSettingsResolver.Resolve(global, repo);

        Assert.Equal(
            [
                new EffectiveItem("Contoso.*", SettingSource.Both),
                new EffectiveItem("Fabrikam.*", SettingSource.Global),
                new EffectiveItem("Northwind.*", SettingSource.Repository),
            ],
            effective.Patterns);
    }

    [Fact]
    public void Duplicates_within_a_single_list_collapse_and_do_not_become_both()
    {
        var global = Global(patterns: ["Contoso.*", "contoso.*"]);
        var repo = new RepositorySettings(patterns: ["Northwind.*", "NORTHWIND.*"]);

        var effective = EffectiveSettingsResolver.Resolve(global, repo);

        Assert.Equal(
            [new EffectiveItem("Contoso.*", SettingSource.Global), new EffectiveItem("Northwind.*", SettingSource.Repository)],
            effective.Patterns);
    }

    [Fact]
    public void Excludes_feeds_and_ignore_union_the_same_way()
    {
        var global = Global(
            excludes: ["A.*"],
            feeds: ["https://a"],
            policy: new UpdatePolicy(UpdateStrategy.Minor, ["Ign.One"]));
        var repo = new RepositorySettings(
            excludes: ["a.*", "B.*"],
            feeds: ["https://b"],
            ignore: ["ign.one", "Ign.Two"]);

        var effective = EffectiveSettingsResolver.Resolve(global, repo);

        Assert.Equal([new("A.*", SettingSource.Both), new("B.*", SettingSource.Repository)], effective.Excludes);
        Assert.Equal([new("https://a", SettingSource.Global), new("https://b", SettingSource.Repository)], effective.Feeds);
        Assert.Equal([new("Ign.One", SettingSource.Both), new("Ign.Two", SettingSource.Repository)], effective.Ignore);
    }

    [Fact]
    public void Repo_strategy_and_prerelease_win_when_inheriting()
    {
        var global = Global(policy: new UpdatePolicy(UpdateStrategy.Minor, [], AllowPrerelease: false));
        var repo = new RepositorySettings(strategy: UpdateStrategy.Major, allowPrerelease: true);

        var effective = EffectiveSettingsResolver.Resolve(global, repo);

        Assert.Equal(new EffectiveValue<UpdateStrategy>(UpdateStrategy.Major, SettingSource.Repository), effective.Strategy);
        Assert.Equal(new EffectiveValue<bool>(true, SettingSource.Repository), effective.AllowPrerelease);
    }

    [Fact]
    public void Repo_explicit_false_prerelease_beats_global_true()
    {
        var global = Global(policy: new UpdatePolicy(UpdateStrategy.Minor, [], AllowPrerelease: true));
        var repo = new RepositorySettings(allowPrerelease: false);

        var effective = EffectiveSettingsResolver.Resolve(global, repo);

        Assert.Equal(new EffectiveValue<bool>(false, SettingSource.Repository), effective.AllowPrerelease);
    }

    [Fact]
    public void Null_strategy_and_prerelease_inherit_global()
    {
        var global = Global(policy: new UpdatePolicy(UpdateStrategy.Patch, [], AllowPrerelease: true));

        var effective = EffectiveSettingsResolver.Resolve(global, RepositorySettings.Inherit);

        Assert.Equal(new EffectiveValue<UpdateStrategy>(UpdateStrategy.Patch, SettingSource.Global), effective.Strategy);
        Assert.Equal(new EffectiveValue<bool>(true, SettingSource.Global), effective.AllowPrerelease);
    }

    [Fact]
    public void Inheritance_off_uses_only_repo_lists_and_built_in_scalars()
    {
        var global = Global(
            patterns: ["Contoso.*"],
            excludes: ["Contoso.Legacy"],
            feeds: ["https://global"],
            policy: new UpdatePolicy(UpdateStrategy.Major, ["Tailspin.*"], AllowPrerelease: true));
        var repo = new RepositorySettings(
            inheritGlobal: false,
            patterns: ["AdventureWorks.*"],
            feeds: ["https://local"]);

        var effective = EffectiveSettingsResolver.Resolve(global, repo);

        Assert.False(effective.InheritGlobal);
        Assert.Equal([new("AdventureWorks.*", SettingSource.Repository)], effective.Patterns);
        Assert.Empty(effective.Excludes);
        Assert.Equal([new("https://local", SettingSource.Repository)], effective.Feeds);
        Assert.Empty(effective.Ignore);
        Assert.Equal(new EffectiveValue<UpdateStrategy>(UpdateStrategy.Minor, SettingSource.Default), effective.Strategy);
        Assert.Equal(new EffectiveValue<bool>(false, SettingSource.Default), effective.AllowPrerelease);
        Assert.Empty(effective.Warnings);
    }

    [Fact]
    public void Inheritance_off_repo_scalars_still_win()
    {
        var global = Global(policy: new UpdatePolicy(UpdateStrategy.Major, [], AllowPrerelease: true));
        var repo = new RepositorySettings(
            inheritGlobal: false, patterns: ["X.*"], feeds: ["https://f"],
            strategy: UpdateStrategy.Patch, allowPrerelease: true);

        var effective = EffectiveSettingsResolver.Resolve(global, repo);

        Assert.Equal(new EffectiveValue<UpdateStrategy>(UpdateStrategy.Patch, SettingSource.Repository), effective.Strategy);
        Assert.Equal(new EffectiveValue<bool>(true, SettingSource.Repository), effective.AllowPrerelease);
    }

    [Fact]
    public void Inheritance_off_with_no_patterns_yields_empty_patterns_and_a_warning()
    {
        var repo = new RepositorySettings(inheritGlobal: false, feeds: ["https://f"]);

        var effective = EffectiveSettingsResolver.Resolve(Global(), repo);

        Assert.Empty(effective.Patterns);
        Assert.Empty(effective.Targeting.Patterns);
        Assert.Equal(["No package patterns: nothing will be updated for this repository."], effective.Warnings);
    }

    [Fact]
    public void No_patterns_and_no_feeds_warns_only_about_patterns()
    {
        var effective = EffectiveSettingsResolver.Resolve(
            Global(patterns: [], feeds: []), RepositorySettings.Inherit);

        Assert.Equal([EffectiveSettingsResolver.NoPatternsWarning], effective.Warnings);
    }

    [Fact]
    public void Patterns_without_feeds_warns_about_feeds()
    {
        var effective = EffectiveSettingsResolver.Resolve(
            Global(patterns: ["Contoso.*"], feeds: []), RepositorySettings.Inherit);

        Assert.Equal(["No feeds: package versions cannot be resolved."], effective.Warnings);
    }

    [Fact]
    public void Inheritance_off_with_patterns_but_no_feeds_warns_even_when_global_has_feeds()
    {
        var repo = new RepositorySettings(inheritGlobal: false, patterns: ["X.*"]);

        var effective = EffectiveSettingsResolver.Resolve(Global(feeds: ["https://global"]), repo);

        Assert.Equal([EffectiveSettingsResolver.NoFeedsWarning], effective.Warnings);
    }

    [Fact]
    public void Repo_schedule_wins_over_global_default()
    {
        var effective = EffectiveSettingsResolver.Resolve(
            Global(schedule: "0 */6 * * *"), new RepositorySettings(schedule: "0 3 * * *"));

        Assert.Equal(new EffectiveValue<string>("0 3 * * *", SettingSource.Repository), effective.Schedule);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Schedule_falls_back_to_global_default_regardless_of_inheritance(bool inherit)
    {
        var repo = new RepositorySettings(inheritGlobal: inherit, patterns: ["X.*"]);

        var effective = EffectiveSettingsResolver.Resolve(Global(schedule: "0 */6 * * *"), repo);

        Assert.Equal(new EffectiveValue<string>("0 */6 * * *", SettingSource.Global), effective.Schedule);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Schedule_falls_back_to_built_in_default_when_unset_everywhere(bool inherit)
    {
        var repo = new RepositorySettings(inheritGlobal: inherit);

        var effective = EffectiveSettingsResolver.Resolve(Global(), repo);

        Assert.Equal(new EffectiveValue<string>("0 0 * * *", SettingSource.Default), effective.Schedule);
        Assert.Equal("0 0 * * *", BuiltInDefaults.Schedule);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Repo_schedule_applies_regardless_of_inheritance(bool inherit)
    {
        var repo = new RepositorySettings(inheritGlobal: inherit, schedule: "0 3 * * *");

        var effective = EffectiveSettingsResolver.Resolve(Global(), repo);

        Assert.Equal(SettingSource.Repository, effective.Schedule.Source);
    }

    [Fact]
    public void Blank_global_default_schedule_is_treated_as_unset()
    {
        var effective = EffectiveSettingsResolver.Resolve(Global(schedule: "  "), RepositorySettings.Inherit);

        Assert.Equal(SettingSource.Default, effective.Schedule.Source);
    }

    [Fact]
    public void Targeting_equals_effective_lists_and_policy()
    {
        var global = Global(
            patterns: ["Contoso.*"],
            excludes: ["Contoso.Legacy"],
            feeds: ["https://a"],
            policy: new UpdatePolicy(UpdateStrategy.Minor, ["Ign.One"]));
        var repo = new RepositorySettings(
            patterns: ["Northwind.*"],
            excludes: ["contoso.legacy"],
            feeds: ["https://b"],
            ignore: ["Ign.Two"],
            strategy: UpdateStrategy.Patch,
            allowPrerelease: true);

        var effective = EffectiveSettingsResolver.Resolve(global, repo);

        Assert.Equal(Values(effective.Patterns), effective.Targeting.Patterns);
        Assert.Equal(Values(effective.Excludes), effective.Targeting.Excludes);
        Assert.Equal(Values(effective.Feeds), effective.Targeting.Feeds);
        Assert.Equal(Values(effective.Ignore), effective.Targeting.Policy.Ignore);
        Assert.Equal(["Contoso.*", "Northwind.*"], effective.Targeting.Patterns);
        Assert.Equal(["Ign.One", "Ign.Two"], effective.Targeting.Policy.Ignore);
        Assert.Equal(UpdateStrategy.Patch, effective.Targeting.Policy.Strategy);
        Assert.True(effective.Targeting.Policy.AllowPrerelease);
    }

    [Fact]
    public void Resolve_does_not_mutate_inputs()
    {
        var global = Global(patterns: ["Contoso.*"]);
        var repo = new RepositorySettings(patterns: ["contoso.*", "X.*"]);

        EffectiveSettingsResolver.Resolve(global, repo);

        Assert.Equal(["Contoso.*"], global.Patterns);
        Assert.Equal(["contoso.*", "X.*"], repo.Patterns);
    }
}

public class RepositorySettingsTests
{
    [Fact]
    public void Inherit_has_no_overrides_and_inherits()
    {
        var inherit = RepositorySettings.Inherit;

        Assert.True(inherit.InheritGlobal);
        Assert.False(inherit.HasOverrides);
        Assert.Empty(inherit.Patterns);
        Assert.Null(inherit.Strategy);
        Assert.Null(inherit.AllowPrerelease);
        Assert.Null(inherit.Schedule);
    }

    [Fact]
    public void Lists_are_trimmed_and_blanks_dropped_and_null_becomes_empty()
    {
        var settings = new RepositorySettings(patterns: ["  A.* ", "", "   ", "B.*"], excludes: null);

        Assert.Equal(["A.*", "B.*"], settings.Patterns);
        Assert.Empty(settings.Excludes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Blank_schedule_becomes_null(string? schedule)
    {
        Assert.Null(new RepositorySettings(schedule: schedule).Schedule);
    }

    [Fact]
    public void Schedule_is_trimmed()
    {
        Assert.Equal("0 3 * * *", new RepositorySettings(schedule: " 0 3 * * * ").Schedule);
    }

    [Fact]
    public void HasOverrides_is_true_for_any_list_or_scalar()
    {
        Assert.True(new RepositorySettings(patterns: ["A"]).HasOverrides);
        Assert.True(new RepositorySettings(excludes: ["A"]).HasOverrides);
        Assert.True(new RepositorySettings(feeds: ["A"]).HasOverrides);
        Assert.True(new RepositorySettings(ignore: ["A"]).HasOverrides);
        Assert.True(new RepositorySettings(strategy: UpdateStrategy.Patch).HasOverrides);
        Assert.True(new RepositorySettings(allowPrerelease: false).HasOverrides);
        Assert.True(new RepositorySettings(schedule: "0 3 * * *").HasOverrides);
        Assert.False(new RepositorySettings(inheritGlobal: false, patterns: ["  "]).HasOverrides);
    }

    [Fact]
    public void TargetingSettings_default_schedule_is_trimmed_and_blank_becomes_null()
    {
        Assert.Equal("0 3 * * *", new TargetingSettings(["A"], defaultSchedule: " 0 3 * * * ").DefaultSchedule);
        Assert.Null(new TargetingSettings(["A"], defaultSchedule: "  ").DefaultSchedule);
        Assert.Null(new TargetingSettings(["A"]).DefaultSchedule);
    }
}
