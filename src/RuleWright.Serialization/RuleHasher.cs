using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using RuleWright.Core;

namespace RuleWright.Serialization;

/// <summary>
/// Computes a stable content hash for a rule, used as the compiled-delegate cache key.
/// The hash covers exactly the compilation-relevant content — the condition tree and
/// actions — rendered in a canonical form (sorted keys, invariant number formatting).
/// It is therefore insensitive to JSON whitespace, key order, and the <c>layout</c>,
/// <c>id</c>, <c>description</c>, <c>priority</c>, and <c>enabled</c> members, none of
/// which change the compiled delegate; any change to condition or action content
/// changes the hash and invalidates the cache entry.
/// </summary>
public static class RuleHasher
{
    /// <summary>
    /// Computes the rule's content hash.
    /// </summary>
    /// <param name="rule">The rule.</param>
    /// <returns>A lowercase-hex SHA-256 of the canonical form.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rule"/> is null.</exception>
    public static string ComputeHash(Rule rule)
    {
        string canonical = GetCanonicalForm(rule);
        using (SHA256 sha = SHA256.Create())
        {
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical));
            var builder = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash)
            {
                builder.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }
    }

    /// <summary>
    /// Renders the rule's compilation-relevant content (condition and actions) as a
    /// canonical JSON string: fixed key order, invariant number formatting, no whitespace.
    /// Exposed for testability and diagnostics.
    /// </summary>
    /// <param name="rule">The rule.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rule"/> is null.</exception>
    public static string GetCanonicalForm(Rule rule)
    {
        if (rule is null)
        {
            throw new ArgumentNullException(nameof(rule));
        }

        var builder = new StringBuilder(256);
        builder.Append("{\"actions\":");
        AppendActions(builder, rule.Actions);
        builder.Append(",\"condition\":");
        AppendCondition(builder, rule.Condition);

        // Else actions change the compiled delegate, so they are part of the content hash.
        // Omitted when absent, so rules without an else keep their existing hash.
        if (rule.ElseActions.Count > 0)
        {
            builder.Append(",\"else\":");
            AppendActions(builder, rule.ElseActions);
        }

        builder.Append('}');
        return builder.ToString();
    }

    private static void AppendActions(StringBuilder builder, IReadOnlyList<RuleAction> actions)
    {
        builder.Append('[');
        for (int i = 0; i < actions.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            RuleAction action = actions[i];
            builder.Append("{\"target\":");
            AppendString(builder, action.Target);
            builder.Append(",\"type\":");
            AppendString(builder, action.Type);

            // removeOutput ignores its value, so the value plays no part in its identity.
            if (action.Type != RuleAction.RemoveOutputType)
            {
                builder.Append(",\"value\":");
                AppendExpression(builder, action.Value);
            }

            builder.Append('}');
        }

        builder.Append(']');
    }

    private static void AppendExpression(StringBuilder builder, ValueExpression expression)
    {
        switch (expression)
        {
            case LiteralExpression literal:
                // A literal renders as its bare scalar, so `5` and `{ "literal": 5 }`
                // (which parse to the same LiteralExpression) hash identically.
                AppendValue(builder, literal.Value);
                break;

            case FieldExpression field:
                builder.Append("{\"field\":");
                AppendString(builder, field.Path);
                builder.Append('}');
                break;

            case OperatorExpression op:
                builder.Append("{\"op\":");
                AppendString(builder, ExpressionOperatorMap.ToJsonName(op.Operator));
                builder.Append(",\"operands\":[");
                for (int i = 0; i < op.Operands.Count; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(',');
                    }

                    AppendExpression(builder, op.Operands[i]);
                }

                builder.Append("]}");
                break;
        }
    }

    private static void AppendCondition(StringBuilder builder, ConditionNode node)
    {
        if (node is ConditionGroup group)
        {
            builder.Append("{\"operator\":");
            AppendString(builder, group.Operator switch
            {
                LogicalOperator.And => "AND",
                LogicalOperator.Or => "OR",
                _ => "NOT",
            });
            builder.Append(",\"rules\":[");
            for (int i = 0; i < group.Children.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                AppendCondition(builder, group.Children[i]);
            }

            builder.Append("],\"type\":\"group\"}");
            return;
        }

        var leaf = (ConditionLeaf)node;
        builder.Append('{');
        bool first = true;
        if (leaf.Left is not null)
        {
            builder.Append("\"expression\":");
            AppendExpression(builder, leaf.Left);
            first = false;
        }
        else if (leaf.Field is not null)
        {
            builder.Append("\"field\":");
            AppendString(builder, leaf.Field);
            first = false;
        }

        if (leaf.FunctionName is not null)
        {
            if (!first)
            {
                builder.Append(',');
            }

            builder.Append("\"name\":");
            AppendString(builder, leaf.FunctionName);
            first = false;
        }

        if (!first)
        {
            builder.Append(',');
        }

        builder.Append("\"operator\":");
        AppendString(builder, OperatorMap.ToJsonName(leaf.Operator));

        if (leaf.ElementCondition is not null)
        {
            // The per-element condition is the whole meaning of a quantifier, so it has to be part
            // of the content hash — otherwise two quantifiers over the same field would share a
            // compiled delegate.
            builder.Append(",\"condition\":");
            AppendCondition(builder, leaf.ElementCondition);
        }
        else if (leaf.Operator is not (ConditionOperator.IsNull or ConditionOperator.IsNotNull))
        {
            builder.Append(",\"value\":");
            AppendValue(builder, leaf.Value);
        }

        builder.Append('}');
    }

    private static void AppendValue(StringBuilder builder, object? value)
    {
        switch (value)
        {
            case null:
                builder.Append("null");
                break;
            case bool b:
                builder.Append(b ? "true" : "false");
                break;
            case string s:
                AppendString(builder, s);
                break;
            case sbyte or byte or short or ushort or int or uint or long:
                builder.Append(Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
                break;
            case ulong ul:
                builder.Append(ul.ToString(CultureInfo.InvariantCulture));
                break;
            case float f:
                AppendBinaryFloating(builder, f);
                break;
            case double d:
                AppendBinaryFloating(builder, d);
                break;
            case decimal m:
                // G29 drops trailing zeros so 10.50 and 10.5 hash identically.
                builder.Append(m.ToString("G29", CultureInfo.InvariantCulture));
                break;
            case object?[] array:
                builder.Append('[');
                for (int i = 0; i < array.Length; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(',');
                    }

                    AppendValue(builder, array[i]);
                }

                builder.Append(']');
                break;
            default:
                // Uncommon hand-built domain values (e.g. DateTime): tag with the type
                // name so distinct types with identical text never collide.
                AppendString(
                    builder,
                    value.GetType().FullName + ":" + Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }
    }

    /// <summary>
    /// Renders a <see cref="double"/>/<see cref="float"/> with a <c>d</c> suffix. Binary
    /// floating-point literals make arithmetic run in <c>double</c> where integral and
    /// <see cref="decimal"/> literals make it run in <c>decimal</c> (1/3 differs between the
    /// two), so they must not share a canonical form — and therefore a compiled-delegate cache
    /// entry — with a numerically equal literal of another kind. JSON numbers parse to
    /// long/decimal whenever representable, so this only tags values that genuinely behave
    /// differently.
    /// </summary>
    private static void AppendBinaryFloating(StringBuilder builder, double value)
        => builder.Append(value.ToString("R", CultureInfo.InvariantCulture)).Append('d');

    private static void AppendString(StringBuilder builder, string value)
    {
        builder.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (c < ' ')
                    {
                        builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        builder.Append('"');
    }
}
