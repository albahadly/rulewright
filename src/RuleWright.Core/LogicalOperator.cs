namespace RuleWright.Core;

/// <summary>
/// Logical combinator applied by a <see cref="ConditionGroup"/> to its children.
/// </summary>
public enum LogicalOperator
{
    /// <summary>All child conditions must pass. Short-circuits left to right.</summary>
    And,

    /// <summary>At least one child condition must pass. Short-circuits left to right.</summary>
    Or,

    /// <summary>Negates a single child condition.</summary>
    Not,
}
