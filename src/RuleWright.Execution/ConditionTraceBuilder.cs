using RuleWright.Core;

namespace RuleWright.Execution;

/// <summary>
/// Materializes a <see cref="ConditionTraceNode"/> tree from the per-node pass/fail
/// results recorded during a traced evaluation. Nodes never reached because of
/// short-circuiting keep a null result slot and report <c>Passed == null</c>.
/// </summary>
internal static class ConditionTraceBuilder
{
    internal static ConditionTraceNode Build(
        ConditionNode node,
        int index,
        int[] layout,
        bool?[] results)
    {
        ConditionTraceNode[]? children = null;
        if (node is ConditionGroup group)
        {
            children = new ConditionTraceNode[group.Children.Count];
            int childIndex = index + 1;
            for (int i = 0; i < group.Children.Count; i++)
            {
                children[i] = Build(group.Children[i], childIndex, layout, results);
                childIndex += layout[childIndex];
            }
        }

        return new ConditionTraceNode(ConditionDescriber.Describe(node), results[index], children);
    }
}
