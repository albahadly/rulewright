using System;
using System.Collections.Generic;
using System.Linq;

namespace RuleWright.Core;

/// <summary>
/// A logical grouping of child conditions combined with <see cref="LogicalOperator"/>.
/// <see cref="LogicalOperator.Not"/> groups must contain exactly one child.
/// </summary>
public sealed class ConditionGroup : ConditionNode
{
    /// <summary>
    /// Creates a condition group.
    /// </summary>
    /// <param name="operator">The logical combinator.</param>
    /// <param name="children">The child conditions; at least one, exactly one for <see cref="LogicalOperator.Not"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="children"/> is null or contains null.</exception>
    /// <exception cref="ArgumentException"><paramref name="children"/> is empty, or a NOT group does not have exactly one child.</exception>
    public ConditionGroup(LogicalOperator @operator, IEnumerable<ConditionNode> children)
    {
        if (children is null)
        {
            throw new ArgumentNullException(nameof(children));
        }

        ConditionNode[] materialized = children.ToArray();
        if (materialized.Length == 0)
        {
            throw new ArgumentException("A condition group must have at least one child.", nameof(children));
        }

        if (materialized.Any(c => c is null))
        {
            throw new ArgumentNullException(nameof(children), "Child conditions must not be null.");
        }

        if (@operator == LogicalOperator.Not && materialized.Length != 1)
        {
            throw new ArgumentException("A NOT group must have exactly one child.", nameof(children));
        }

        Operator = @operator;
        Children = materialized;
    }

    /// <summary>The logical combinator applied to <see cref="Children"/>.</summary>
    public LogicalOperator Operator { get; }

    /// <summary>The child conditions, in document order.</summary>
    public IReadOnlyList<ConditionNode> Children { get; }
}
