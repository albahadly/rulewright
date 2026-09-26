using System;
using System.Collections.Generic;
using RuleWright.Core;

namespace RuleWright.Execution;

/// <summary>
/// Evaluates a computed-action value expression against a dictionary fact by walking the
/// expression tree directly. Mirrors the compiled path exactly: field reads use the same
/// <see cref="RuleInterpreter.ResolvePath"/> resolution, operators the same
/// <see cref="ValueExpressionOps"/> semantics, and <c>call</c> nodes the same registered
/// <see cref="IRuleValueFunction"/> instances, so both paths agree by construction. How the
/// resulting value combines into the outputs is the shared concern of
/// <see cref="OutputApplier"/>.
/// </summary>
internal static class ActionExpressionInterpreter
{
    internal static object? EvaluateValue(ValueExpression expression, object fact, EvaluationServices services)
    {
        switch (expression)
        {
            case LiteralExpression literal:
                return literal.Value;

            case FieldExpression field:
                return RuleInterpreter.ResolvePath(fact, field.Path);

            case OperatorExpression op:
                return EvaluateOperator(op, fact, services);

            case CallExpression call:
                return services.ValueFunctions[call.Name].Invoke(EvaluateAll(call.Operands, fact, services));

            default:
                return null;
        }
    }

    private static object? EvaluateOperator(OperatorExpression op, object fact, EvaluationServices services)
    {
        IReadOnlyList<ValueExpression> operands = op.Operands;
        switch (op.Operator)
        {
            case ExpressionOperator.Add:
                return Fold(operands, fact, services, ValueExpressionOps.Add);

            case ExpressionOperator.Multiply:
                return Fold(operands, fact, services, ValueExpressionOps.Multiply);

            case ExpressionOperator.Subtract:
                return ValueExpressionOps.Subtract(
                    EvaluateValue(operands[0], fact, services), EvaluateValue(operands[1], fact, services));

            case ExpressionOperator.Divide:
                return ValueExpressionOps.Divide(
                    EvaluateValue(operands[0], fact, services), EvaluateValue(operands[1], fact, services));

            case ExpressionOperator.Modulo:
                return ValueExpressionOps.Modulo(
                    EvaluateValue(operands[0], fact, services), EvaluateValue(operands[1], fact, services));

            case ExpressionOperator.Negate:
                return ValueExpressionOps.Negate(EvaluateValue(operands[0], fact, services));

            case ExpressionOperator.Count:
                return ValueExpressionOps.Count(EvaluateValue(operands[0], fact, services));

            case ExpressionOperator.Concat:
                return ValueExpressionOps.Concat(EvaluateAll(operands, fact, services));

            default: // Coalesce
                return ValueExpressionOps.Coalesce(EvaluateAll(operands, fact, services));
        }
    }

    private static object? Fold(
        IReadOnlyList<ValueExpression> operands,
        object fact,
        EvaluationServices services,
        Func<object?, object?, object?> op)
    {
        object? accumulator = EvaluateValue(operands[0], fact, services);
        for (int i = 1; i < operands.Count; i++)
        {
            accumulator = op(accumulator, EvaluateValue(operands[i], fact, services));
        }

        return accumulator;
    }

    private static object?[] EvaluateAll(IReadOnlyList<ValueExpression> operands, object fact, EvaluationServices services)
    {
        var values = new object?[operands.Count];
        for (int i = 0; i < operands.Count; i++)
        {
            values[i] = EvaluateValue(operands[i], fact, services);
        }

        return values;
    }
}
