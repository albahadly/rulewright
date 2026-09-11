using System;

namespace RuleWright.Execution;

/// <summary>
/// The compiled form of one rule for one fact type: an untraced fast-path predicate,
/// a traced variant that records each condition node's outcome into a caller-supplied
/// slot array, and — for each branch that is not made purely of constant <c>setOutput</c>
/// actions — the ordered output steps that apply to the running outputs when that branch
/// fires. All are compiled once and cached; tracing therefore adds zero cost to untraced
/// evaluations, and pure constant-output branches carry no steps at all.
/// </summary>
internal sealed class CompiledRule<TFact>
{
    internal CompiledRule(
        Func<TFact, bool> predicate,
        Func<TFact, bool?[], bool> tracedPredicate,
        OutputStep<TFact>[]? outputSteps,
        OutputStep<TFact>[]? elseOutputSteps)
    {
        Predicate = predicate;
        TracedPredicate = tracedPredicate;
        OutputSteps = outputSteps;
        ElseOutputSteps = elseOutputSteps;
    }

    internal Func<TFact, bool> Predicate { get; }

    internal Func<TFact, bool?[], bool> TracedPredicate { get; }

    /// <summary>
    /// The rule's <c>actions</c> as ordered steps (action type, target, and a compiled delegate
    /// that computes the value from the fact), or null when every action is a constant
    /// <c>setOutput</c> (the engine then reuses the rule's pre-materialized outputs).
    /// </summary>
    internal OutputStep<TFact>[]? OutputSteps { get; }

    /// <summary>
    /// The rule's <c>else</c> actions as ordered steps, or null when the else branch is empty
    /// or made purely of constant <c>setOutput</c> actions.
    /// </summary>
    internal OutputStep<TFact>[]? ElseOutputSteps { get; }
}

/// <summary>
/// One action prepared for a specific fact type: how it combines (<see cref="Type"/>), where
/// it writes (<see cref="Target"/>), and a delegate computing its value from the fact.
/// </summary>
internal readonly struct OutputStep<TFact>
{
    internal OutputStep(string type, string target, Func<TFact, object?> valueFactory)
    {
        Type = type;
        Target = target;
        ValueFactory = valueFactory;
    }

    internal string Type { get; }

    internal string Target { get; }

    internal Func<TFact, object?> ValueFactory { get; }
}
