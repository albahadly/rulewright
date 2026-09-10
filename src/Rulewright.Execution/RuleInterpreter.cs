using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using Rulewright.Core;

namespace Rulewright.Execution;

/// <summary>
/// The dynamic-fact fallback: walks the condition tree against dictionary facts
/// (<c>IDictionary&lt;string, object&gt;</c>, nested dictionaries, or POCOs reached via
/// cached reflection). Slower than compiled delegates by design — results flag it via
/// <see cref="CompilationMode.Interpreted"/> so the degradation is visible, never silent.
/// </summary>
internal static class RuleInterpreter
{
    // Keyed by pattern *and* timeout: two engines can be configured with different bounds and
    // must not share a Regex whose timeout was baked in for the other.
    private static readonly ConcurrentDictionary<(string Pattern, TimeSpan Timeout), Regex> RegexCache =
        new ConcurrentDictionary<(string, TimeSpan), Regex>();

    private static readonly ConcurrentDictionary<(Type Type, string Name), MemberInfo?> MemberCache =
        new ConcurrentDictionary<(Type, string), MemberInfo?>();

    /// <summary>
    /// Evaluates one condition node. <c>index</c> is the node's pre-order position, which is also
    /// its slot in <c>results</c>: children start at <c>index + 1</c> and each later sibling follows
    /// the previous one by that sibling's subtree size, read from <c>layout</c>. Positions rather
    /// than node identity, so one node instance reused at several positions gets a slot per
    /// position.
    /// </summary>
    internal static bool Evaluate(
        ConditionNode node,
        int index,
        object fact,
        IReadOnlyDictionary<string, IRuleFunction> functions,
        TimeSpan regexTimeout,
        bool?[]? results,
        int[] layout)
    {
        bool outcome;
        if (node is ConditionGroup group)
        {
            switch (group.Operator)
            {
                case LogicalOperator.And:
                {
                    outcome = true;
                    int childIndex = index + 1;
                    foreach (ConditionNode child in group.Children)
                    {
                        if (!Evaluate(child, childIndex, fact, functions, regexTimeout, results, layout))
                        {
                            outcome = false;
                            break;
                        }

                        childIndex += layout[childIndex];
                    }

                    break;
                }

                case LogicalOperator.Or:
                {
                    outcome = false;
                    int childIndex = index + 1;
                    foreach (ConditionNode child in group.Children)
                    {
                        if (Evaluate(child, childIndex, fact, functions, regexTimeout, results, layout))
                        {
                            outcome = true;
                            break;
                        }

                        childIndex += layout[childIndex];
                    }

                    break;
                }

                default:
                    outcome = !Evaluate(group.Children[0], index + 1, fact, functions, regexTimeout, results, layout);
                    break;
            }
        }
        else
        {
            outcome = EvaluateLeaf((ConditionLeaf)node, fact, functions, regexTimeout);
        }

        if (results is not null)
        {
            results[index] = outcome;
        }

        return outcome;
    }

    private static bool EvaluateLeaf(
        ConditionLeaf leaf,
        object fact,
        IReadOnlyDictionary<string, IRuleFunction> functions,
        TimeSpan regexTimeout)
    {
        object? fieldValue = leaf.Left is not null
            ? ActionExpressionInterpreter.EvaluateValue(leaf.Left, fact)
            : leaf.Field is null ? fact : ResolvePath(fact, leaf.Field);

        return ApplyOperator(leaf, fieldValue, functions, regexTimeout);
    }

