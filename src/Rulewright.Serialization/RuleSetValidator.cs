using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Rulewright.Serialization;

/// <summary>
/// Structurally validates a rule document against the Rulewright schema contract
/// (<c>docs/schema/rule-schema.json</c>) before parsing/compilation, producing
/// errors with JSON pointer paths. Usable standalone — this is the surface a
/// rule-builder UI's real-time validation hooks into.
/// </summary>
public static class RuleSetValidator
{
    /// <summary>
    /// Validates a parsed JSON document as a rule or rule set.
    /// </summary>
    /// <param name="document">The document root.</param>
    /// <returns>A result carrying zero or more pointer-addressed errors.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is null.</exception>
    public static RuleSetValidationResult Validate(RuleJsonValue document)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        var errors = new List<RuleValidationError>();
        if (document.Kind != RuleJsonValueKind.Object)
        {
            errors.Add(new RuleValidationError(string.Empty, "The document root must be a JSON object."));
            return new RuleSetValidationResult(errors);
        }

        if (document.TryGetProperty("decisionTable", out RuleJsonValue decisionTable))
        {
            ValidateDecisionTable(decisionTable, "/decisionTable", errors);
        }
        else if (document.TryGetProperty("rules", out _))
        {
            ValidateRuleSet(document, errors);
        }
        else
        {
            ValidateRule(document, string.Empty, errors);
        }

