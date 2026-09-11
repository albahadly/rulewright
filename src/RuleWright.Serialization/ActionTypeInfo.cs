namespace RuleWright.Serialization;

/// <summary>How an action changes the running outputs at its target.</summary>
public enum ActionEffect
{
    /// <summary>Replaces the value at the target (<c>setOutput</c>).</summary>
    Replace,

    /// <summary>Numerically adds to a running total (<c>addToOutput</c>).</summary>
    Add,

    /// <summary>Appends to a list (<c>appendToOutput</c>).</summary>
    Append,

    /// <summary>Deletes the target key (<c>removeOutput</c>).</summary>
    Remove,
}

/// <summary>
/// Discovery metadata for one action type: its JSON <c>type</c> name, whether it takes a
/// <c>value</c>, and how it changes the outputs. Enumerated from
/// <see cref="RuleSchemaCatalog.ActionTypes"/>.
/// </summary>
public sealed class ActionTypeInfo
{
    internal ActionTypeInfo(string name, bool requiresValue, ActionEffect effect)
    {
        Name = name;
        RequiresValue = requiresValue;
        Effect = effect;
    }

    /// <summary>The JSON <c>type</c> name (e.g. <c>"setOutput"</c>).</summary>
    public string Name { get; }

    /// <summary>Whether the action requires a <c>value</c> (false only for <c>removeOutput</c>).</summary>
    public bool RequiresValue { get; }

    /// <summary>How the action changes the outputs at its target.</summary>
    public ActionEffect Effect { get; }
}
