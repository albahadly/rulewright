using System;
using System.Collections.Generic;

namespace RuleWright.Core;

/// <summary>
/// The result of evaluating a rule set against one fact: which rules fired, the
/// merged outputs, how execution ran, and (when enabled) a full execution trace.
/// </summary>
public sealed class RuleEvaluationResult
{
    /// <summary>
    /// Creates an evaluation result.
    /// </summary>
    /// <param name="firedRules">The rules that fired, in evaluation order.</param>
    /// <param name="outputs">All action outputs merged across fired rules; later rules overwrite earlier ones on key collisions.</param>
    /// <param name="compilationMode">Whether execution used compiled delegates or the interpreter.</param>
    /// <param name="trace">The execution trace, or null when tracing was disabled.</param>
    /// <param name="failures">The authored failure messages of evaluated rules whose conditions did not pass; null means none.</param>
    /// <exception cref="ArgumentNullException"><paramref name="firedRules"/> or <paramref name="outputs"/> is null.</exception>
    public RuleEvaluationResult(
        IReadOnlyList<FiredRule> firedRules,
        IReadOnlyDictionary<string, object?> outputs,
        CompilationMode compilationMode,
        EvaluationTrace? trace = null,
        IReadOnlyList<RuleFailure>? failures = null)
    {
        FiredRules = firedRules ?? throw new ArgumentNullException(nameof(firedRules));
        Outputs = outputs ?? throw new ArgumentNullException(nameof(outputs));
        CompilationMode = compilationMode;
        Trace = trace;
        Failures = failures ?? Array.Empty<RuleFailure>();
    }

    /// <summary>The rules that fired, in evaluation order (descending priority, then document order).</summary>
    public IReadOnlyList<FiredRule> FiredRules { get; }

    /// <summary>All action outputs merged across fired rules; later rules overwrite earlier ones on key collisions.</summary>
    public IReadOnlyDictionary<string, object?> Outputs { get; }

    /// <summary>Whether execution used compiled delegates or the dynamic-fact interpreter.</summary>
    public CompilationMode CompilationMode { get; }

    /// <summary>The execution trace, or null when <see cref="EvaluationOptions.EnableTrace"/> was false.</summary>
    public EvaluationTrace? Trace { get; }

    /// <summary>
    /// The authored <c>failureMessage</c> of every rule that was evaluated and whose condition
    /// did not pass, in evaluation order. Rules without a <c>failureMessage</c>, rules that
    /// matched, and rules that were skipped (disabled, or unreached after a stop-on-first-match)
    /// contribute nothing. Empty when no evaluated rule with a message failed.
    /// </summary>
    public IReadOnlyList<RuleFailure> Failures { get; }
}