        return errors.Count == 0 ? RuleSetValidationResult.Success : new RuleSetValidationResult(errors);
    }

    private static void ValidateRuleSet(RuleJsonValue ruleSet, List<RuleValidationError> errors)
    {
        if (ruleSet.TryGetProperty("name", out RuleJsonValue name) && name.Kind != RuleJsonValueKind.String)
        {
            errors.Add(new RuleValidationError("/name", "'name' must be a string."));
        }

        ruleSet.TryGetProperty("rules", out RuleJsonValue rules);
        if (rules.Kind != RuleJsonValueKind.Array)
        {
            errors.Add(new RuleValidationError("/rules", "'rules' must be an array."));
            return;
        }

        if (rules.Items.Count == 0)
        {
            errors.Add(new RuleValidationError("/rules", "'rules' must contain at least one rule."));
            return;
        }

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < rules.Items.Count; i++)
        {
            string path = "/rules/" + i.ToString(CultureInfo.InvariantCulture);
            ValidateRule(rules.Items[i], path, errors);

            if (rules.Items[i].Kind == RuleJsonValueKind.Object
                && rules.Items[i].TryGetProperty("id", out RuleJsonValue id)
                && id.Kind == RuleJsonValueKind.String
                && !seenIds.Add(id.GetString()))
            {
                errors.Add(new RuleValidationError(path + "/id", $"Duplicate rule id '{id.GetString()}'."));
            }
        }
    }

    private static void ValidateRule(RuleJsonValue rule, string path, List<RuleValidationError> errors)
    {
        if (rule.Kind != RuleJsonValueKind.Object)
        {
            errors.Add(new RuleValidationError(path, "A rule must be a JSON object."));
            return;
        }

        if (!rule.TryGetProperty("id", out RuleJsonValue id))
        {
            errors.Add(new RuleValidationError(path, "'id' is required."));
        }
        else if (id.Kind != RuleJsonValueKind.String || id.GetString().Length == 0)
        {
            errors.Add(new RuleValidationError(path + "/id", "'id' must be a non-empty string."));
        }

        if (rule.TryGetProperty("description", out RuleJsonValue description)
            && description.Kind != RuleJsonValueKind.String)
        {
            errors.Add(new RuleValidationError(path + "/description", "'description' must be a string."));
        }

        if (rule.TryGetProperty("priority", out RuleJsonValue priority)
            && (priority.Kind != RuleJsonValueKind.Number
                || !priority.TryGetInt64(out long priorityValue)
                || priorityValue < int.MinValue
                || priorityValue > int.MaxValue))
        {
            errors.Add(new RuleValidationError(path + "/priority", "'priority' must be a 32-bit integer."));
        }

        if (rule.TryGetProperty("enabled", out RuleJsonValue enabled)
            && enabled.Kind != RuleJsonValueKind.True
            && enabled.Kind != RuleJsonValueKind.False)
        {
            errors.Add(new RuleValidationError(path + "/enabled", "'enabled' must be a boolean."));
        }

        if (!rule.TryGetProperty("condition", out RuleJsonValue condition))
        {
            errors.Add(new RuleValidationError(path, "'condition' is required."));
        }
        else
        {
            ValidateCondition(condition, path + "/condition", errors);
        }

        ValidateActionArray(rule, "actions", path, errors);
        ValidateActionArray(rule, "else", path, errors);

        if (rule.TryGetProperty("layout", out RuleJsonValue layout) && layout.Kind != RuleJsonValueKind.Object)
        {
            errors.Add(new RuleValidationError(path + "/layout", "'layout' must be an object."));
        }
    }

    private static void ValidateCondition(RuleJsonValue condition, string path, List<RuleValidationError> errors)
    {
        if (condition.Kind != RuleJsonValueKind.Object)
        {
            errors.Add(new RuleValidationError(path, "A condition must be a JSON object."));
            return;
        }

        // Discriminator: groups carry "type": "group"; anything else is a leaf.
        if (condition.TryGetProperty("type", out RuleJsonValue type))
        {
            if (type.Kind != RuleJsonValueKind.String || type.GetString() != "group")
            {
                errors.Add(new RuleValidationError(path + "/type", "'type' must be the string \"group\"."));
                return;
            }

            ValidateGroup(condition, path, errors);
        }
        else
        {
            ValidateLeaf(condition, path, errors);
        }
    }

    private static void ValidateGroup(RuleJsonValue group, string path, List<RuleValidationError> errors)
    {
        string? operatorName = null;
        if (!group.TryGetProperty("operator", out RuleJsonValue op))
        {
            errors.Add(new RuleValidationError(path, "'operator' is required for a group."));
        }
        else if (op.Kind != RuleJsonValueKind.String
            || (operatorName = op.GetString()) is not ("AND" or "OR" or "NOT"))
        {
            errors.Add(new RuleValidationError(path + "/operator", "Group 'operator' must be \"AND\", \"OR\", or \"NOT\"."));
            operatorName = null;
        }

        if (!group.TryGetProperty("rules", out RuleJsonValue rules) || rules.Kind != RuleJsonValueKind.Array)
        {
            errors.Add(new RuleValidationError(path, "'rules' (an array of child conditions) is required for a group."));
            return;
        }

        if (rules.Items.Count == 0)
        {
            errors.Add(new RuleValidationError(path + "/rules", "A group must contain at least one child condition."));
            return;
        }

        if (operatorName == "NOT" && rules.Items.Count != 1)
        {
            errors.Add(new RuleValidationError(path + "/rules", "A NOT group must contain exactly one child condition."));
        }

        for (int i = 0; i < rules.Items.Count; i++)
        {
            ValidateCondition(rules.Items[i], path + "/rules/" + i.ToString(CultureInfo.InvariantCulture), errors);
        }
    }

    private static void ValidateLeaf(RuleJsonValue leaf, string path, List<RuleValidationError> errors)
    {
        if (!leaf.TryGetProperty("operator", out RuleJsonValue op) || op.Kind != RuleJsonValueKind.String)
        {
            errors.Add(new RuleValidationError(path, "'operator' (a string) is required for a condition."));
            return;
        }

        if (!OperatorMap.TryParse(op.GetString(), out Core.ConditionOperator parsedOperator))
        {
            errors.Add(new RuleValidationError(
                path + "/operator",
                $"Unknown operator '{op.GetString()}'. Expected one of: {string.Join(", ", OperatorMap.JsonNames)}."));
            return;
        }

        bool hasField = leaf.TryGetProperty("field", out RuleJsonValue field);
        if (hasField && (field.Kind != RuleJsonValueKind.String || field.GetString().Length == 0))
        {
            errors.Add(new RuleValidationError(path + "/field", "'field' must be a non-empty string."));
            hasField = false;
        }

        bool hasExpression = leaf.TryGetProperty("expression", out RuleJsonValue leftExpression);

        if (parsedOperator == Core.ConditionOperator.Custom)
        {
            if (hasExpression)
            {
                errors.Add(new RuleValidationError(path + "/expression", "'expression' is not allowed with operator \"custom\"; use 'field'."));
            }

            if (!leaf.TryGetProperty("name", out RuleJsonValue name)
                || name.Kind != RuleJsonValueKind.String
                || name.GetString().Length == 0)
            {
                errors.Add(new RuleValidationError(path, "'name' (a non-empty string) is required when operator is \"custom\"."));
            }

            // A custom function's operand is whatever it declares (scalar, text, or an array such
            // as IsBetweenInclusive's [min, max]). Only object nodes have no CLR mapping.
            if (leaf.TryGetProperty("value", out RuleJsonValue customValue))
            {
                ValidateOperandShape(customValue, path + "/value", errors);
            }

            return;
        }

        // Non-custom leaves take their left-hand side from 'field' (a dotted path) or
        // 'expression' (a computed value), but not both.
        if (hasField && hasExpression)
        {
            errors.Add(new RuleValidationError(path, "A condition must have 'field' or 'expression', not both."));
        }
        else if (hasExpression)
        {
            ValidateValueExpression(leftExpression, path + "/expression", errors);
        }
        else if (!hasField)
        {
            errors.Add(new RuleValidationError(path, $"'field' or 'expression' is required for operator '{op.GetString()}'."));
        }

        bool hasValue = leaf.TryGetProperty("value", out RuleJsonValue value);
        switch (parsedOperator)
        {
            case Core.ConditionOperator.IsNull:
            case Core.ConditionOperator.IsNotNull:
                if (hasValue)
                {
                    errors.Add(new RuleValidationError(path + "/value", $"'value' is not allowed for operator '{op.GetString()}'."));
                }

                break;

            case Core.ConditionOperator.In:
            case Core.ConditionOperator.NotIn:
                if (!hasValue || value.Kind != RuleJsonValueKind.Array)
                {
                    errors.Add(new RuleValidationError(
                        hasValue ? path + "/value" : path,
                        $"'value' must be a non-empty array for operator '{op.GetString()}'."));
                }
                else if (value.Items.Count == 0)
                {
                    errors.Add(new RuleValidationError(path + "/value", $"'value' must be a non-empty array for operator '{op.GetString()}'."));
                }
                else
                {
                    ValidateSetItems(value, path + "/value", errors);
                }

                break;

            case Core.ConditionOperator.Contains:
            case Core.ConditionOperator.StartsWith:
            case Core.ConditionOperator.EndsWith:
                if (!hasValue || value.Kind != RuleJsonValueKind.String)
                {
                    errors.Add(new RuleValidationError(
                        hasValue ? path + "/value" : path,
                        $"'value' must be a string for operator '{op.GetString()}'."));
                }

                break;

            case Core.ConditionOperator.MatchesRegex:
                if (!hasValue || value.Kind != RuleJsonValueKind.String)
                {
                    errors.Add(new RuleValidationError(
                        hasValue ? path + "/value" : path,
                        "'value' must be a string (a regular expression) for operator 'MatchesRegex'."));
                }
                else
                {
                    ValidateRegexPattern(value.GetString(), path + "/value", errors);
                }

                break;

            case Core.ConditionOperator.GreaterThan:
            case Core.ConditionOperator.GreaterThanOrEqual:
            case Core.ConditionOperator.LessThan:
            case Core.ConditionOperator.LessThanOrEqual:
                if (!hasValue || value.Kind is not (RuleJsonValueKind.Number or RuleJsonValueKind.String))
                {
                    errors.Add(new RuleValidationError(
                        hasValue ? path + "/value" : path,
                        $"'value' must be a number or string for operator '{op.GetString()}'."));
                }

                break;

            case Core.ConditionOperator.Equal:
            case Core.ConditionOperator.NotEqual:
                if (!hasValue)
                {
                    errors.Add(new RuleValidationError(path, $"'value' is required for operator '{op.GetString()}'."));
                }
                else if (value.Kind is RuleJsonValueKind.Object or RuleJsonValueKind.Array)
                {
                    errors.Add(new RuleValidationError(path + "/value", $"'value' must be a scalar or null for operator '{op.GetString()}'."));
                }

                break;
        }
    }

    /// <summary>
    /// Checks the members of an In/NotIn set. Only scalars have a CLR mapping the compiled path's
    /// typed set and the interpreter's value comparison agree on, so an object, a nested array, or a
    /// null is rejected at its own pointer. Shared by condition leaves and decision-table cells so
    /// the two authoring surfaces cannot drift apart.
    /// </summary>
    private static void ValidateSetItems(RuleJsonValue value, string path, List<RuleValidationError> errors)
    {
        for (int i = 0; i < value.Items.Count; i++)
        {
            RuleJsonValueKind itemKind = value.Items[i].Kind;
            if (itemKind is RuleJsonValueKind.Object or RuleJsonValueKind.Array or RuleJsonValueKind.Null)
            {
                errors.Add(new RuleValidationError(
                    path + "/" + i.ToString(CultureInfo.InvariantCulture),
                    "In/NotIn values must be scalars (string, number, or boolean)."));
            }
        }
    }

    /// <summary>
    /// Compiles a pattern to prove it is a well-formed regular expression, so a bad one is a
    /// validation error with a pointer rather than a compilation failure at load time. Shared by
    /// condition leaves and decision-table cells.
    /// </summary>
    private static void ValidateRegexPattern(string pattern, string path, List<RuleValidationError> errors)
    {
        try
        {
            _ = new Regex(pattern);
        }
        catch (ArgumentException ex)
        {
            errors.Add(new RuleValidationError(path, $"Invalid regular expression: {ex.Message}"));
        }
    }

    /// <summary>
    /// Checks that a condition operand carries only nodes the parser can turn into CLR values —
    /// scalars, nulls, and arrays of the same. Object nodes have no mapping and are reported at
    /// the pointer of the offending node rather than surfacing later as a parse failure.
    /// </summary>
    private static void ValidateOperandShape(RuleJsonValue value, string path, List<RuleValidationError> errors)
    {
        if (value.Kind == RuleJsonValueKind.Object)
        {
            errors.Add(new RuleValidationError(path, "'value' must be a scalar, null, or an array; objects are not valid operands."));
            return;
        }

        if (value.Kind != RuleJsonValueKind.Array)
        {
            return;
        }

        for (int i = 0; i < value.Items.Count; i++)
        {
            ValidateOperandShape(value.Items[i], path + "/" + i.ToString(CultureInfo.InvariantCulture), errors);
        }
    }

    private static void ValidateActionArray(RuleJsonValue rule, string property, string path, List<RuleValidationError> errors)
    {
        if (!rule.TryGetProperty(property, out RuleJsonValue actions))
        {
            return;
        }

        if (actions.Kind != RuleJsonValueKind.Array)
        {
            errors.Add(new RuleValidationError(path + "/" + property, $"'{property}' must be an array."));
            return;
        }

        for (int i = 0; i < actions.Items.Count; i++)
        {
            ValidateAction(actions.Items[i], path + "/" + property + "/" + i.ToString(CultureInfo.InvariantCulture), errors);
        }
    }

    private static void ValidateAction(RuleJsonValue action, string path, List<RuleValidationError> errors)
    {
        if (action.Kind != RuleJsonValueKind.Object)
        {
            errors.Add(new RuleValidationError(path, "An action must be a JSON object."));
            return;
        }

        bool hasType = action.TryGetProperty("type", out RuleJsonValue type);
        bool isRemove = hasType && type.Kind == RuleJsonValueKind.String && type.GetString() == Core.RuleAction.RemoveOutputType;
        if (!hasType
            || type.Kind != RuleJsonValueKind.String
            || (type.GetString() != Core.RuleAction.SetOutputType
                && type.GetString() != Core.RuleAction.AddToOutputType
                && type.GetString() != Core.RuleAction.AppendToOutputType
                && type.GetString() != Core.RuleAction.RemoveOutputType))
        {
            errors.Add(new RuleValidationError(
                hasType ? path + "/type" : path,
                $"Action 'type' must be \"{Core.RuleAction.SetOutputType}\", "
                + $"\"{Core.RuleAction.AddToOutputType}\", \"{Core.RuleAction.AppendToOutputType}\", "
                + $"or \"{Core.RuleAction.RemoveOutputType}\"."));
        }

        if (!action.TryGetProperty("target", out RuleJsonValue target)
            || target.Kind != RuleJsonValueKind.String
            || target.GetString().Length == 0)
        {
            errors.Add(new RuleValidationError(
                action.TryGetProperty("target", out _) ? path + "/target" : path,
                "Action 'target' must be a non-empty string."));
        }

        bool hasValue = action.TryGetProperty("value", out RuleJsonValue value);
        if (isRemove)
        {
            // removeOutput deletes a key; it takes no value.
            if (hasValue)
            {
                errors.Add(new RuleValidationError(path + "/value", $"'value' is not allowed for action type \"{Core.RuleAction.RemoveOutputType}\"."));
            }
        }
        else if (!hasValue)
        {
            errors.Add(new RuleValidationError(path, "Action 'value' is required (a constant scalar or a value expression)."));
        }
        else
        {
            ValidateValueExpression(value, path + "/value", errors);
        }
    }

    private static void ValidateValueExpression(RuleJsonValue node, string path, List<RuleValidationError> errors)
    {
        if (node.Kind == RuleJsonValueKind.Array)
        {
            errors.Add(new RuleValidationError(path, "An expression must be a scalar literal or an object; arrays are not valid."));
            return;
        }

        if (node.Kind != RuleJsonValueKind.Object)
        {
            // A bare scalar (string, number, boolean, null) is a valid literal.
            return;
        }

        bool hasOp = node.TryGetProperty("op", out RuleJsonValue op);
        bool hasField = node.TryGetProperty("field", out RuleJsonValue field);
        bool hasLiteral = node.TryGetProperty("literal", out RuleJsonValue literal);

        int discriminators = (hasOp ? 1 : 0) + (hasField ? 1 : 0) + (hasLiteral ? 1 : 0);
        if (discriminators == 0)
        {
            errors.Add(new RuleValidationError(path, "An expression object must have exactly one of 'op', 'field', or 'literal'."));
            return;
        }

        if (discriminators > 1)
        {
            errors.Add(new RuleValidationError(path, "An expression object must have exactly one of 'op', 'field', or 'literal', not several."));
            return;
        }

        if (hasField)
        {
            if (field.Kind != RuleJsonValueKind.String || field.GetString().Length == 0)
            {
                errors.Add(new RuleValidationError(path + "/field", "Expression 'field' must be a non-empty string."));
            }

            return;
        }

        if (hasLiteral)
        {
            if (literal.Kind is RuleJsonValueKind.Object or RuleJsonValueKind.Array)
            {
                errors.Add(new RuleValidationError(path + "/literal", "Expression 'literal' must be a scalar or null."));
            }

            return;
        }

        // Operator node.
        if (op.Kind != RuleJsonValueKind.String || !ExpressionOperatorMap.TryParse(op.GetString(), out Core.ExpressionOperator parsedOperator))
        {
            errors.Add(new RuleValidationError(
                path + "/op",
                $"Unknown expression operator '{(op.Kind == RuleJsonValueKind.String ? op.GetString() : op.Kind.ToString())}'. "
                + $"Expected one of: {string.Join(", ", ExpressionOperatorMap.JsonNames)}."));
            return;
        }

        if (!node.TryGetProperty("operands", out RuleJsonValue operands) || operands.Kind != RuleJsonValueKind.Array)
        {
            errors.Add(new RuleValidationError(path, "An operator expression requires an 'operands' array."));
            return;
        }

        int? requiredArity = ExpressionOperatorMap.RequiredArity(parsedOperator);
        if (requiredArity is int exact)
        {
            if (operands.Items.Count != exact)
            {
                errors.Add(new RuleValidationError(
                    path + "/operands",
                    $"Operator '{op.GetString()}' requires exactly {exact} operand{(exact == 1 ? string.Empty : "s")}."));
            }
        }
        else if (operands.Items.Count < 2)
        {
            errors.Add(new RuleValidationError(
                path + "/operands",
                $"Operator '{op.GetString()}' requires at least two operands."));
        }

        for (int i = 0; i < operands.Items.Count; i++)
        {
            ValidateValueExpression(operands.Items[i], path + "/operands/" + i.ToString(CultureInfo.InvariantCulture), errors);
        }
    }

    private static void ValidateDecisionTable(RuleJsonValue table, string path, List<RuleValidationError> errors)
    {
        if (table.Kind != RuleJsonValueKind.Object)
        {
            errors.Add(new RuleValidationError(path, "'decisionTable' must be a JSON object."));
            return;
        }

        if (table.TryGetProperty("hitPolicy", out RuleJsonValue hitPolicy)
            && (hitPolicy.Kind != RuleJsonValueKind.String || hitPolicy.GetString() is not ("collect" or "first")))
        {
            errors.Add(new RuleValidationError(path + "/hitPolicy", "'hitPolicy' must be \"collect\" or \"first\"."));
        }

        int inputCount = ValidateDecisionInputs(table, path, errors, out RuleJsonValue inputs);
        int outputCount = ValidateDecisionOutputs(table, path, errors);

        if (!table.TryGetProperty("rows", out RuleJsonValue rows) || rows.Kind != RuleJsonValueKind.Array)
        {
            errors.Add(new RuleValidationError(path + "/rows", "'rows' must be a non-empty array."));
            return;
        }

        if (rows.Items.Count == 0)
        {
            errors.Add(new RuleValidationError(path + "/rows", "'rows' must contain at least one row."));
            return;
        }

        for (int r = 0; r < rows.Items.Count; r++)
        {
            string rowPath = path + "/rows/" + r.ToString(CultureInfo.InvariantCulture);
            ValidateDecisionRow(rows.Items[r], rowPath, inputs, inputCount, outputCount, errors);
        }
    }

    private static int ValidateDecisionInputs(RuleJsonValue table, string path, List<RuleValidationError> errors, out RuleJsonValue inputs)
    {
        if (!table.TryGetProperty("inputs", out inputs) || inputs.Kind != RuleJsonValueKind.Array || inputs.Items.Count == 0)
        {
            errors.Add(new RuleValidationError(path + "/inputs", "'inputs' must be a non-empty array of input columns."));
            return 0;
        }

        for (int i = 0; i < inputs.Items.Count; i++)
        {
            string columnPath = path + "/inputs/" + i.ToString(CultureInfo.InvariantCulture);
            RuleJsonValue column = inputs.Items[i];
            if (column.Kind != RuleJsonValueKind.Object)
            {
                errors.Add(new RuleValidationError(columnPath, "An input column must be a JSON object."));
                continue;
            }

            if (!column.TryGetProperty("field", out RuleJsonValue field)
                || field.Kind != RuleJsonValueKind.String
                || field.GetString().Length == 0)
            {
                errors.Add(new RuleValidationError(columnPath + "/field", "Input 'field' must be a non-empty string."));
            }

            if (column.TryGetProperty("operator", out RuleJsonValue op)
                && (op.Kind != RuleJsonValueKind.String
                    || !OperatorMap.TryParse(op.GetString(), out Core.ConditionOperator parsed)
                    || !IsTableOperator(parsed)))
            {
                errors.Add(new RuleValidationError(
                    columnPath + "/operator",
                    "Input 'operator' must be a value-comparison operator (not IsNull, IsNotNull, or custom)."));
            }
        }

        return inputs.Items.Count;
    }

    private static int ValidateDecisionOutputs(RuleJsonValue table, string path, List<RuleValidationError> errors)
    {
        if (!table.TryGetProperty("outputs", out RuleJsonValue outputs) || outputs.Kind != RuleJsonValueKind.Array || outputs.Items.Count == 0)
        {
            errors.Add(new RuleValidationError(path + "/outputs", "'outputs' must be a non-empty array of output columns."));
            return 0;
        }

        for (int i = 0; i < outputs.Items.Count; i++)
        {
            string columnPath = path + "/outputs/" + i.ToString(CultureInfo.InvariantCulture);
            RuleJsonValue column = outputs.Items[i];
            if (column.Kind != RuleJsonValueKind.Object)
            {
                errors.Add(new RuleValidationError(columnPath, "An output column must be a JSON object."));
                continue;
            }

            if (!column.TryGetProperty("target", out RuleJsonValue target)
                || target.Kind != RuleJsonValueKind.String
                || target.GetString().Length == 0)
            {
                errors.Add(new RuleValidationError(columnPath + "/target", "Output 'target' must be a non-empty string."));
            }

            if (column.TryGetProperty("type", out RuleJsonValue type)
                && (type.Kind != RuleJsonValueKind.String || !IsActionType(type.GetString())))
            {
                errors.Add(new RuleValidationError(
                    columnPath + "/type",
                    $"Output 'type' must be \"{Core.RuleAction.SetOutputType}\", "
                    + $"\"{Core.RuleAction.AddToOutputType}\", or \"{Core.RuleAction.AppendToOutputType}\"."));
            }
        }

        return outputs.Items.Count;
    }

    private static void ValidateDecisionRow(
        RuleJsonValue row,
        string path,
        RuleJsonValue inputs,
        int inputCount,
        int outputCount,
        List<RuleValidationError> errors)
    {
        if (row.Kind != RuleJsonValueKind.Object)
        {
            errors.Add(new RuleValidationError(path, "A row must be a JSON object."));
            return;
        }

        if (!row.TryGetProperty("when", out RuleJsonValue when) || when.Kind != RuleJsonValueKind.Array)
        {
            errors.Add(new RuleValidationError(path + "/when", "Row 'when' must be an array of input cells."));
        }
        else if (inputCount > 0 && when.Items.Count != inputCount)
        {
            errors.Add(new RuleValidationError(
                path + "/when",
                $"Row 'when' must have {inputCount.ToString(CultureInfo.InvariantCulture)} cell(s), one per input."));
        }
        else
        {
            for (int c = 0; c < when.Items.Count && c < inputCount; c++)
            {
                ValidateWhenCell(inputs.Items[c], when.Items[c], path + "/when/" + c.ToString(CultureInfo.InvariantCulture), errors);
            }
        }

        if (!row.TryGetProperty("then", out RuleJsonValue then) || then.Kind != RuleJsonValueKind.Array)
        {
            errors.Add(new RuleValidationError(path + "/then", "Row 'then' must be an array of output cells."));
        }
        else if (outputCount > 0 && then.Items.Count != outputCount)
        {
            errors.Add(new RuleValidationError(
                path + "/then",
                $"Row 'then' must have {outputCount.ToString(CultureInfo.InvariantCulture)} cell(s), one per output."));
        }
        else
        {
            for (int c = 0; c < then.Items.Count; c++)
            {
                // A null 'then' cell skips that output; any other value is a value expression.
                if (then.Items[c].Kind != RuleJsonValueKind.Null)
                {
                    ValidateValueExpression(then.Items[c], path + "/then/" + c.ToString(CultureInfo.InvariantCulture), errors);
                }
            }
        }
    }

    private static void ValidateWhenCell(RuleJsonValue column, RuleJsonValue cell, string path, List<RuleValidationError> errors)
    {
        // A null cell is a wildcard (no condition for that column).
        if (cell.Kind == RuleJsonValueKind.Null)
        {
            return;
        }

        Core.ConditionOperator op = Core.ConditionOperator.Equal;
        if (column.Kind == RuleJsonValueKind.Object
            && column.TryGetProperty("operator", out RuleJsonValue opValue)
            && opValue.Kind == RuleJsonValueKind.String)
        {
            OperatorMap.TryParse(opValue.GetString(), out op);
        }

        switch (op)
        {
            case Core.ConditionOperator.In:
            case Core.ConditionOperator.NotIn:
                if (cell.Kind != RuleJsonValueKind.Array || cell.Items.Count == 0)
                {
                    errors.Add(new RuleValidationError(path, "This cell must be a non-empty array (its column uses In/NotIn)."));
                }
                else
                {
                    ValidateSetItems(cell, path, errors);
                }

                break;

            case Core.ConditionOperator.Contains:
            case Core.ConditionOperator.StartsWith:
            case Core.ConditionOperator.EndsWith:
            case Core.ConditionOperator.MatchesRegex:
                if (cell.Kind != RuleJsonValueKind.String)
                {
                    errors.Add(new RuleValidationError(path, "This cell must be a string (its column uses a string operator)."));
                }
                else if (op == Core.ConditionOperator.MatchesRegex)
                {
                    ValidateRegexPattern(cell.GetString(), path, errors);
                }

                break;

            default:
                if (cell.Kind is RuleJsonValueKind.Object or RuleJsonValueKind.Array)
                {
                    errors.Add(new RuleValidationError(path, "This cell must be a scalar or null."));
                }

                break;
        }
    }

    private static bool IsTableOperator(Core.ConditionOperator op)
        => op is not (Core.ConditionOperator.IsNull or Core.ConditionOperator.IsNotNull or Core.ConditionOperator.Custom);

    private static bool IsActionType(string type)
        => type == Core.RuleAction.SetOutputType
        || type == Core.RuleAction.AddToOutputType
        || type == Core.RuleAction.AppendToOutputType;
}
