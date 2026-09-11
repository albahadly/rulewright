using RuleWright.Core;

namespace RuleWright.Serialization;

/// <summary>
/// Discovery metadata for one logical group combinator: its JSON spelling, domain enum, and
/// how many child conditions it accepts. Enumerated from
/// <see cref="RuleSchemaCatalog.LogicalOperators"/>.
/// </summary>
public sealed class LogicalOperatorInfo
{
    internal LogicalOperatorInfo(LogicalOperator @operator, string jsonName, int minChildren, int? maxChildren)
    {
        Operator = @operator;
        JsonName = jsonName;
        MinChildren = minChildren;
        MaxChildren = maxChildren;
    }

    /// <summary>The domain enum value.</summary>
    public LogicalOperator Operator { get; }

    /// <summary>The JSON schema spelling used in a group's <c>operator</c> (<c>"AND"</c>/<c>"OR"</c>/<c>"NOT"</c>).</summary>
    public string JsonName { get; }

    /// <summary>The minimum number of child conditions the group requires.</summary>
    public int MinChildren { get; }

    /// <summary>The maximum number of child conditions, or null when unbounded (<c>AND</c>/<c>OR</c>).</summary>
    public int? MaxChildren { get; }
}
