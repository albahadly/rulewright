using System;
using System.Globalization;
using System.Linq;
using Rulewright.Core;

namespace Rulewright.Execution;

/// <summary>
/// Renders human-readable descriptions of condition nodes for execution traces,
/// e.g. <c>"Customer.Age GreaterThan 18"</c> or <c>"AND"</c>.
/// </summary>
internal static class ConditionDescriber
{
    internal static string Describe(ConditionNode node)
    {
        if (node is ConditionGroup group)
        {
            return group.Operator switch
            {
                LogicalOperator.And => "AND",
                LogicalOperator.Or => "OR",
                _ => "NOT",
            };
        }

        var leaf = (ConditionLeaf)node;

        // A computed left-hand side renders as the expression itself: "(fact)" is reserved for a
        // field-less custom call, where the whole fact really is the operand.
        string field = leaf.Left is not null ? Describe(leaf.Left) : leaf.Field ?? "(fact)";
        return leaf.Operator switch
        {
            ConditionOperator.Custom => $"{field} custom:{leaf.FunctionName}",
            ConditionOperator.IsNull => $"{field} IsNull",
            ConditionOperator.IsNotNull => $"{field} IsNotNull",
            _ => $"{field} {OperatorName(leaf.Operator)} {Literal(leaf.Value)}",
        };
    }

    /// <summary>
    /// Renders a value expression in the same shape the JSON reads, so a trace of a computed
    /// left-hand side says what was computed rather than naming the fact.
    /// </summary>
    internal static string Describe(ValueExpression expression) => expression switch
    {
        LiteralExpression literal => Literal(literal.Value),
        FieldExpression field => field.Path,
        OperatorExpression op => ExpressionOperatorName(op.Operator)
            + "(" + string.Join(", ", op.Operands.Select(Describe)) + ")",
        _ => "(expression)",
    };

    private static string ExpressionOperatorName(ExpressionOperator @operator) => @operator switch
    {
        ExpressionOperator.Add => "add",
        ExpressionOperator.Subtract => "subtract",
        ExpressionOperator.Multiply => "multiply",
        ExpressionOperator.Divide => "divide",
        ExpressionOperator.Modulo => "modulo",
        ExpressionOperator.Negate => "negate",
        ExpressionOperator.Concat => "concat",
        _ => "coalesce",
    };

    private static string OperatorName(ConditionOperator @operator) => @operator switch
    {
        ConditionOperator.Equal => "Equals",
        ConditionOperator.NotEqual => "NotEquals",
        ConditionOperator.GreaterThan => "GreaterThan",
        ConditionOperator.GreaterThanOrEqual => "GreaterThanOrEqual",
        ConditionOperator.LessThan => "LessThan",
        ConditionOperator.LessThanOrEqual => "LessThanOrEqual",
        ConditionOperator.Contains => "Contains",
        ConditionOperator.StartsWith => "StartsWith",
        ConditionOperator.EndsWith => "EndsWith",
        ConditionOperator.MatchesRegex => "MatchesRegex",
        ConditionOperator.In => "In",
        _ => "NotIn",
    };

    private static string Literal(object? value) => value switch
    {
        null => "null",
        string s => "\"" + s + "\"",
        bool b => b ? "true" : "false",
        object?[] array => "[" + string.Join(", ", array.Select(Literal)) + "]",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "null",
    };
}
