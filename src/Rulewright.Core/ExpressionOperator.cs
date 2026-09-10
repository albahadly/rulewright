namespace Rulewright.Core;

/// <summary>
/// The closed vocabulary of operators available inside a computed action
/// <see cref="OperatorExpression"/>. Deliberately small and pure-data — Rulewright never
/// embeds arbitrary code strings, so a UI can safely generate these and a reviewer can
/// safely diff them.
/// </summary>
public enum ExpressionOperator
{
    /// <summary>Numeric sum of two or more operands.</summary>
    Add,

    /// <summary>Numeric difference of exactly two operands (left minus right).</summary>
    Subtract,

    /// <summary>Numeric product of two or more operands.</summary>
    Multiply,

    /// <summary>Numeric quotient of exactly two operands; never integer-truncated. Division by zero yields null.</summary>
    Divide,

    /// <summary>Numeric remainder of exactly two operands. A zero divisor yields null.</summary>
    Modulo,

    /// <summary>Arithmetic negation of exactly one operand.</summary>
    Negate,

    /// <summary>String concatenation of two or more operands (each rendered invariantly).</summary>
    Concat,

    /// <summary>The first non-null operand, or null if all operands are null.</summary>
    Coalesce,

    /// <summary>
    /// The number of elements in a single collection operand, as a <see cref="long"/>. Null when
    /// the operand is null or is not a collection; a string is text, not a collection of
    /// characters.
    /// </summary>
    Count,
}
