using System;

namespace RuleWright.Core;

/// <summary>
/// One rule's authored explanation for not matching: produced when a rule that declares a
/// <c>failureMessage</c> is evaluated and its condition does not pass. Skipped rules
/// (disabled, or unreached after a stop-on-first-match) produce none — a message means the
/// rule was actually asked and said no.
/// </summary>
public sealed class RuleFailure
{
    /// <summary>
    /// Creates a rule failure.
    /// </summary>
    /// <param name="ruleId">The id of the rule whose condition did not pass.</param>
    /// <param name="message">The rule's authored <c>failureMessage</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="ruleId"/> or <paramref name="message"/> is null or empty.</exception>
    public RuleFailure(string ruleId, string message)
    {
        if (string.IsNullOrEmpty(ruleId))
        {
            throw new ArgumentException("Rule id must not be null or empty.", nameof(ruleId));
        }

        if (string.IsNullOrEmpty(message))
        {
            throw new ArgumentException("Failure message must not be null or empty.", nameof(message));
        }

        RuleId = ruleId;
        Message = message;
    }

    /// <summary>The id of the rule whose condition did not pass.</summary>
    public string RuleId { get; }

    /// <summary>The rule's authored <c>failureMessage</c>.</summary>
    public string Message { get; }
}
