using System;
using System.Collections.Generic;

namespace RuleWright.Core;

/// <summary>
/// The traced outcome of one condition node. Mirrors the shape of the rule's
/// condition tree; nodes that were never reached because of short-circuiting
/// report a null <see cref="Passed"/>.
/// </summary>
public sealed class ConditionTraceNode
{
    /// <summary>
    /// Creates a condition trace node.
    /// </summary>
    /// <param name="description">
    /// Human-readable description of the node, e.g. <c>"Customer.Age GreaterThan 18"</c>
    /// for a leaf or <c>"AND"</c> for a group.
    /// </param>
    /// <param name="passed">Whether the node passed, or null if it was never evaluated (short-circuited).</param>
    /// <param name="children">Child node traces for groups; empty for leaves.</param>
    /// <exception cref="ArgumentException"><paramref name="description"/> is null or empty.</exception>
    public ConditionTraceNode(string description, bool? passed, IReadOnlyList<ConditionTraceNode>? children = null)
    {
        if (string.IsNullOrEmpty(description))
        {
            throw new ArgumentException("Description must not be null or empty.", nameof(description));
        }

        Description = description;
        Passed = passed;
        Children = children ?? Array.Empty<ConditionTraceNode>();
    }

    /// <summary>Human-readable description of the node.</summary>
    public string Description { get; }

    /// <summary>Whether the node passed, or null if short-circuiting skipped it.</summary>
    public bool? Passed { get; }

    /// <summary>Child node traces for groups; empty for leaves.</summary>
    public IReadOnlyList<ConditionTraceNode> Children { get; }
}
