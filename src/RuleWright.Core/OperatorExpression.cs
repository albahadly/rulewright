using System;
using System.Collections.Generic;
using System.Linq;

namespace RuleWright.Core;

/// <summary>
/// A value expression that applies an <see cref="ExpressionOperator"/> to one or more
/// operand expressions. In JSON,
/// <c>{ "op": "multiply", "operands": [ { "field": "Order.Total" }, 0.1 ] }</c>.
/// Operand order is significant for non-commutative operators
/// (<see cref="ExpressionOperator.Subtract"/>, <see cref="ExpressionOperator.Divide"/>,
/// <see cref="ExpressionOperator.Modulo"/>).
/// </summary>
public sealed class OperatorExpression : ValueExpression
{
    /// <summary>
    /// Creates an operator value expression.
    /// </summary>
    /// <param name="operator">The operator to apply.</param>
    /// <param name="operands">The operand expressions, in document order.</param>
    /// <exception cref="ArgumentNullException"><paramref name="operands"/> is null or contains null.</exception>
    /// <exception cref="ArgumentException"><paramref name="operands"/> is empty.</exception>
    public OperatorExpression(ExpressionOperator @operator, IEnumerable<ValueExpression> operands)
    {
        if (operands is null)
        {
            throw new ArgumentNullException(nameof(operands));
        }

        ValueExpression[] materialized = operands.ToArray();
        if (materialized.Length == 0)
        {
            throw new ArgumentException("An operator expression requires at least one operand.", nameof(operands));
        }

        if (materialized.Any(operand => operand is null))
        {
            throw new ArgumentNullException(nameof(operands), "Operands must not contain null.");
        }

        Operator = @operator;
        Operands = materialized;
    }

    /// <summary>The operator applied to <see cref="Operands"/>.</summary>
    public ExpressionOperator Operator { get; }

    /// <summary>The operand expressions, in document order.</summary>
    public IReadOnlyList<ValueExpression> Operands { get; }
}
