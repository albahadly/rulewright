using System;
using System.Collections.Generic;
using System.Globalization;
using RuleWright.Core;

namespace RuleWright.Serialization;

/// <summary>
/// Maps a validated JSON document to the immutable <see cref="RuleSet"/> domain model.
/// The <c>layout</c> key is presentation metadata and is skipped entirely.
/// </summary>
public static class RuleSetParser
{
    /// <summary>
    /// Validates and parses a rule document (a single rule or a rule set), against the
    /// built-in vocabulary only.
    /// </summary>
    /// <param name="document">The document root.</param>
    /// <returns>The parsed rule set; a single-rule document becomes a one-rule set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is null.</exception>
    /// <exception cref="RuleValidationException">The document fails structural validation.</exception>
    public static RuleSet Parse(RuleJsonValue document)
        => Parse(document, null);

    /// <summary>
    /// Validates and parses a rule document, folding an engine's registered vocabulary
    /// (custom action types) into the schema contract.
    ///
    /// <para>Parsing is also where <c>params</c> disappear: a param is a named subexpression,
    /// and every <c>{ "param": "..." }</c> reference is replaced by the (immutable, shared)
    /// expression it names — local names shadowing the set's. The domain model, the content
    /// hash, and both execution paths therefore never see params at all, which is exactly why
    /// a param-authored document and its hand-inlined equivalent compile to the same
    /// delegates.</para>
    /// </summary>
    /// <param name="document">The document root.</param>
    /// <param name="options">The engine-registered vocabulary, or null for the built-ins only.</param>
    /// <returns>The parsed rule set; a single-rule document becomes a one-rule set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is null.</exception>
    /// <exception cref="RuleValidationException">The document fails structural validation.</exception>
    public static RuleSet Parse(RuleJsonValue document, RuleDocumentOptions? options)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        RuleSetValidationResult validation = RuleSetValidator.Validate(document, options);
        if (!validation.IsValid)
        {
            throw new RuleValidationException(validation.Errors);
        }

        if (document.TryGetProperty("decisionTable", out RuleJsonValue decisionTable))
        {
            return ExpandDecisionTable(decisionTable);
        }

        if (document.TryGetProperty("rules", out RuleJsonValue rules))
        {
            string? name = document.TryGetProperty("name", out RuleJsonValue nameValue)
                ? nameValue.GetString()
                : null;

            // The set's own semantics, the same flag a `first` decision table produces. Absent
            // means collect, so every document written before this property keeps its behaviour.
            bool stopAfterFirstMatch =
                document.TryGetProperty("stopAfterFirstMatch", out RuleJsonValue stopValue)
                && stopValue.Kind == RuleJsonValueKind.True;

            ParamScope? globalParams = ParamScope.Create(document, parent: null);
            var parsed = new List<Rule>(rules.Items.Count);
            foreach (RuleJsonValue rule in rules.Items)
            {
                parsed.Add(ParseRule(rule, globalParams));
            }

            return new RuleSet(parsed, name, stopAfterFirstMatch);
        }

