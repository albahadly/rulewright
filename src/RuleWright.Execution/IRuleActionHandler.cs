namespace RuleWright.Execution;

/// <summary>
/// A custom action type referenced from rule JSON as
/// <c>{ "type": "&lt;Name&gt;", "target": "...", "value": ... }</c>. Where the built-in action
/// types cover replacing, accumulating, appending, and removing outputs, a handler implements
/// any other way a fired rule should shape the outputs — the rule document still only
/// <em>names</em> the action, and the behaviour stays registered C# code
/// (<c>RuleWrightBuilder.RegisterAction</c>). An unregistered type fails at
/// <c>LoadRuleSet</c>, exactly as an unknown built-in type does.
/// </summary>
/// <remarks>
/// Implementations must be thread-safe (one instance serves all concurrent evaluations) and
/// should confine themselves to the outputs exposed on the context: evaluation is meant to
/// stay a pure fact-in, result-out computation.
/// </remarks>
public interface IRuleActionHandler
{
    /// <summary>The action type name used in rule JSON. Case-sensitive.</summary>
    string Name { get; }

    /// <summary>
    /// Applies the action for one fired rule branch.
    /// </summary>
    /// <param name="context">The action's target, evaluated value, fact, and output access.</param>
    void Apply(RuleActionContext context);
}
