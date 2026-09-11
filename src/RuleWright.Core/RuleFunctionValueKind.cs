namespace RuleWright.Core;

/// <summary>
/// A coarse hint about the shape of the <c>value</c> operand a custom <see cref="IRuleFunction"/>
/// expects, when it opts into <see cref="IRuleFunctionMetadata"/>. Purely descriptive — the engine
/// never validates against it; it exists so a rule-authoring UI can choose an appropriate value
/// editor for the <c>custom</c> operator.
/// </summary>
public enum RuleFunctionValueKind
{
    /// <summary>No metadata was supplied; the expected shape is unknown.</summary>
    Unspecified,

    /// <summary>The function ignores <c>value</c> — only the field matters.</summary>
    None,

    /// <summary>A single scalar (number, string, or boolean).</summary>
    Scalar,

    /// <summary>An array of values, e.g. the <c>[min, max]</c> pair for a range check.</summary>
    Array,

    /// <summary>A single string value.</summary>
    Text,
}
