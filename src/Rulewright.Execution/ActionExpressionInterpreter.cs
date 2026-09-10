using System;
using System.Collections.Generic;
using Rulewright.Core;

namespace Rulewright.Execution;

/// <summary>
/// Evaluates a computed-action value expression against a dictionary fact by walking the
/// expression tree directly. Mirrors the compiled path exactly: field reads use the same
/// <see cref="RuleInterpreter.ResolvePath"/> resolution and operators the same
/// <see cref="ValueExpressionOps"/> semantics, so both paths agree by construction. How the
/// resulting value combines into the outputs is the shared concern of
/// <see cref="OutputApplier"/>.
/// </summary>
internal static class ActionExpressionInterpreter
{
    internal static object? EvaluateValue(ValueExpression expression, object fact)
    {
        switch (expression)
        {
            case LiteralExpression literal:
                return literal.Value;

            case FieldExpression field:
                return RuleInterpreter.ResolvePath(fact, field.Path);

            case OperatorExpression op:
                return EvaluateOperator(op, fact);

            default:
                return null;
        }
    }

    private static object? EvaluateOperator(OperatorExpression op, object fact)
    {
        IReadOnlyList<ValueExpression> operands = op.Operands;
        switch (op.Operator)
        {
            case ExpressionOperator.Add:
                return Fold(operands, fact, ValueExpressionOps.Add);

            case ExpressionOperator.Multiply:
                return Fold(operands, fact, ValueExpressionOps.Multiply);

            case ExpressionOperator.Subtract:
                return ValueExpressionOps.Subtract(EvaluateValue(operands[0], fact), EvaluateValue(operands[1], fact));

            case ExpressionOperator.Divide:
                return ValueExpressionOps.Divide(EvaluateValue(operands[0], fact), EvaluateValue(operands[1], fact));

            case ExpressionOperator.Modulo:
                return ValueExpressionOps.Modulo(EvaluateValue(operands[0], fact), EvaluateValue(operands[1], fact));

            case ExpressionOperator.Negate:
                return ValueExpressionOps.Negate(EvaluateValue(operands[0], fact));

            case ExpressionOperator.Count:
                return ValueExpressionOps.Count(EvaluateValue(operands[0], fact));

            case ExpressionOperator.Concat:
                return ValueExpressionOps.Concat(EvaluateAll(operands, fact));

            default: // Coalesce
                return ValueExpressionOps.Coalesce(EvaluateAll(operands, fact));
        }
    }

    private static object? Fold(IReadOnlyList<ValueExpression> operands, object fact, Func<object?, object?, object?> op)
    {
        object? accumulator = EvaluateValue(operands[0], fact);
        for (int i = 1; i < operands.Count; i++)
        {
            accumulator = op(accumulator, EvaluateValue(operands[i], fact));
        }

        return accumulator;
    }

    private static object?[] EvaluateAll(IReadOnlyList<ValueExpression> operands, object fact)
    {
        var values = new object?[operands.Count];
        for (int i = 0; i < operands.Count; i++)
        {
            values[i] = EvaluateValue(operands[i], fact);
        }

        return values;
    }
}
