using System.Text.Json;

namespace AutoRemediator.Infrastructure.Configuration;

/// <summary>JSON helpers for string-list columns shared by the configuration stores.</summary>
internal static class JsonLists
{
    /// <summary>Serializes a list; always returns a JSON array.</summary>
    public static string Serialize(IReadOnlyList<string> values) => JsonSerializer.Serialize(values);

    /// <summary>Serializes a list, or null when empty (keeps rows lean; null reads back as empty).</summary>
    public static string? SerializeOrNull(IReadOnlyList<string> values) => values.Count == 0 ? null : Serialize(values);

    /// <summary>Null or blank reads as empty.</summary>
    public static List<string> Deserialize(string? json)
        => string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<string>>(json) ?? [];
}
