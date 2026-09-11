using System;
using System.Collections.Generic;

namespace RuleWright.Core;

/// <summary>
/// A rule that contributed outputs during an evaluation — either because its condition
/// passed (<see cref="RuleBranch.Then"/>) or because it did not and the rule has an
/// <c>else</c> branch (<see cref="RuleBranch.Else"/>) — with the outputs that branch produced.
/// </summary>
public sealed class FiredRule
{
    /// <summary>
    /// Creates a fired-rule record.
    /// </summary>
    /// <param name="ruleId">The id of the rule that fired.</param>
    /// <param name="outputs">The outputs produced by the rule's actions (target → value).</param>
    /// <param name="branch">Which branch ran — the condition's <c>actions</c> or its <c>else</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="ruleId"/> is null or empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="outputs"/> is null.</exception>
    public FiredRule(string ruleId, IReadOnlyDictionary<string, object?> outputs, RuleBranch branch = RuleBranch.Then)
    {
        if (string.IsNullOrEmpty(ruleId))
        {
            throw new ArgumentException("Rule id must not be null or empty.", nameof(ruleId));
        }

        RuleId = ruleId;
        Outputs = outputs ?? throw new ArgumentNullException(nameof(outputs));
        Branch = branch;
    }

    /// <summary>The id of the rule that fired.</summary>
    public string RuleId { get; }

    /// <summary>The outputs produced by the rule's actions (target → value).</summary>
    public IReadOnlyDictionary<string, object?> Outputs { get; }

    /// <summary>
    /// Whether the rule fired its <c>actions</c> (condition matched) or its <c>else</c>
    /// actions (condition did not match).
    /// </summary>
    public RuleBranch Branch { get; }
}
