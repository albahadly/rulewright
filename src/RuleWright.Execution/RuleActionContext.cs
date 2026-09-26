using System;
using System.Collections.Generic;
using RuleWright.Core;

namespace RuleWright.Execution;

/// <summary>
/// Everything an <see cref="IRuleActionHandler"/> gets to see and touch when its action fires:
/// the firing rule and branch, the action's <c>target</c> and already-evaluated <c>value</c>,
/// the fact under evaluation (read-only by convention), and the running outputs. Writes go
/// through <see cref="SetOutput"/>/<see cref="RemoveOutput"/> so they land in both the merged
/// result outputs and the firing rule's own <c>FiredRule.Outputs</c> snapshot, exactly as the
/// built-in action types record theirs.
/// </summary>
public sealed class RuleActionContext
{
    private readonly IDictionary<string, object?> _running;
    private readonly Dictionary<string, object?> _snapshot;

    internal RuleActionContext(
        IDictionary<string, object?> running,
        Dictionary<string, object?> snapshot,
        string ruleId,
        RuleBranch branch,
        string target,
        object? value,
        object fact)
    {
        _running = running;
        _snapshot = snapshot;
        RuleId = ruleId;
        Branch = branch;
        Target = target;
        Value = value;
        Fact = fact;
    }

    /// <summary>The id of the rule whose branch fired.</summary>
    public string RuleId { get; }

    /// <summary>Which branch fired: <c>actions</c> or <c>else</c>.</summary>
    public RuleBranch Branch { get; }

    /// <summary>The action's <c>target</c> output key.</summary>
    public string Target { get; }

    /// <summary>
    /// The action's <c>value</c>, already evaluated against the fact (a constant, field read,
    /// computed expression, or <c>call</c> result). Null when the action declared no value.
    /// </summary>
    public object? Value { get; }

    /// <summary>The fact under evaluation. Treat it as read-only.</summary>
    public object Fact { get; }

    /// <summary>Reads a key from the running outputs (what earlier-fired rules have written).</summary>
    /// <param name="key">The output key.</param>
    /// <param name="value">The current value, or null when absent.</param>
    /// <returns>Whether the key is present.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
    public bool TryGetOutput(string key, out object? value)
    {
        if (key is null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        return _running.TryGetValue(key, out value);
    }

    /// <summary>Writes a key into the running outputs and the firing rule's own snapshot.</summary>
    /// <param name="key">The output key.</param>
    /// <param name="value">The value to write.</param>
    /// <exception cref="ArgumentException"><paramref name="key"/> is null or empty.</exception>
    public void SetOutput(string key, object? value)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentException("Output key must not be null or empty.", nameof(key));
        }

        _running[key] = value;
        _snapshot[key] = value;
    }

    /// <summary>Removes a key from the running outputs and the firing rule's own snapshot.</summary>
    /// <param name="key">The output key.</param>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
    public void RemoveOutput(string key)
    {
        if (key is null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        _running.Remove(key);
        _snapshot.Remove(key);
    }
}
