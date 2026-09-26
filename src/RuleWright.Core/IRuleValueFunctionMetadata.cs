namespace RuleWright.Core;

/// <summary>
/// Optional metadata an <see cref="IRuleValueFunction"/> implementation can expose about
/// itself. The description is purely informational (surfaced via
/// <see cref="RuleValueFunctionDescriptor"/> for rule-authoring UIs); a declared
/// <see cref="RequiredOperandCount"/> is additionally enforced at <c>LoadRuleSet</c>, so a
/// call with the wrong number of operands fails at load rather than computing null mid-evaluation.
/// </summary>
public interface IRuleValueFunctionMetadata
{
    /// <summary>A short, human-readable description of what the function computes.</summary>
    string? Description { get; }

    /// <summary>
    /// The exact number of operands a call must supply, or null when the function accepts
    /// any number.
    /// </summary>
    int? RequiredOperandCount { get; }
}
