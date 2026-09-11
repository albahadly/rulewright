using System;
using System.Collections.Generic;
using System.Linq;

namespace RuleWright.Core;

/// <summary>
/// A single business rule: a condition tree plus the actions applied when it passes.
/// Immutable after construction; parsed once from JSON and compiled once per fact type.
/// </summary>
public sealed class Rule
{
    /// <summary>
    /// Creates a rule.
    /// </summary>
    /// <param name="id">Unique identifier within the rule set.</param>
    /// <param name="condition">The root of the condition tree.</param>
    /// <param name="actions">Actions applied when the condition passes; may be empty.</param>
    /// <param name="description">Optional human-readable summary.</param>
    /// <param name="priority">Higher-priority rules are evaluated first; ties keep document order.</param>
    /// <param name="enabled">Disabled rules are skipped entirely during evaluation.</param>
    /// <param name="elseActions">Actions applied when the condition does <em>not</em> pass; may be empty.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is null or empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="condition"/> is null, or <paramref name="actions"/>/<paramref name="elseActions"/> contains null.</exception>
    public Rule(
        string id,
        ConditionNode condition,
        IEnumerable<RuleAction>? actions = null,
        string? description = null,
        int priority = 0,
        bool enabled = true,
        IEnumerable<RuleAction>? elseActions = null)
    {
        if (string.IsNullOrEmpty(id))
        {
            throw new ArgumentException("Rule id must not be null or empty.", nameof(id));
        }

        RuleAction[] materializedActions = actions?.ToArray() ?? Array.Empty<RuleAction>();
        if (materializedActions.Any(a => a is null))
        {
            throw new ArgumentNullException(nameof(actions), "Actions must not contain null.");
        }

        RuleAction[] materializedElseActions = elseActions?.ToArray() ?? Array.Empty<RuleAction>();
        if (materializedElseActions.Any(a => a is null))
        {
            throw new ArgumentNullException(nameof(elseActions), "Else actions must not contain null.");
        }

        Id = id;
        Condition = condition ?? throw new ArgumentNullException(nameof(condition));
        Actions = materializedActions;
        Description = description;
        Priority = priority;
        Enabled = enabled;
        ElseActions = materializedElseActions;
    }

    /// <summary>Unique identifier within the rule set.</summary>
    public string Id { get; }

    /// <summary>Optional human-readable summary.</summary>
    public string? Description { get; }

    /// <summary>Higher-priority rules are evaluated first; ties keep document order.</summary>
    public int Priority { get; }

    /// <summary>Whether the rule participates in evaluation.</summary>
    public bool Enabled { get; }

    /// <summary>The root of the condition tree.</summary>
    public ConditionNode Condition { get; }

    /// <summary>Actions applied when the condition passes, in document order.</summary>
    public IReadOnlyList<RuleAction> Actions { get; }

    /// <summary>
    /// Actions applied when the condition does not pass, in document order. Empty when the
    /// rule has no <c>else</c> branch, in which case a non-matching rule contributes nothing.
    /// </summary>
    public IReadOnlyList<RuleAction> ElseActions { get; }
}
