namespace RuleWright.Core;

/// <summary>
/// A node in a rule's condition tree: either a <see cref="ConditionGroup"/>
/// (logical combinator) or a <see cref="ConditionLeaf"/> (field comparison).
/// Immutable after construction.
/// </summary>
public abstract class ConditionNode
{
    private protected ConditionNode()
    {
    }
}
