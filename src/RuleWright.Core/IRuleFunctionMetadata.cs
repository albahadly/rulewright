namespace RuleWright.Core;

/// <summary>
/// Optional metadata an <see cref="IRuleFunction"/> implementation can expose about itself —
/// a human-readable description and the expected shape of its <c>value</c> operand. Purely
/// descriptive, consumed via <see cref="RuleFunctionDescriptor"/>; the engine never validates
/// against it. Intended for rule-authoring UIs (e.g. a <c>custom</c>-operator function picker)
/// that need more than just a function's name to build a sensible value editor.
/// </summary>
public interface IRuleFunctionMetadata
{
    /// <summary>A short, human-readable description of what the function checks.</summary>
    string? Description { get; }

    /// <summary>A coarse hint about the shape of the <c>value</c> operand this function expects.</summary>
    RuleFunctionValueKind ValueKind { get; }
}
