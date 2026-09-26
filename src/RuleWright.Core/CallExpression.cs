using System;
using System.Collections.Generic;
using System.Linq;

namespace RuleWright.Core;

/// <summary>
/// A value expression that invokes a registered value function. In JSON,
/// <c>{ "call": "RoundTo", "operands": [ { "field": "Order.Total" }, 2 ] }</c>.
/// The function itself is C# registered on the engine builder
/// (<c>RuleWrightBuilder.RegisterValueFunction</c>), so the rule document stays pure data:
/// it names the function, it never embeds code. An unregistered name fails at
/// <c>LoadRuleSet</c>, exactly as an unregistered <c>custom</c> condition function does.
/// </summary>
public sealed class CallExpression : ValueExpression
{
    /// <summary>
    /// Creates a call expression.
    /// </summary>
    /// <param name="name">The case-sensitive registered function name.</param>
    /// <param name="operands">The argument expressions, in document order; may be empty.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null or empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="operands"/> contains null.</exception>
    public CallExpression(string name, IEnumerable<ValueExpression>? operands = null)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("Call expression function name must not be null or empty.", nameof(name));
        }

        ValueExpression[] materialized = operands?.ToArray() ?? Array.Empty<ValueExpression>();
        if (materialized.Any(operand => operand is null))
        {
            throw new ArgumentNullException(nameof(operands), "Operands must not contain null.");
        }

        Name = name;
        Operands = materialized;
    }

    /// <summary>The case-sensitive registered function name.</summary>
    public string Name { get; }

    /// <summary>The argument expressions, in document order; empty for a no-argument call.</summary>
    public IReadOnlyList<ValueExpression> Operands { get; }
}
