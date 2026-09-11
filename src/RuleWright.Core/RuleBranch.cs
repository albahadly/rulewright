namespace RuleWright.Core;

/// <summary>
/// Which side of a rule produced a <see cref="FiredRule"/>: the <c>actions</c> that run when
/// the condition passes, or the <c>else</c> actions that run when it does not.
/// </summary>
public enum RuleBranch
{
    /// <summary>The rule's condition matched; its <c>actions</c> ran.</summary>
    Then,

    /// <summary>The rule's condition did not match; its <c>else</c> actions ran.</summary>
    Else,
}
