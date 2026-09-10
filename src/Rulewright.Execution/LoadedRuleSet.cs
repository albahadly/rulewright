using System;
using System.Collections.Generic;
using Rulewright.Core;

namespace Rulewright.Execution;

/// <summary>
/// A rule set that has been parsed, validated, and prepared for execution by a
/// <see cref="RulewrightEngine"/>: rules are pre-sorted by priority, per-rule content
/// hashes are computed for the compiled-delegate cache, action outputs are
/// prematerialized, and condition nodes are pre-indexed for tracing. Compilation to
/// delegates happens lazily per fact type on first evaluation and is then cached.
/// </summary>
public sealed class LoadedRuleSet
{
    internal LoadedRuleSet(RuleSet ruleSet, RuleEntry[] orderedRules)
    {
        RuleSet = ruleSet;
        OrderedRules = orderedRules;
    }

    /// <summary>The underlying immutable rule set.</summary>
    public RuleSet RuleSet { get; }

    internal RuleEntry[] OrderedRules { get; }
}

/// <summary>
/// Per-rule execution metadata prepared once at load time.
/// </summary>
internal sealed class RuleEntry
{
    internal RuleEntry(
        Rule rule,
        string hash,
        IReadOnlyDictionary<string, object?> outputs,
        bool hasComplexOutputs,
        IReadOnlyDictionary<string, object?> elseOutputs,
        bool hasComplexElseOutputs,
        int[] nodeLayout)
    {
        Rule = rule;
        Hash = hash;
        Outputs = outputs;
        HasComplexOutputs = hasComplexOutputs;
        ElseOutputs = elseOutputs;
        HasComplexElseOutputs = hasComplexElseOutputs;
        NodeLayout = nodeLayout;
    }

    internal Rule Rule { get; }

    /// <summary>Content hash of the rule's condition and actions — the compiled-delegate cache key.</summary>
    internal string Hash { get; }

    /// <summary>
    /// The outputs this rule produces when its condition matches, shared and reused across
    /// evaluations. Populated only when <see cref="HasComplexOutputs"/> is false — i.e. every
    /// action is a constant <c>setOutput</c>; otherwise outputs are applied to the running
    /// result per evaluation instead.
    /// </summary>
    internal IReadOnlyDictionary<string, object?> Outputs { get; }

    /// <summary>
    /// Whether any action is not a constant <c>setOutput</c> (a computed value, an accumulating
    /// <c>addToOutput</c>/<c>appendToOutput</c>, or a <c>removeOutput</c>), so its output must
    /// be applied to the running result per evaluation rather than reused from
    /// <see cref="Outputs"/>.
    /// </summary>
    internal bool HasComplexOutputs { get; }

    /// <summary>The <see cref="Outputs"/> equivalent for the rule's <c>else</c> branch.</summary>
    internal IReadOnlyDictionary<string, object?> ElseOutputs { get; }

    /// <summary>The <see cref="HasComplexOutputs"/> equivalent for the rule's <c>else</c> branch.</summary>
    internal bool HasComplexElseOutputs { get; }

    /// <summary>Whether the rule has any <c>else</c> actions to run when its condition fails.</summary>
    internal bool HasElse => Rule.ElseActions.Count > 0;

    /// <summary>
    /// Pre-order subtree sizes for the condition tree, shared by the compiler, the interpreter, and
    /// the trace builder so per-node results line up across execution modes. Its length is the node
    /// count. See <see cref="ConditionNodeIndexer"/>.
    /// </summary>
    internal int[] NodeLayout { get; }
}
