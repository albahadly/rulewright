using System;
using System.Collections.Generic;
using Rulewright.Core;

namespace Rulewright.Serialization;

/// <summary>
/// Maps between the JSON <c>op</c> spellings of computed-action operators
/// (<c>"add"</c>, <c>"concat"</c>, …) and the <see cref="ExpressionOperator"/> domain enum,
/// and carries each operator's operand arity for structural validation.
/// </summary>
internal static class ExpressionOperatorMap
{
    private static readonly Dictionary<string, ExpressionOperator> FromJson =
        new Dictionary<string, ExpressionOperator>(StringComparer.Ordinal)
        {
            ["add"] = ExpressionOperator.Add,
            ["subtract"] = ExpressionOperator.Subtract,
            ["multiply"] = ExpressionOperator.Multiply,
            ["divide"] = ExpressionOperator.Divide,
            ["modulo"] = ExpressionOperator.Modulo,
            ["negate"] = ExpressionOperator.Negate,
            ["concat"] = ExpressionOperator.Concat,
            ["coalesce"] = ExpressionOperator.Coalesce,
            ["count"] = ExpressionOperator.Count,
        };

    private static readonly Dictionary<ExpressionOperator, string> ToJson = BuildReverse();

    internal static bool TryParse(string name, out ExpressionOperator @operator)
        => FromJson.TryGetValue(name, out @operator);

    internal static string ToJsonName(ExpressionOperator @operator) => ToJson[@operator];

    internal static IEnumerable<string> JsonNames => FromJson.Keys;

    /// <summary>
    /// The exact operand count an operator requires, or null when it accepts any count of
    /// two or more (<see cref="ExpressionOperator.Add"/>, <see cref="ExpressionOperator.Multiply"/>,
    /// <see cref="ExpressionOperator.Concat"/>, <see cref="ExpressionOperator.Coalesce"/>).
    /// </summary>
    internal static int? RequiredArity(ExpressionOperator @operator) => @operator switch
    {
        ExpressionOperator.Negate => 1,
        ExpressionOperator.Count => 1,
        ExpressionOperator.Subtract => 2,
        ExpressionOperator.Divide => 2,
        ExpressionOperator.Modulo => 2,
        _ => null,
    };

    private static Dictionary<ExpressionOperator, string> BuildReverse()
    {
        var reverse = new Dictionary<ExpressionOperator, string>();
        foreach (KeyValuePair<string, ExpressionOperator> pair in FromJson)
        {
            reverse[pair.Value] = pair.Key;
        }

        return reverse;
    }
}
