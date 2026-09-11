namespace RuleWright.Core;

/// <summary>
/// A custom condition function referenced from JSON as
/// <c>{ "operator": "custom", "name": "..." }</c>. Implementations are registered
/// with the engine builder and bound into compiled rules at compile time — no
/// name lookup or reflection happens per evaluation.
/// </summary>
/// <remarks>
/// Implementations must be thread-safe: a single instance is shared across all
/// concurrent evaluations of every rule that references it.
/// </remarks>
public interface IRuleFunction
{
    /// <summary>The name the function is referenced by in rule JSON. Case-sensitive.</summary>
    string Name { get; }

    /// <summary>
    /// Evaluates the condition.
    /// </summary>
    /// <param name="fieldValue">
    /// The resolved (boxed) field value when the leaf specifies a <c>field</c>;
    /// otherwise the whole fact object.
    /// </param>
    /// <param name="value">The leaf's constant <c>value</c> operand, or null if absent.</param>
    /// <returns>Whether the condition passes.</returns>
    bool Evaluate(object? fieldValue, object? value);
}
