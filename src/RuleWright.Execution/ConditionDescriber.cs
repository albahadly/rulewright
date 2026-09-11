using System;
using System.Globalization;
using System.Linq;
using RuleWright.Core;

namespace RuleWright.Execution;

/// <summary>
/// Renders human-readable descriptions of condition nodes for execution traces,
/// e.g. <c>"Customer.Age GreaterThan 18"</c> or <c>"AND"</c>.
/// </summary>
internal static class ConditionDescriber
{
    internal static string Describe(ConditionNode node) => Describe(node, nested: false);

    private static string Describe(ConditionNode node, bool nested)
    {
        if (node is ConditionGroup group)
        {
            string name = group.Operator switch
            {
                LogicalOperator.And => "AND",
                LogicalOperator.Or => "OR",
                _ => "NOT",
            };

            // At the top level a group is its own trace node with its children beneath it, so the
            // combinator alone is the whole description. Nested inside a quantifier there are no
            // child nodes to show, so spell the group out.
            return nested
                ? name + "(" + string.Join(", ", group.Children.Select(child => Describe(child, nested: true))) + ")"
                : name;
        }

        var leaf = (ConditionLeaf)node;

        // A computed left-hand side renders as the expression itself: "(fact)" is reserved for a
        // field-less custom call, where the whole fact really is the operand.
        string field = leaf.Left is not null ? Describe(leaf.Left) : leaf.Field ?? "(fact)";
        return leaf.Operator switch
        {
            // A quantifier is one trace node, so its per-element condition is rendered inline here
            // rather than traced separately - per-element results have no single slot to live in.
            ConditionOperator.Any or ConditionOperator.All or ConditionOperator.None =>
                $"{field} {leaf.Operator} ({Describe(leaf.ElementCondition!, nested: true)})",
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
        ExpressionOperator.Coalesce => "coalesce",
        ExpressionOperator.Count => "count",
        // Named explicitly rather than folded into a catch-all: a catch-all silently renders every
        // operator added later under the last one's name, which is how count once traced as coalesce.
        _ => @operator.ToString().ToLowerInvariant(),
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
