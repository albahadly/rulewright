namespace Rulewright.Core;

/// <summary>
/// Per-evaluation options. Immutable once constructed, so the shared
/// <see cref="Default"/> instance cannot be reconfigured out from under other callers —
/// set values with an object initializer (<c>new EvaluationOptions { EnableTrace = true }</c>).
/// </summary>
public sealed class EvaluationOptions
{
    /// <summary>The default options: tracing off, evaluate all rules.</summary>
    public static EvaluationOptions Default { get; } = new EvaluationOptions();

    /// <summary>
    /// When true, the result carries an <see cref="EvaluationTrace"/> recording which
    /// rules fired and which condition nodes passed or failed. Off by default; the
    /// untraced fast path has no tracing overhead.
    /// </summary>
    public bool EnableTrace { get; init; }

    /// <summary>
    /// When true, evaluation stops after the first rule whose condition passes;
    /// remaining rules are reported in the trace with
    /// <see cref="RuleSkipReason.StoppedAfterMatch"/>.
    /// </summary>
    public bool StopOnFirstMatch { get; init; }
}
