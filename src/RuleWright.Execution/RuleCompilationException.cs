using System;
using RuleWright.Core;

namespace RuleWright.Execution;

/// <summary>
/// Thrown when a structurally valid rule cannot be compiled against a fact type —
/// for example a field path that does not exist on the fact, a comparison value that
/// cannot be converted to the field's type, or an unregistered custom function.
/// </summary>
public sealed class RuleCompilationException : RuleWrightException
{
    /// <summary>
    /// Creates the exception.
    /// </summary>
    /// <param name="ruleId">The id of the rule that failed to compile.</param>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying cause, if any.</param>
    public RuleCompilationException(string ruleId, string message, Exception? innerException = null)
        : base($"Rule '{ruleId}': {message}", innerException!)
    {
        RuleId = ruleId;
    }

    /// <summary>The id of the rule that failed to compile.</summary>
    public string RuleId { get; }
}
