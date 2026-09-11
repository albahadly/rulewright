using System;
using RuleWright.Core;

namespace RuleWright.Extensions.Functions;

/// <summary>
/// An <see cref="IRuleFunction"/> backed by a delegate — a name plus a predicate over the
/// resolved field value and the leaf's constant <c>value</c>. Immutable and therefore
/// thread-safe, as <see cref="IRuleFunction"/> requires. Use it to define ad-hoc custom
/// functions inline, or as the building block for a reusable catalog (see
/// <see cref="BuiltInFunctions"/>).
/// </summary>
public sealed class NamedRuleFunction : IRuleFunction, IRuleFunctionMetadata
{
    private readonly Func<object?, object?, bool> _predicate;

    /// <summary>
    /// Creates a named function from a predicate.
    /// </summary>
    /// <param name="name">The case-sensitive name used in rule JSON (<c>"custom"</c> leaves).</param>
    /// <param name="predicate">The predicate over the field value and the leaf's constant value.</param>
    /// <param name="description">
    /// An optional short description of what the function checks, surfaced via
    /// <see cref="IRuleFunctionMetadata"/> for rule-authoring UIs. Defaults to null.
    /// </param>
    /// <param name="valueKind">
    /// A hint about the shape of the <c>value</c> operand this function expects, surfaced via
    /// <see cref="IRuleFunctionMetadata"/>. Defaults to <see cref="RuleFunctionValueKind.Unspecified"/>.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null or empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is null.</exception>
    public NamedRuleFunction(
        string name,
        Func<object?, object?, bool> predicate,
        string? description = null,
        RuleFunctionValueKind valueKind = RuleFunctionValueKind.Unspecified)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("Function name must not be null or empty.", nameof(name));
        }

        Name = name;
        _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        Description = description;
        ValueKind = valueKind;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public bool Evaluate(object? fieldValue, object? value) => _predicate(fieldValue, value);

    /// <inheritdoc />
    public string? Description { get; }

    /// <inheritdoc />
    public RuleFunctionValueKind ValueKind { get; }
}
