using System;

namespace RuleWright.Execution;

/// <summary>
/// Wraps a delegate registered via <see cref="RuleWrightBuilder.RegisterAction(string, Action{RuleActionContext})"/>
/// as an <see cref="IRuleActionHandler"/>.
/// </summary>
internal sealed class DelegateRuleActionHandler : IRuleActionHandler
{
    private readonly Action<RuleActionContext> _handler;

    internal DelegateRuleActionHandler(string name, Action<RuleActionContext> handler)
    {
        Name = name;
        _handler = handler;
    }

    public string Name { get; }

    public void Apply(RuleActionContext context) => _handler(context);
}
