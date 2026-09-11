using System;

namespace RuleWright.Core;

/// <summary>
/// A leaf condition comparing a left-hand side against a constant value, or delegating to a
/// registered custom function. The left-hand side is usually a fact field (a dotted path such
/// as <c>Customer.Age</c>), but can also be a computed <see cref="Left"/> value expression
/// (such as <c>Order.Total * 0.9</c>).
/// </summary>
public sealed class ConditionLeaf : ConditionNode
{
    /// <summary>
    /// Creates a leaf condition whose left-hand side is a fact field (or a custom function).
    /// </summary>
    /// <param name="field">
    /// Dotted path resolved against the fact. Required for every operator except
    /// <see cref="ConditionOperator.Custom"/>, where it is optional (a custom function
    /// without a field receives the whole fact).
    /// </param>
    /// <param name="operator">The comparison operator.</param>
    /// <param name="value">
    /// The comparison operand: a CLR scalar (<see cref="string"/>, <see cref="bool"/>,
    /// <see cref="long"/>, <see cref="decimal"/>, <see cref="double"/>), null, or an
    /// object array for <see cref="ConditionOperator.In"/>/<see cref="ConditionOperator.NotIn"/>.
    /// </param>
    /// <param name="functionName">
    /// Registered function name; required when <paramref name="operator"/> is
    /// <see cref="ConditionOperator.Custom"/>.
    /// </param>
    /// <exception cref="ArgumentException">
    /// A custom leaf has no function name, or a non-custom leaf has no field.
    /// </exception>
    public ConditionLeaf(string? field, ConditionOperator @operator, object? value, string? functionName = null)
    {
        if (@operator == ConditionOperator.Custom)
        {
            if (string.IsNullOrEmpty(functionName))
            {
                throw new ArgumentException("A custom condition requires a function name.", nameof(functionName));
            }
        }
        else if (IsQuantifier(@operator))
        {
            throw new ArgumentException(
                $"Operator {@operator} compares elements of a collection, so it takes a per-element "
                + "condition rather than a value. Use the ConditionLeaf(string, ConditionOperator, ConditionNode) constructor.",
                nameof(@operator));
        }
        else if (string.IsNullOrEmpty(field))
        {
            throw new ArgumentException("A non-custom condition requires a field path.", nameof(field));
        }

        Field = field;
        Operator = @operator;
        Value = value;
        FunctionName = functionName;
    }

    /// <summary>
    /// Creates a leaf condition whose left-hand side is a computed value expression.
    /// </summary>
    /// <param name="left">The expression whose value is compared against <paramref name="value"/>.</param>
    /// <param name="operator">The comparison operator (not <see cref="ConditionOperator.Custom"/>).</param>
    /// <param name="value">The comparison operand (see the field constructor).</param>
    /// <exception cref="ArgumentNullException"><paramref name="left"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="operator"/> is <see cref="ConditionOperator.Custom"/>.</exception>
    public ConditionLeaf(ValueExpression left, ConditionOperator @operator, object? value)
    {
        if (left is null)
        {
            throw new ArgumentNullException(nameof(left));
        }

        if (@operator == ConditionOperator.Custom)
        {
            throw new ArgumentException("A custom condition uses a field, not an expression.", nameof(@operator));
        }

        if (IsQuantifier(@operator))
        {
            throw new ArgumentException(
                $"Operator {@operator} reads a collection from a field path, not from an expression.",
                nameof(@operator));
        }

        Left = left;
        Operator = @operator;
        Value = value;
    }

    /// <summary>
    /// Creates a collection quantifier: <paramref name="field"/> names a collection and
    /// <paramref name="elementCondition"/> is evaluated once per element.
    /// </summary>
    /// <param name="field">Dotted path to the collection.</param>
    /// <param name="operator">
    /// <see cref="ConditionOperator.Any"/>, <see cref="ConditionOperator.All"/>, or
    /// <see cref="ConditionOperator.None"/>.
    /// </param>
    /// <param name="elementCondition">
    /// The condition applied to each element. Its field paths resolve against the <em>element</em>,
    /// and the path <c>"$"</c> means the element itself — which is how a collection of scalars is
    /// tested.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="field"/> is null or empty, or <paramref name="operator"/> is not a quantifier.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="elementCondition"/> is null.</exception>
    /// <remarks>
    /// A factory rather than a constructor overload: a third <c>(string, ConditionOperator, …)</c>
    /// constructor would capture <c>new ConditionLeaf(field, op, null)</c>, which today means a
    /// null <em>value</em>, and silently change what existing code compiles to.
    /// </remarks>
    public static ConditionLeaf Quantifier(string field, ConditionOperator @operator, ConditionNode elementCondition)
        => new ConditionLeaf(field, @operator, elementCondition, quantifier: true);

    private ConditionLeaf(string field, ConditionOperator @operator, ConditionNode elementCondition, bool quantifier)
    {
        if (!IsQuantifier(@operator))
        {
            throw new ArgumentException(
                $"Operator {@operator} takes a value, not a per-element condition.",
                nameof(@operator));
        }

        if (string.IsNullOrEmpty(field))
        {
            throw new ArgumentException("A collection quantifier requires a field path.", nameof(field));
        }

        Field = field;
        Operator = @operator;
        ElementCondition = elementCondition ?? throw new ArgumentNullException(nameof(elementCondition));
    }

    /// <summary>The path meaning "the element currently being tested" inside a quantifier's condition.</summary>
    public const string ElementSelfPath = "$";

    /// <summary>Whether an operator quantifies over the elements of a collection.</summary>
    public static bool IsQuantifier(ConditionOperator @operator)
        => @operator is ConditionOperator.Any or ConditionOperator.All or ConditionOperator.None;

    /// <summary>Dotted field path, or null for a computed (<see cref="Left"/>) or field-less custom condition.</summary>
    public string? Field { get; }

    /// <summary>
    /// The computed left-hand side, or null when the left-hand side is the <see cref="Field"/>
    /// path (or the whole fact for a field-less custom condition).
    /// </summary>
    public ValueExpression? Left { get; }

    /// <summary>The comparison operator.</summary>
    public ConditionOperator Operator { get; }

    /// <summary>The constant comparison operand (null for IsNull/IsNotNull and field-only custom calls).</summary>
    public object? Value { get; }

    /// <summary>The registered custom function name when <see cref="Operator"/> is <see cref="ConditionOperator.Custom"/>.</summary>
    public string? FunctionName { get; }

    /// <summary>
    /// The per-element condition when <see cref="Operator"/> is a quantifier
    /// (<see cref="ConditionOperator.Any"/>/<see cref="ConditionOperator.All"/>/<see cref="ConditionOperator.None"/>),
    /// otherwise null. In JSON this is the leaf's <c>condition</c> key. Field paths inside it
    /// resolve against the element, with <see cref="ElementSelfPath"/> naming the element itself.
    /// </summary>
    public ConditionNode? ElementCondition { get; }
}