    /// <summary>
    /// Applies a leaf's operator to an already-resolved left-hand value. Shared by the
    /// interpreter and by the compiled path's computed-left-hand-side leaves, so a field
    /// leaf and an expression leaf with the same value compare identically.
    /// </summary>
    internal static bool ApplyOperator(
        ConditionLeaf leaf,
        object? fieldValue,
        IReadOnlyDictionary<string, IRuleFunction> functions,
        TimeSpan regexTimeout)
    {
        switch (leaf.Operator)
        {
            case ConditionOperator.IsNull:
                return fieldValue is null;

            case ConditionOperator.IsNotNull:
                return fieldValue is not null;

            case ConditionOperator.Custom:
                return functions[leaf.FunctionName!].Evaluate(fieldValue, leaf.Value);

            case ConditionOperator.Equal:
                return RuntimeComparisons.AreEqual(fieldValue, leaf.Value);

            case ConditionOperator.NotEqual:
                return !RuntimeComparisons.AreEqual(fieldValue, leaf.Value);

            case ConditionOperator.GreaterThan:
                return RuntimeComparisons.TryCompare(fieldValue, leaf.Value) > 0;

            case ConditionOperator.GreaterThanOrEqual:
                return RuntimeComparisons.TryCompare(fieldValue, leaf.Value) >= 0;

            case ConditionOperator.LessThan:
                return RuntimeComparisons.TryCompare(fieldValue, leaf.Value) < 0;

            case ConditionOperator.LessThanOrEqual:
                return RuntimeComparisons.TryCompare(fieldValue, leaf.Value) <= 0;

            case ConditionOperator.Contains:
                return fieldValue is string containsText && containsText.Contains((string)leaf.Value!);

            case ConditionOperator.StartsWith:
                return fieldValue is string startsText
                    && startsText.StartsWith((string)leaf.Value!, StringComparison.Ordinal);

            case ConditionOperator.EndsWith:
                return fieldValue is string endsText
                    && endsText.EndsWith((string)leaf.Value!, StringComparison.Ordinal);

            case ConditionOperator.MatchesRegex:
                return fieldValue is string regexText && GetRegex((string)leaf.Value!, regexTimeout).IsMatch(regexText);

            case ConditionOperator.In:
                return IsInSet(fieldValue, (object?[])leaf.Value!);

            default: // NotIn
                return !IsInSet(fieldValue, (object?[])leaf.Value!);
        }
    }

    /// <summary>
    /// Set membership, matching the compiled path's typed <c>HashSet</c> exactly: a null in the set
    /// contributes nothing. Null is a field's absence, not a member — so a null field is in no set,
    /// which is what the documented null semantics say (<c>In</c> false, <c>NotIn</c> true) and what
    /// the compiled path already does by dropping nulls when it builds the set.
    /// </summary>
    private static bool IsInSet(object? fieldValue, object?[] items)
    {
        foreach (object? item in items)
        {
            if (item is not null && RuntimeComparisons.AreEqual(fieldValue, item))
            {
                return true;
            }
        }

        return false;
    }

    internal static object? ResolvePath(object fact, string path)
    {
        object? current = fact;
        foreach (string segment in path.Split('.'))
        {
            if (current is null)
            {
                return null;
            }

            current = ResolveSegment(current, segment);
        }

        return current;
    }

    private static object? ResolveSegment(object current, string name)
    {
        // Missing dictionary keys resolve to null (the operator's null semantics then
        // apply), because dynamic facts have no compile-time shape to validate against.
        if (current is IDictionary<string, object?> generic)
        {
            return generic.TryGetValue(name, out object? value) ? value : null;
        }

        if (current is System.Collections.IDictionary nonGeneric)
        {
            return nonGeneric.Contains(name) ? nonGeneric[name] : null;
        }

        MemberInfo? member = MemberCache.GetOrAdd((current.GetType(), name), FindMember);
        return member switch
        {
            PropertyInfo property => property.GetValue(current),
            FieldInfo field => field.GetValue(current),
            _ => null,
        };
    }

    private static MemberInfo? FindMember((Type Type, string Name) key)
    {
        const BindingFlags exact = BindingFlags.Instance | BindingFlags.Public;
        const BindingFlags relaxed = exact | BindingFlags.IgnoreCase;
        return (MemberInfo?)key.Type.GetProperty(key.Name, exact)
            ?? (MemberInfo?)key.Type.GetField(key.Name, exact)
            ?? (MemberInfo?)key.Type.GetProperty(key.Name, relaxed)
            ?? key.Type.GetField(key.Name, relaxed);
    }

    private static Regex GetRegex(string pattern, TimeSpan timeout)
        => RegexCache.GetOrAdd((pattern, timeout), key => new Regex(key.Pattern, RegexOptions.Compiled, key.Timeout));
}
