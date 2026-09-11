namespace RuleWright.Core;

/// <summary>
/// A node in a computed action's value expression tree: a <see cref="LiteralExpression"/>
/// (a constant), a <see cref="FieldExpression"/> (a dotted fact path read), or an
/// <see cref="OperatorExpression"/> (an operator over sub-expressions). Immutable after
/// construction. Value expressions are pure data — a closed operator vocabulary, never
/// embedded code — mirroring the condition tree.
/// </summary>
public abstract class ValueExpression
{
    private protected ValueExpression()
    {
    }
}
