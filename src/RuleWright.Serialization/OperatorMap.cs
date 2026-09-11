using System;
using System.Collections.Generic;
using RuleWright.Core;

namespace RuleWright.Serialization;

/// <summary>
/// Maps between the JSON schema's operator spellings (<c>"Equals"</c>, <c>"custom"</c>, …)
/// and the <see cref="ConditionOperator"/> domain enum.
/// </summary>
internal static class OperatorMap
{
    private static readonly Dictionary<string, ConditionOperator> FromJson =
        new Dictionary<string, ConditionOperator>(StringComparer.Ordinal)
        {
            ["Equals"] = ConditionOperator.Equal,
            ["NotEquals"] = ConditionOperator.NotEqual,
            ["GreaterThan"] = ConditionOperator.GreaterThan,
            ["GreaterThanOrEqual"] = ConditionOperator.GreaterThanOrEqual,
            ["LessThan"] = ConditionOperator.LessThan,
            ["LessThanOrEqual"] = ConditionOperator.LessThanOrEqual,
            ["Contains"] = ConditionOperator.Contains,
            ["StartsWith"] = ConditionOperator.StartsWith,
            ["EndsWith"] = ConditionOperator.EndsWith,
            ["MatchesRegex"] = ConditionOperator.MatchesRegex,
            ["In"] = ConditionOperator.In,
            ["NotIn"] = ConditionOperator.NotIn,
            ["IsNull"] = ConditionOperator.IsNull,
            ["IsNotNull"] = ConditionOperator.IsNotNull,
            ["custom"] = ConditionOperator.Custom,
            ["Any"] = ConditionOperator.Any,
            ["All"] = ConditionOperator.All,
            ["None"] = ConditionOperator.None,
        };

    private static readonly Dictionary<ConditionOperator, string> ToJson = BuildReverse();

    internal static bool TryParse(string name, out ConditionOperator @operator)
        => FromJson.TryGetValue(name, out @operator);

    internal static string ToJsonName(ConditionOperator @operator) => ToJson[@operator];

    internal static IEnumerable<string> JsonNames => FromJson.Keys;

    private static Dictionary<ConditionOperator, string> BuildReverse()
    {
        var reverse = new Dictionary<ConditionOperator, string>();
        foreach (KeyValuePair<string, ConditionOperator> pair in FromJson)
        {
            reverse[pair.Value] = pair.Key;
        }

        return reverse;
    }
}
