namespace RuleWright.Core;

/// <summary>
/// Why a rule contributed nothing to an evaluation. A skipped rule is never evaluated at all, so
/// its <see cref="RuleTrace.Condition"/> is null — this says which of the two very different
/// reasons applies, so a trace reader can tell "the author turned this off" from "evaluation had
/// already finished".
/// </summary>
public enum RuleSkipReason
{
    /// <summary>The rule was evaluated; it was not skipped.</summary>
    None,

    /// <summary>The rule carries <c>"enabled": false</c>.</summary>
    Disabled,

    /// <summary>
    /// An earlier rule matched and evaluation stopped — either
    /// <see cref="EvaluationOptions.StopOnFirstMatch"/> or a <c>first</c>-hit-policy rule set.
    /// </summary>
    StoppedAfterMatch,
}
