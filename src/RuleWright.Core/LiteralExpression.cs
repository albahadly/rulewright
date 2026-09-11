namespace RuleWright.Core;

/// <summary>
/// A value expression that evaluates to a constant. In JSON, a bare scalar
/// (<c>10</c>, <c>"gold"</c>, <c>true</c>, <c>null</c>) or an explicit
/// <c>{ "literal": &lt;scalar&gt; }</c> node.
/// </summary>
public sealed class LiteralExpression : ValueExpression
{
    /// <summary>
    /// Creates a literal value expression.
    /// </summary>
    /// <param name="value">
    /// The constant: a CLR scalar (<see cref="string"/>, <see cref="bool"/>,
    /// <see cref="long"/>, <see cref="decimal"/>, <see cref="double"/>) or null.
    /// </param>
    public LiteralExpression(object? value)
    {
        Value = value;
    }

    /// <summary>The constant value this expression evaluates to.</summary>
    public object? Value { get; }
}
