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
    /// <see cref="double"/>.
    /// </summary>
    /// <param name="token">A JSON token of kind <see cref="JTokenType.Object"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="token"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="token"/> is not a JSON object.</exception>
    public static Dictionary<string, object?> ToDictionary(JToken token)
    {
        if (token is null)
        {
            throw new ArgumentNullException(nameof(token));
        }

        if (token is not JObject obj)
        {
            throw new ArgumentException($"A dictionary fact requires a JSON object, not {token.Type}.", nameof(token));
        }

        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (JProperty property in obj.Properties())
        {
            result[property.Name] = ToClrValue(property.Value);
        }

        return result;
    }

    private static object? ToClrValue(JToken token)
    {
        switch (token.Type)
        {
            case JTokenType.Object:
                return ToDictionary(token);

            case JTokenType.Array:
                var array = (JArray)token;
                var items = new object?[array.Count];
                for (int i = 0; i < array.Count; i++)
                {
                    items[i] = ToClrValue(array[i]);
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
