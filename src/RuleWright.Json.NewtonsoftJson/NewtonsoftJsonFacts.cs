using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace RuleWright.Json.NewtonsoftJson;

/// <summary>
/// Helpers for using Newtonsoft.Json payloads as dynamic facts: converts a
/// <see cref="JToken"/> object into the <c>IDictionary&lt;string, object&gt;</c> shape the
/// engine's interpreter evaluates (reported as
/// <see cref="RuleWright.Core.CompilationMode.Interpreted"/>). Uses the same number policy as the
/// System.Text.Json adapter, so a JSON payload produces the same fact through either library.
/// </summary>
public static class NewtonsoftJsonFacts
{
    /// <summary>
    /// Recursively converts a JSON object into a dictionary fact. Nested objects become nested
    /// dictionaries; arrays become <c>object?[]</c>; numbers become <see cref="long"/> when
    /// integral, otherwise <see cref="decimal"/> when exactly representable, otherwise
    /// <see cref="double"/>. Keys match exactly (ordinal, case-sensitive); to match field paths
    /// without regard to case, use <see cref="ToDictionary(JToken, IEqualityComparer{string})"/>.
    /// </summary>
    /// <param name="token">A JSON token of kind <see cref="JTokenType.Object"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="token"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="token"/> is not a JSON object.</exception>
    public static Dictionary<string, object?> ToDictionary(JToken token)
        => ToDictionary(token, StringComparer.Ordinal);

    /// <summary>
    /// Recursively converts a JSON object into a dictionary fact whose keys, at every level, compare
    /// with <paramref name="keyComparer"/>. The interpreter looks each field-path segment up through
    /// the dictionary's own comparer, so with <see cref="StringComparer.OrdinalIgnoreCase"/> a rule
    /// reading <c>Order.Total</c> finds a camelCase payload's <c>order.total</c>, just as a typed
    /// fact's members already match. Values convert exactly as in <see cref="ToDictionary(JToken)"/>.
    /// </summary>
    /// <param name="token">A JSON token of kind <see cref="JTokenType.Object"/>.</param>
    /// <param name="keyComparer">How keys compare, such as <see cref="StringComparer.OrdinalIgnoreCase"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="token"/> or <paramref name="keyComparer"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="token"/> is not a JSON object, or one object has two differently spelled
    /// properties that <paramref name="keyComparer"/> treats as the same key (such as <c>Total</c> and
    /// <c>total</c> under a case-insensitive comparer). That is reported rather than letting one
    /// value silently replace the other.
    /// </exception>
    public static Dictionary<string, object?> ToDictionary(JToken token, IEqualityComparer<string> keyComparer)
    {
        if (token is null)
        {
            throw new ArgumentNullException(nameof(token));
        }

        if (keyComparer is null)
        {
            throw new ArgumentNullException(nameof(keyComparer));
        }

        if (token is not JObject obj)
        {
            throw new ArgumentException($"A dictionary fact requires a JSON object, not {token.Type}.", nameof(token));
        }

        var result = new Dictionary<string, object?>(keyComparer);
        var spelling = new Dictionary<string, string>(keyComparer);
        foreach (JProperty property in obj.Properties())
        {
            if (spelling.TryGetValue(property.Name, out string? first) && !string.Equals(first, property.Name, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Properties '{first}' and '{property.Name}' are the same key under the chosen key comparer, so one value would silently replace the other.",
                    nameof(token));
            }

            spelling[property.Name] = property.Name;
            result[property.Name] = ToClrValue(property.Value, keyComparer);
        }

        return result;
    }

    private static object? ToClrValue(JToken token, IEqualityComparer<string> keyComparer)
    {
        switch (token.Type)
        {
            case JTokenType.Object:
                return ToDictionary(token, keyComparer);

            case JTokenType.Array:
                var array = (JArray)token;
                var items = new object?[array.Count];
                for (int i = 0; i < array.Count; i++)
                {
                    items[i] = ToClrValue(array[i], keyComparer);
                }

                return items;

            case JTokenType.String:
                return (string)((JValue)token).Value!;

            case JTokenType.Integer:
            case JTokenType.Float:
                string raw = NewtonsoftJsonNumber.ToRawText((JValue)token);
                if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long integral))
                {
                    return integral;
                }

                if (decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal exact))
                {
                    return exact;
                }

                return double.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture);

            case JTokenType.Boolean:
                return (bool)((JValue)token).Value!;

            default:
                return null;
        }
    }
}
