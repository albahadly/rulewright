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
    /// <see cref="double"/>.
    /// </summary>
    /// <param name="element">A JSON element of kind <see cref="JsonValueKind.Object"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="element"/> is not a JSON object.</exception>
    public static Dictionary<string, object?> ToDictionary(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException($"A dictionary fact requires a JSON object, not {element.ValueKind}.", nameof(element));
        }

        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            result[property.Name] = ToClrValue(property.Value);
        }

        return result;
    }

    private static object? ToClrValue(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                return ToDictionary(element);

            case JsonValueKind.Array:
                var items = new object?[element.GetArrayLength()];
                int index = 0;
                foreach (JsonElement item in element.EnumerateArray())
                {
                    items[index++] = ToClrValue(item);
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
