using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace RuleWright.Json.SystemText;

/// <summary>
/// Helpers for using JSON payloads as dynamic facts: converts a <see cref="JsonElement"/>
/// into the <c>IDictionary&lt;string, object&gt;</c> shape the engine's interpreter
/// evaluates (reported as <see cref="RuleWright.Core.CompilationMode.Interpreted"/>).
/// </summary>
public static class SystemTextJsonFacts
{
    /// <summary>
    /// Recursively converts a JSON object into a dictionary fact. Nested objects become
    /// nested dictionaries; arrays become <c>object?[]</c>; numbers become <see cref="long"/>
    /// when integral, otherwise <see cref="decimal"/> when exactly representable, otherwise
    /// <see cref="double"/>. Keys match exactly (ordinal, case-sensitive); to match field paths
    /// without regard to case, use <see cref="ToDictionary(JsonElement, IEqualityComparer{string})"/>.
    /// </summary>
    /// <param name="element">A JSON element of kind <see cref="JsonValueKind.Object"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="element"/> is not a JSON object.</exception>
    public static Dictionary<string, object?> ToDictionary(JsonElement element)
        => ToDictionary(element, StringComparer.Ordinal);

    /// <summary>
    /// Recursively converts a JSON object into a dictionary fact whose keys, at every level, compare
    /// with <paramref name="keyComparer"/>. The interpreter looks each field-path segment up through
    /// the dictionary's own comparer, so with <see cref="StringComparer.OrdinalIgnoreCase"/> a rule
    /// reading <c>Order.Total</c> finds a camelCase payload's <c>order.total</c>, just as a typed
    /// fact's members already match. Values convert exactly as in <see cref="ToDictionary(JsonElement)"/>.
    /// </summary>
    /// <param name="element">A JSON element of kind <see cref="JsonValueKind.Object"/>.</param>
    /// <param name="keyComparer">How keys compare, such as <see cref="StringComparer.OrdinalIgnoreCase"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="keyComparer"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="element"/> is not a JSON object, or one object has two differently spelled
    /// properties that <paramref name="keyComparer"/> treats as the same key (such as <c>Total</c> and
    /// <c>total</c> under a case-insensitive comparer). That is reported rather than letting one
    /// value silently replace the other.
    /// </exception>
    public static Dictionary<string, object?> ToDictionary(JsonElement element, IEqualityComparer<string> keyComparer)
    {
        if (keyComparer is null)
        {
            throw new ArgumentNullException(nameof(keyComparer));
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException($"A dictionary fact requires a JSON object, not {element.ValueKind}.", nameof(element));
        }

        var result = new Dictionary<string, object?>(keyComparer);
        var spelling = new Dictionary<string, string>(keyComparer);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (spelling.TryGetValue(property.Name, out string? first) && !string.Equals(first, property.Name, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Properties '{first}' and '{property.Name}' are the same key under the chosen key comparer, so one value would silently replace the other.",
                    nameof(element));
            }

            spelling[property.Name] = property.Name;
            result[property.Name] = ToClrValue(property.Value, keyComparer);
        }

        return result;
    }

    private static object? ToClrValue(JsonElement element, IEqualityComparer<string> keyComparer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                return ToDictionary(element, keyComparer);

            case JsonValueKind.Array:
                var items = new object?[element.GetArrayLength()];
                int index = 0;
                foreach (JsonElement item in element.EnumerateArray())
                {
                    items[index++] = ToClrValue(item, keyComparer);
                }

                return items;

            case JsonValueKind.String:
                return element.GetString();

            case JsonValueKind.Number:
                string raw = element.GetRawText();
                if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long integral))
                {
                    return integral;
                }

                if (decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal exact))
                {
                    return exact;
                }

                return element.GetDouble();

            case JsonValueKind.True:
                return true;

            case JsonValueKind.False:
                return false;

            default:
                return null;
        }
    }
}
