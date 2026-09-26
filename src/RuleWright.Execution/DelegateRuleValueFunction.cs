using System;
using RuleWright.Core;

namespace RuleWright.Execution;

/// <summary>
/// Wraps a delegate registered via <see cref="RuleWrightBuilder.RegisterValueFunction(string, Func{object?[], object?})"/>
/// as an <see cref="IRuleValueFunction"/>.
/// </summary>
internal sealed class DelegateRuleValueFunction : IRuleValueFunction
{
    private readonly Func<object?[], object?> _function;

    internal DelegateRuleValueFunction(string name, Func<object?[], object?> function)
    {
        Name = name;
        _function = function;
    }

    public string Name { get; }

    public object? Invoke(object?[] arguments) => _function(arguments);
}
