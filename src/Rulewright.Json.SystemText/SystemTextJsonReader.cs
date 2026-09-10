using System;
using System.Collections.Generic;
using System.Text.Json;
using Rulewright.Serialization;

namespace Rulewright.Json.SystemText;

/// <summary>
/// <see cref="IRuleJsonReader"/> adapter backed by System.Text.Json. Comments and
/// trailing commas are tolerated so hand-written rule files stay pleasant to edit.
/// </summary>
public sealed class SystemTextJsonReader : IRuleJsonReader
{
    private static readonly JsonDocumentOptions DocumentOptions = new JsonDocumentOptions
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <inheritdoc />
    public RuleJsonValue Read(string json)
    {
        if (json is null)
        {
            throw new ArgumentNullException(nameof(json));
        }

        try
        {
            using (JsonDocument document = JsonDocument.Parse(json, DocumentOptions))
            {
                return Convert(document.RootElement);
            }
        }
        catch (JsonException ex)
        {
            throw new RuleParseException($"Invalid JSON: {ex.Message}", ex);
        }
        catch (ArgumentException ex)
        {
            // A syntactically valid token the neutral DOM refuses (an overflowing number, say) is
            // still a bad document, so it surfaces as the documented parse failure rather than as a
            // raw ArgumentException from an internal factory.
            throw new RuleParseException($"Invalid JSON: {ex.Message}", ex);
        }
    }

    private static RuleJsonValue Convert(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var properties = new List<KeyValuePair<string, RuleJsonValue>>();
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    properties.Add(new KeyValuePair<string, RuleJsonValue>(property.Name, Convert(property.Value)));
                }

                return RuleJsonValue.CreateObject(properties);

            case JsonValueKind.Array:
                var items = new List<RuleJsonValue>(element.GetArrayLength());
                foreach (JsonElement item in element.EnumerateArray())
                {
                    items.Add(Convert(item));
                }

                return RuleJsonValue.CreateArray(items);

            case JsonValueKind.String:
                return RuleJsonValue.CreateString(element.GetString()!);

            case JsonValueKind.Number:
                return RuleJsonValue.CreateNumber(element.GetRawText());

            case JsonValueKind.True:
                return RuleJsonValue.True;

            case JsonValueKind.False:
                return RuleJsonValue.False;

            default:
                return RuleJsonValue.Null;
        }
    }
}
