using System.Collections.Generic;
using Rulewright.Core;

namespace Rulewright.Execution;

/// <summary>
/// Measures a condition tree into a pre-order layout: entry <c>i</c> is the number of nodes in the
/// subtree rooted at pre-order position <c>i</c>, so a node at position <c>i</c> has its first child
/// at <c>i + 1</c> and each later sibling at the previous sibling's position plus that sibling's
/// size. The compiled traced delegate, the interpreter, and the trace builder all walk the tree in
/// that same pre-order and derive each node's result slot from its position, so per-node pass/fail
/// results line up across execution modes.
///
/// <para>Positions, not node identity: the domain model is immutable, so one
/// <see cref="ConditionNode"/> instance may legitimately appear at several positions in a tree, and
/// each occurrence needs its own slot.</para>
/// </summary>
internal static class ConditionNodeIndexer
{
    /// <summary>Builds the pre-order subtree-size layout; its length is the node count.</summary>
    internal static int[] BuildLayout(ConditionNode root)
    {
        var sizes = new List<int>();
        Measure(root, sizes);
        return sizes.ToArray();
    }

    /// <summary>Records this node's subtree size at its own position and returns it.</summary>
    private static int Measure(ConditionNode node, List<int> sizes)
    {
        int position = sizes.Count;
        sizes.Add(0); // Reserved: the size is only known once the children are measured.

        int size = 1;
        if (node is ConditionGroup group)
        {
            foreach (ConditionNode child in group.Children)
            {
                size += Measure(child, sizes);
            }
        }

        sizes[position] = size;
        return size;
    }
}
