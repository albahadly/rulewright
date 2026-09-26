namespace RuleWright.Core;

/// <summary>
/// A custom value-returning function referenced from a value expression as
/// <c>{ "call": "...", "operands": [...] }</c>. Where an <see cref="IRuleFunction"/> answers a
/// condition (true/false), a value function computes a value usable anywhere an expression is:
/// an action's <c>value</c>, the left-hand side of a condition, or a decision-table cell.
/// Implementations are registered with the engine builder and bound into compiled rules at
/// compile time — no name lookup or reflection happens per evaluation.
/// </summary>
/// <remarks>
/// Implementations must be thread-safe (one instance serves all concurrent evaluations) and
/// should be <em>total</em>, like the built-in expression operators: return null for an
/// argument shape they don't understand rather than throwing.
/// </remarks>
public interface IRuleValueFunction
{
    /// <summary>The name the function is referenced by in rule JSON. Case-sensitive.</summary>
    string Name { get; }

    /// <summary>
    /// Computes the function's value.
    /// </summary>
    /// <param name="arguments">
    /// The evaluated operand values, in document order; empty for a no-argument call.
    /// </param>
    /// <returns>The computed value, or null.</returns>
    object? Invoke(object?[] arguments);
}
