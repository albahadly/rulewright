using RuleWright.Core;
using Xunit;

namespace RuleWright.Core.Tests;

public class DomainModelTests
{
    private static ConditionLeaf Leaf() => new ConditionLeaf("A", ConditionOperator.Equal, 1L);

    [Fact]
    public void Rule_Defaults_PriorityZeroAndEnabled()
    {
        var rule = new Rule("r1", Leaf());
        Assert.Equal(0, rule.Priority);
        Assert.True(rule.Enabled);
        Assert.Null(rule.Description);
        Assert.Empty(rule.Actions);
    }

    [Fact]
    public void Rule_EmptyId_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Rule(string.Empty, Leaf()));
    }

    [Fact]
    public void Rule_NullCondition_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new Rule("r1", null!));
    }

    [Fact]
    public void ConditionGroup_NotWithTwoChildren_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new ConditionGroup(LogicalOperator.Not, new ConditionNode[] { Leaf(), Leaf() }));
    }

    [Fact]
    public void ConditionGroup_Empty_Throws()
    {
        Assert.Throws<ArgumentException>(() => new ConditionGroup(LogicalOperator.And, new ConditionNode[0]));
    }

    [Fact]
    public void ConditionLeaf_CustomWithoutFunctionName_Throws()
    {
        Assert.Throws<ArgumentException>(() => new ConditionLeaf("A", ConditionOperator.Custom, null));
    }

    [Fact]
    public void ConditionLeaf_NonCustomWithoutField_Throws()
    {
        Assert.Throws<ArgumentException>(() => new ConditionLeaf((string?)null, ConditionOperator.Equal, 1L));
    }

    [Fact]
    public void ConditionLeaf_CustomWithoutField_IsAllowed()
    {
        var leaf = new ConditionLeaf(null, ConditionOperator.Custom, null, "IsBusinessDay");
        Assert.Null(leaf.Field);
        Assert.Equal("IsBusinessDay", leaf.FunctionName);
    }

    [Fact]
    public void RuleSet_DuplicateIds_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new RuleSet(new[] { new Rule("a", Leaf()), new Rule("a", Leaf()) }));
    }

    [Fact]
    public void RuleSet_Empty_Throws()
    {
        Assert.Throws<ArgumentException>(() => new RuleSet(new Rule[0]));
    }

    [Fact]
    public void RuleAction_EmptyTarget_Throws()
    {
        Assert.Throws<ArgumentException>(() => new RuleAction(RuleAction.SetOutputType, string.Empty, 1));
    }

    [Fact]
    public void RuleAction_RemoveOutput_HasTypeAndNullValueLiteral()
    {
        RuleAction action = RuleAction.RemoveOutput("Discount");
        Assert.Equal(RuleAction.RemoveOutputType, action.Type);
        Assert.Equal("Discount", action.Target);
        Assert.Null(Assert.IsType<LiteralExpression>(action.Value).Value);
    }

    [Fact]
    public void Rule_ElseActions_DefaultEmpty()
    {
        Assert.Empty(new Rule("r", Leaf()).ElseActions);
    }

    [Fact]
    public void Rule_ElseActions_AreExposed()
    {
        var rule = new Rule("r", Leaf(), elseActions: new[] { RuleAction.RemoveOutput("T") });
        RuleAction elseAction = Assert.Single(rule.ElseActions);
        Assert.Equal(RuleAction.RemoveOutputType, elseAction.Type);
    }

    [Fact]
    public void Rule_ElseActions_WithNull_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new Rule("r", Leaf(), elseActions: new RuleAction[] { null! }));
    }

    [Fact]
    public void FiredRule_Branch_DefaultsToThen()
    {
        var fired = new FiredRule("r", new Dictionary<string, object?>());
        Assert.Equal(RuleBranch.Then, fired.Branch);
    }

    [Fact]
    public void FiredRule_Branch_IsExposed()
    {
        var fired = new FiredRule("r", new Dictionary<string, object?>(), RuleBranch.Else);
        Assert.Equal(RuleBranch.Else, fired.Branch);
    }

    [Fact]
    public void EvaluationOptions_Default_TracingOffEvaluateAll()
    {
        Assert.False(EvaluationOptions.Default.EnableTrace);
        Assert.False(EvaluationOptions.Default.StopOnFirstMatch);
    }
}
