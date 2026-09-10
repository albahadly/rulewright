using System.Collections.Generic;
using Rulewright.Core;

namespace Rulewright.Execution;

/// <summary>
/// Applies a single action's already-computed value into the running outputs dictionary,
/// according to the action type. Shared by the compiled and interpreted paths so
/// accumulation semantics are identical regardless of fact shape. Actions apply in priority
/// order across fired rules, so <c>addToOutput</c> and <c>appendToOutput</c> accumulate over
/// the whole evaluation and <c>removeOutput</c> can undo what an earlier rule wrote.
/// </summary>
internal static class OutputApplier
{
    private static readonly object DecimalZero = 0m;

    /// <summary>
    /// Whether an action is a constant <c>setOutput</c> — the only kind whose contribution
    /// is independent of what other rules write, so a rule made entirely of them can reuse a
    /// single pre-materialized outputs dictionary instead of applying per evaluation.
    /// </summary>
    internal static bool IsLiteralSet(RuleAction action)
        => action.Type == RuleAction.SetOutputType && action.Value is LiteralExpression;

    /// <summary>
    /// Applies <paramref name="value"/> to <paramref name="target"/> in the running outputs,
    /// then records the resulting value into <paramref name="snapshot"/> (the firing rule's
    /// own view of what it wrote).
    /// </summary>
    internal static void Apply(
        IDictionary<string, object?> running,
        Dictionary<string, object?> snapshot,
        string type,
        string target,
        object? value)
    {
        switch (type)
        {
            case RuleAction.AddToOutputType:
                // Only numeric contributions accumulate, and only onto a numeric (or absent)
                // running total. A null or non-numeric value contributes nothing, and a target
                // already holding a non-numeric value is left exactly as it is — either way one
                // stray rule cannot wipe what another wrote.
                if (value is not null && ValueConverter.IsNumericValue(value))
                {
                    running.TryGetValue(target, out object? existing);
                    if (existing is null || ValueConverter.IsNumericValue(existing))
                    {
                        running[target] = ValueExpressionOps.Add(existing ?? DecimalZero, value);
                    }
                }

                break;

            case RuleAction.AppendToOutputType:
                // A null value contributes nothing to a collection. Copy-on-append keeps each
                // rule's snapshot list immutable as later rules extend the collection.
                if (value is not null)
                {
                    running.TryGetValue(target, out object? current);
                    var list = current is List<object?> existingList
                        ? new List<object?>(existingList)
                        : new List<object?>();
                    list.Add(value);
                    running[target] = list;
                }

                break;

            case RuleAction.RemoveOutputType:
                // Delete the key (if present) from both the running outputs and this rule's own
                // snapshot, so the rule's FiredRule.Outputs never reports a value it just removed.
                running.Remove(target);
                snapshot.Remove(target);
                return;

            default: // setOutput
                running[target] = value;
                break;
        }

        running.TryGetValue(target, out object? applied);
        snapshot[target] = applied;
    }
}
