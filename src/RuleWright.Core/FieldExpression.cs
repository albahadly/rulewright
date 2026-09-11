using System;

namespace RuleWright.Core;

/// <summary>
/// A value expression that reads a fact field via a dotted path (<c>Order.Total</c>),
/// using the same resolution and null semantics as a <see cref="ConditionLeaf"/> field:
/// a null anywhere along the path yields null. In JSON, <c>{ "field": "Order.Total" }</c>.
/// </summary>
public sealed class FieldExpression : ValueExpression
{
    /// <summary>
    /// Creates a field-read value expression.
    /// </summary>
    /// <param name="path">The dotted field path resolved against the fact.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null or empty.</exception>
    public FieldExpression(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            throw new ArgumentException("Field path must not be null or empty.", nameof(path));
        }

        Path = path;
    }

    /// <summary>The dotted field path resolved against the fact.</summary>
    public string Path { get; }
}
