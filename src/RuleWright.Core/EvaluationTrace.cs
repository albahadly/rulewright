using System;
using System.Collections.Generic;

namespace RuleWright.Core;

/// <summary>
/// A full execution trace: one entry per rule in the set, in evaluation order.
/// Produced only when <see cref="EvaluationOptions.EnableTrace"/> is true.
/// </summary>
public sealed class EvaluationTrace
{
    /// <summary>
    /// Creates an evaluation trace.
    /// </summary>
    /// <param name="rules">One trace entry per rule, in evaluation order.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rules"/> is null.</exception>
    public EvaluationTrace(IReadOnlyList<RuleTrace> rules)
    {
        Rules = rules ?? throw new ArgumentNullException(nameof(rules));
    }

    /// <summary>One trace entry per rule, in evaluation order.</summary>
    public IReadOnlyList<RuleTrace> Rules { get; }
}
