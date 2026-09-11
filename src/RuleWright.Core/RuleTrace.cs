using System;

namespace RuleWright.Core;

/// <summary>
/// The trace of a single rule within an evaluation: whether it fired, whether it
/// was skipped, and the per-node condition results.
/// </summary>
public sealed class RuleTrace
{
    /// <summary>
    /// Creates a rule trace entry.
    /// </summary>
    /// <param name="ruleId">The rule's id.</param>
    /// <param name="fired">Whether the rule's condition passed.</param>
    /// <param name="skipped">
    /// Whether the rule was never evaluated. Prefer the <see cref="RuleSkipReason"/> overload,
    /// which also records <em>why</em>; this one reports a skip as
    /// <see cref="RuleSkipReason.Disabled"/>.
    /// </param>
    /// <param name="condition">The condition trace tree, or null when the rule was skipped.</param>
    /// <exception cref="ArgumentException"><paramref name="ruleId"/> is null or empty.</exception>
    public RuleTrace(string ruleId, bool fired, bool skipped, ConditionTraceNode? condition)
        : this(ruleId, fired, skipped ? RuleSkipReason.Disabled : RuleSkipReason.None, condition)
    {
    }

    /// <summary>
    /// Creates a rule trace entry, recording why the rule was skipped when it was.
    /// </summary>
    /// <param name="ruleId">The rule's id.</param>
    /// <param name="fired">Whether the rule's condition passed.</param>
    /// <param name="skipReason">Why the rule was never evaluated, or <see cref="RuleSkipReason.None"/>.</param>
    /// <param name="condition">The condition trace tree, or null when the rule was skipped.</param>
    /// <exception cref="ArgumentException"><paramref name="ruleId"/> is null or empty.</exception>
    public RuleTrace(string ruleId, bool fired, RuleSkipReason skipReason, ConditionTraceNode? condition)
    {
        if (string.IsNullOrEmpty(ruleId))
        {
            throw new ArgumentException("Rule id must not be null or empty.", nameof(ruleId));
        }

        RuleId = ruleId;
        Fired = fired;
        SkipReason = skipReason;
        Condition = condition;
    }

    /// <summary>The rule's id.</summary>
    public string RuleId { get; }

    /// <summary>Whether the rule's condition passed.</summary>
    public bool Fired { get; }

    /// <summary>Whether the rule was never evaluated; see <see cref="SkipReason"/> for why.</summary>
    public bool Skipped => SkipReason != RuleSkipReason.None;

    /// <summary>
    /// Why the rule was never evaluated — <see cref="RuleSkipReason.Disabled"/> (the author turned
    /// it off) or <see cref="RuleSkipReason.StoppedAfterMatch"/> (evaluation had already
    /// finished) — or <see cref="RuleSkipReason.None"/> when it was evaluated.
    /// </summary>
    public RuleSkipReason SkipReason { get; }

    /// <summary>The condition trace tree, or null when the rule was skipped.</summary>
    public ConditionTraceNode? Condition { get; }
}
