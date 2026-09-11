using RuleWright.Core;

namespace RuleWright.Serialization;

/// <summary>Broad grouping of a computed-value <see cref="ExpressionOperator"/>, for UI palettes.</summary>
public enum ExpressionOperatorCategory
{
    /// <summary>Numeric arithmetic (<c>add</c>, <c>subtract</c>, <c>multiply</c>, <c>divide</c>, <c>modulo</c>, <c>negate</c>).</summary>
    Arithmetic,

    /// <summary>String building (<c>concat</c>).</summary>
    Text,

    /// <summary>Null handling (<c>coalesce</c>).</summary>
    NullHandling,

    /// <summary>Collections (<c>count</c>).</summary>
    Collection,
}

/// <summary>
/// Discovery metadata for one computed-value expression operator: its JSON spelling, domain
/// enum, operand arity, and category. Enumerated from
/// <see cref="RuleSchemaCatalog.ExpressionOperators"/>.
/// </summary>
public sealed class ExpressionOperatorInfo
{
    internal ExpressionOperatorInfo(
        ExpressionOperator @operator,
        string jsonName,
        int minOperands,
        int? maxOperands,
        ExpressionOperatorCategory category)
    {
        Operator = @operator;
        JsonName = jsonName;
        MinOperands = minOperands;
        MaxOperands = maxOperands;
        Category = category;
    }

    /// <summary>The domain enum value.</summary>
    public ExpressionOperator Operator { get; }

    /// <summary>The JSON <c>op</c> spelling used in expression nodes (e.g. <c>"add"</c>).</summary>
    public string JsonName { get; }

    /// <summary>The minimum number of operands the operator requires.</summary>
    public int MinOperands { get; }

    /// <summary>The maximum number of operands, or null when unbounded (<c>add</c>/<c>multiply</c>/<c>concat</c>/<c>coalesce</c>).</summary>
    public int? MaxOperands { get; }

    /// <summary>The operator's broad category.</summary>
    public ExpressionOperatorCategory Category { get; }
}
