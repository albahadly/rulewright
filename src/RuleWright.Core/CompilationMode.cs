namespace RuleWright.Core;

/// <summary>
/// How a rule set was executed for a given fact shape. Exposed on
/// <see cref="RuleEvaluationResult.CompilationMode"/> so interpreted (slower)
/// execution is visible rather than a silent degradation.
/// </summary>
public enum CompilationMode
{
    /// <summary>Rules were compiled to delegates via expression trees (typed facts).</summary>
    Compiled,

    /// <summary>Rules were interpreted by walking the condition tree (dictionary facts).</summary>
    Interpreted,
}
