using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rulewright.Serialization;

namespace Rulewright.Json.NewtonsoftJson;

/// <summary>
/// <see cref="IRuleJsonReader"/> adapter backed by Newtonsoft.Json. Comments and trailing commas
/// are tolerated so hand-written rule files stay pleasant to edit, and automatic date parsing is
/// disabled so date-like strings stay strings — matching the System.Text.Json adapter exactly.
/// </summary>
public sealed class NewtonsoftJsonReader : IRuleJsonReader
{
    private static readonly JsonLoadSettings LoadSettings = new JsonLoadSettings
    {
        CommentHandling = CommentHandling.Ignore,
        LineInfoHandling = LineInfoHandling.Ignore,
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
            using (var stringReader = new StringReader(json))
            using (var jsonReader = new JsonTextReader(stringReader)
            {
                // Keep numbers as double (not decimal) and never coerce date-like strings — both
                // are required for parity with the System.Text.Json adapter.
                DateParseHandling = DateParseHandling.None,
                FloatParseHandling = FloatParseHandling.Double,
            })
            {
                JToken token = JToken.ReadFrom(jsonReader, LoadSettings);

                // Reject trailing content after the root document (e.g. "{} garbage"), as
                // System.Text.Json's JsonDocument.Parse does.
                while (jsonReader.Read())
                {
                    if (jsonReader.TokenType != JsonToken.Comment)
                    {
                        throw new JsonReaderException("Additional text found after the JSON document.");
                    }
                }

                return Convert(token);
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

    private static RuleJsonValue Convert(JToken token)
    {
        switch (token.Type)
        {
            case JTokenType.Object:
                var properties = new List<KeyValuePair<string, RuleJsonValue>>();
                foreach (JProperty property in ((JObject)token).Properties())
                {
                    properties.Add(new KeyValuePair<string, RuleJsonValue>(property.Name, Convert(property.Value)));
                }

                return RuleJsonValue.CreateObject(properties);

            case JTokenType.Array:
                var items = new List<RuleJsonValue>();
                foreach (JToken item in (JArray)token)
                {
                    items.Add(Convert(item));
                }

                return RuleJsonValue.CreateArray(items);

            case JTokenType.String:
                return RuleJsonValue.CreateString((string)((JValue)token).Value!);

            case JTokenType.Integer:
            case JTokenType.Float:
                return RuleJsonValue.CreateNumber(NewtonsoftJsonNumber.ToRawText((JValue)token));

            case JTokenType.Boolean:
                return RuleJsonValue.CreateBoolean((bool)((JValue)token).Value!);

            default:
                // Null, Undefined, and None all map to JSON null; no other token types arise from
                // parsing JSON text with date parsing disabled.
                return RuleJsonValue.Null;
        }
    }
}