        return new RuleSet(new[] { ParseRule(document, outerParams: null) });
    }

    private static Rule ParseRule(RuleJsonValue rule, ParamScope? outerParams)
    {
        ParamScope? paramScope = ParamScope.Create(rule, outerParams);

        rule.TryGetProperty("id", out RuleJsonValue id);
        rule.TryGetProperty("condition", out RuleJsonValue condition);

        string? description = rule.TryGetProperty("description", out RuleJsonValue descriptionValue)
            ? descriptionValue.GetString()
            : null;

        string? failureMessage = rule.TryGetProperty("failureMessage", out RuleJsonValue failureMessageValue)
            ? failureMessageValue.GetString()
            : null;

        int priority = 0;
        if (rule.TryGetProperty("priority", out RuleJsonValue priorityValue)
            && priorityValue.TryGetInt64(out long parsedPriority))
        {
            priority = (int)parsedPriority;
        }

        bool enabled = !rule.TryGetProperty("enabled", out RuleJsonValue enabledValue)
            || enabledValue.Kind == RuleJsonValueKind.True;

        List<RuleAction>? actions = rule.TryGetProperty("actions", out RuleJsonValue actionsValue)
            ? ParseActions(actionsValue, paramScope)
            : null;

        List<RuleAction>? elseActions = rule.TryGetProperty("else", out RuleJsonValue elseValue)
            ? ParseActions(elseValue, paramScope)
            : null;

        return new Rule(
            id.GetString(),
            ParseCondition(condition, paramScope),
            actions,
            description,
            priority,
            enabled,
            elseActions,
            failureMessage);
    }

    private static List<RuleAction> ParseActions(RuleJsonValue actionsValue, ParamScope? paramScope)
    {
        var actions = new List<RuleAction>(actionsValue.Items.Count);
        foreach (RuleJsonValue action in actionsValue.Items)
        {
            action.TryGetProperty("type", out RuleJsonValue type);
            action.TryGetProperty("target", out RuleJsonValue target);

            // removeOutput deletes a key and carries no value.
            if (type.GetString() == RuleAction.RemoveOutputType)
            {
                actions.Add(RuleAction.RemoveOutput(target.GetString()));
                continue;
            }

            // A registered custom action may omit its value; the handler then sees null.
            if (!action.TryGetProperty("value", out RuleJsonValue value))
            {
                actions.Add(new RuleAction(type.GetString(), target.GetString(), (object?)null));
                continue;
            }

            actions.Add(new RuleAction(type.GetString(), target.GetString(), ParseValueExpression(value, paramScope)));
        }

        return actions;
    }

    /// <summary>
    /// Expands a decision table into ordinary rules — one rule per row — so the engine
    /// evaluates it through the normal compiled/interpreted path with no special casing.
    /// Each input column contributes a condition leaf (a null cell is a wildcard); a row with
    /// all-wildcard inputs is a catch-all. The <c>first</c> hit policy is baked into the
    /// conditions by ANDing each row with the negation of every earlier row's own condition,
    /// so exactly the first matching row fires under normal evaluation.
    /// </summary>
    private static RuleSet ExpandDecisionTable(RuleJsonValue table)
    {
        string? name = table.TryGetProperty("name", out RuleJsonValue nameValue) && nameValue.Kind == RuleJsonValueKind.String
            ? nameValue.GetString()
            : null;
        string idBase = table.TryGetProperty("id", out RuleJsonValue idValue) && idValue.Kind == RuleJsonValueKind.String
            ? idValue.GetString()
            : "row";
        bool firstPolicy = table.TryGetProperty("hitPolicy", out RuleJsonValue hitPolicy)
            && hitPolicy.Kind == RuleJsonValueKind.String
            && hitPolicy.GetString() == "first";

        ParamScope? paramScope = ParamScope.Create(table, parent: null);
        table.TryGetProperty("inputs", out RuleJsonValue inputs);
        table.TryGetProperty("outputs", out RuleJsonValue outputs);
        table.TryGetProperty("rows", out RuleJsonValue rows);

        int inputCount = inputs.Items.Count;
        var fields = new string[inputCount];
        var operators = new ConditionOperator[inputCount];
        for (int c = 0; c < inputCount; c++)
        {
            inputs.Items[c].TryGetProperty("field", out RuleJsonValue field);
            fields[c] = field.GetString();
            operators[c] = ConditionOperator.Equal;
            if (inputs.Items[c].TryGetProperty("operator", out RuleJsonValue op))
            {
                OperatorMap.TryParse(op.GetString(), out operators[c]);
            }
        }

        int outputCount = outputs.Items.Count;
        var targets = new string[outputCount];
        var types = new string[outputCount];
        for (int c = 0; c < outputCount; c++)
        {
            outputs.Items[c].TryGetProperty("target", out RuleJsonValue target);
            targets[c] = target.GetString();
            types[c] = outputs.Items[c].TryGetProperty("type", out RuleJsonValue type) && type.Kind == RuleJsonValueKind.String
                ? type.GetString()
                : RuleAction.SetOutputType;
        }

        int rowCount = rows.Items.Count;
        var expanded = new List<Rule>(rowCount);
        for (int r = 0; r < rowCount; r++)
        {
            rows.Items[r].TryGetProperty("when", out RuleJsonValue when);
            var leaves = new List<ConditionNode>();
            for (int c = 0; c < inputCount; c++)
            {
                RuleJsonValue cell = when.Items[c];
                if (cell.Kind == RuleJsonValueKind.Null)
                {
                    continue; // wildcard: no condition for this column
                }

                leaves.Add(new ConditionLeaf(fields[c], operators[c], ParseLeafValue(cell)));
            }

            ConditionNode condition = leaves.Count switch
            {
                0 => AlwaysTrue(fields[0]),
                1 => leaves[0],
                _ => new ConditionGroup(LogicalOperator.And, leaves),
            };

            rows.Items[r].TryGetProperty("then", out RuleJsonValue then);
            var actions = new List<RuleAction>();
            for (int c = 0; c < outputCount; c++)
            {
                RuleJsonValue cell = then.Items[c];
                if (cell.Kind == RuleJsonValueKind.Null)
                {
                    continue; // this row does not write this output
                }

                actions.Add(new RuleAction(types[c], targets[c], ParseValueExpression(cell, paramScope)));
            }

            string id = idBase + "-" + r.ToString(CultureInfo.InvariantCulture);
            expanded.Add(new Rule(id, condition, actions, description: null, priority: rowCount - r));
        }

        return new RuleSet(expanded, name, stopAfterFirstMatch: firstPolicy);
    }

    /// <summary>
    /// Converts a condition leaf's comparison operand by its JSON shape rather than by the
    /// operator: an array becomes <c>object?[]</c> (recursively), any other node its scalar CLR
    /// value. In/NotIn take arrays, and a <c>custom</c> function declaring
    /// <see cref="RuleFunctionValueKind.Array"/> takes one too, so shape — not operator — is what
    /// decides. Validation has already rejected the object nodes that have no CLR mapping.
    /// </summary>
    private static object? ParseLeafValue(RuleJsonValue node)
    {
        if (node.Kind != RuleJsonValueKind.Array)
        {
            return node.ToClrValue();
        }

        var items = new object?[node.Items.Count];
        for (int i = 0; i < node.Items.Count; i++)
        {
            items[i] = ParseLeafValue(node.Items[i]);
        }

        return items;
    }

    private static ConditionNode AlwaysTrue(string field)
        => new ConditionGroup(
            LogicalOperator.Or,
            new ConditionNode[]
            {
                new ConditionLeaf(field, ConditionOperator.IsNotNull, null),
                new ConditionLeaf(field, ConditionOperator.IsNull, null),
            });

    private static ValueExpression ParseValueExpression(RuleJsonValue node, ParamScope? paramScope)
    {
        if (node.Kind == RuleJsonValueKind.Object)
        {
            if (node.TryGetProperty("op", out RuleJsonValue op))
            {
                ExpressionOperatorMap.TryParse(op.GetString(), out ExpressionOperator @operator);
                node.TryGetProperty("operands", out RuleJsonValue operands);
                var parsedOperands = new List<ValueExpression>(operands.Items.Count);
                foreach (RuleJsonValue operand in operands.Items)
                {
                    parsedOperands.Add(ParseValueExpression(operand, paramScope));
                }

                return new OperatorExpression(@operator, parsedOperands);
            }

            if (node.TryGetProperty("call", out RuleJsonValue call))
            {
                var arguments = new List<ValueExpression>();
                if (node.TryGetProperty("operands", out RuleJsonValue callOperands))
                {
                    foreach (RuleJsonValue operand in callOperands.Items)
                    {
                        arguments.Add(ParseValueExpression(operand, paramScope));
                    }
                }

                return new CallExpression(call.GetString(), arguments);
            }

            if (node.TryGetProperty("param", out RuleJsonValue param))
            {
                // Substitution point: the reference becomes the named (immutable, shared)
                // expression itself. Validation has already guaranteed the name resolves
                // and that definitions are acyclic.
                return paramScope!.Resolve(param.GetString());
            }

            if (node.TryGetProperty("field", out RuleJsonValue field))
            {
                return new FieldExpression(field.GetString());
            }

            // Validation guarantees an explicit-literal object here; { "literal": <scalar> }.
            node.TryGetProperty("literal", out RuleJsonValue literal);
            return new LiteralExpression(literal.ToClrValue());
        }

        // A bare scalar is a literal.
        return new LiteralExpression(node.ToClrValue());
    }

    private static ConditionNode ParseCondition(RuleJsonValue condition, ParamScope? paramScope)
    {
        if (condition.TryGetProperty("type", out _))
        {
            condition.TryGetProperty("operator", out RuleJsonValue groupOperator);
            LogicalOperator logical = groupOperator.GetString() switch
            {
                "AND" => LogicalOperator.And,
                "OR" => LogicalOperator.Or,
                _ => LogicalOperator.Not,
            };

            condition.TryGetProperty("rules", out RuleJsonValue children);
            var parsedChildren = new List<ConditionNode>(children.Items.Count);
            foreach (RuleJsonValue child in children.Items)
            {
                parsedChildren.Add(ParseCondition(child, paramScope));
            }

            return new ConditionGroup(logical, parsedChildren);
        }

        condition.TryGetProperty("operator", out RuleJsonValue leafOperator);
        OperatorMap.TryParse(leafOperator.GetString(), out ConditionOperator @operator);

        string? field = condition.TryGetProperty("field", out RuleJsonValue fieldValue)
            ? fieldValue.GetString()
            : null;

        string? functionName = condition.TryGetProperty("name", out RuleJsonValue nameValue)
            ? nameValue.GetString()
            : null;

        // A quantifier reads a collection from 'field' and applies a nested condition per element.
        if (ConditionLeaf.IsQuantifier(@operator))
        {
            // Params are not referenceable inside the element condition (validation rejects
            // them), so the scope passed on is never consulted there.
            condition.TryGetProperty("condition", out RuleJsonValue elementCondition);
            return ConditionLeaf.Quantifier(field!, @operator, ParseCondition(elementCondition, paramScope));
        }

        object? value = null;
        if (condition.TryGetProperty("value", out RuleJsonValue operand))
        {
            value = ParseLeafValue(operand);
        }

        if (condition.TryGetProperty("expression", out RuleJsonValue leftExpression))
        {
            return new ConditionLeaf(ParseValueExpression(leftExpression, paramScope), @operator, value);
        }

        return new ConditionLeaf(field, @operator, value, functionName);
    }

    /// <summary>
    /// One level of <c>params</c> during parsing: the container's own definitions plus the
    /// enclosing level (a rule's params over the set's). <see cref="Resolve"/> parses a
    /// definition on first reference — own names first, so a local shadows a global — and
    /// memoizes the result, so every reference to a param shares one immutable expression
    /// instance. Validation has already established that every reference resolves and that
    /// definitions are acyclic, which is what makes this recursion terminate.
    /// </summary>
    private sealed class ParamScope
    {
        private readonly ParamScope? _parent;
        private readonly Dictionary<string, RuleJsonValue> _definitions;
        private readonly Dictionary<string, ValueExpression> _resolved =
            new Dictionary<string, ValueExpression>(StringComparer.Ordinal);

        private ParamScope(RuleJsonValue paramsValue, ParamScope? parent)
        {
            _parent = parent;
            _definitions = new Dictionary<string, RuleJsonValue>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, RuleJsonValue> definition in paramsValue.Properties)
            {
                _definitions[definition.Key] = definition.Value;
            }
        }

        /// <summary>
        /// The scope for a container: a new level when it declares <c>params</c>, otherwise
        /// the enclosing scope unchanged (null when there is none anywhere).
        /// </summary>
        internal static ParamScope? Create(RuleJsonValue container, ParamScope? parent)
            => container.TryGetProperty("params", out RuleJsonValue paramsValue)
                && paramsValue.Kind == RuleJsonValueKind.Object
                ? new ParamScope(paramsValue, parent)
                : parent;

        internal ValueExpression Resolve(string name)
        {
            if (_resolved.TryGetValue(name, out ValueExpression? expression))
            {
                return expression;
            }

            if (_definitions.TryGetValue(name, out RuleJsonValue? definition))
            {
                ValueExpression parsed = ParseValueExpression(definition, this);
                _resolved[name] = parsed;
                return parsed;
            }

            return _parent!.Resolve(name);
        }
    }
}
