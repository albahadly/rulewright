using RuleWright.Core;
using Xunit;
using static RuleWright.Execution.Tests.TestEngine;

namespace RuleWright.Execution.Tests;

/// <summary>
/// A condition leaf's left-hand side can be a computed value expression (for example
/// <c>Order.Total * 0.9</c>) rather than only a dotted field path. The compiled path computes
/// the left side with reflection-free field access, then both paths compare it through the
/// same boxed operator logic, so typed and dictionary facts agree.
/// </summary>
public class ComputedConditionTests
{
    private static Dictionary<string, object?> DictFact() => new()
    {
        ["Customer"] = new Dictionary<string, object?> { ["Age"] = 21L },
        ["Order"] = new Dictionary<string, object?> { ["Total"] = 120.5m, ["ItemCount"] = 3L },
    };

    private static void AssertBothPaths(string conditionJson, bool expected)
    {
        Assert.Equal(expected, Matches(conditionJson, DefaultFact()));
        Assert.Equal(expected, Matches(conditionJson, DictFact()));
    }

    [Fact]
    public void Multiply_GreaterThan_True()
        => AssertBothPaths(
            "{\"expression\":{\"op\":\"multiply\",\"operands\":[{\"field\":\"Order.Total\"},0.9]},\"operator\":\"GreaterThan\",\"value\":100}",
            true); // 120.5 * 0.9 = 108.45 > 100

    [Fact]
    public void Multiply_GreaterThan_False()
        => AssertBothPaths(
            "{\"expression\":{\"op\":\"multiply\",\"operands\":[{\"field\":\"Order.Total\"},0.5]},\"operator\":\"GreaterThan\",\"value\":100}",
            false); // 120.5 * 0.5 = 60.25 > 100

    [Fact]
    public void Add_GreaterThanOrEqual()
        => AssertBothPaths(
            "{\"expression\":{\"op\":\"add\",\"operands\":[{\"field\":\"Order.ItemCount\"},1]},\"operator\":\"GreaterThanOrEqual\",\"value\":4}",
            true); // 3 + 1 = 4 >= 4

    [Fact]
    public void Subtract_Equals()
        => AssertBothPaths(
            "{\"expression\":{\"op\":\"subtract\",\"operands\":[{\"field\":\"Order.Total\"},20.5]},\"operator\":\"Equals\",\"value\":100}",
            true); // 120.5 - 20.5 = 100 == 100

    [Fact]
    public void Modulo_Equals()
        => AssertBothPaths(
            "{\"expression\":{\"op\":\"modulo\",\"operands\":[{\"field\":\"Order.ItemCount\"},2]},\"operator\":\"Equals\",\"value\":1}",
            true); // 3 % 2 = 1 == 1

    [Fact]
    public void ComputedLeaf_InsideGroup()
        => AssertBothPaths(
            "{\"type\":\"group\",\"operator\":\"AND\",\"rules\":["
            + "{\"field\":\"Customer.Age\",\"operator\":\"GreaterThan\",\"value\":18},"
            + "{\"expression\":{\"op\":\"multiply\",\"operands\":[{\"field\":\"Order.Total\"},0.9]},\"operator\":\"GreaterThan\",\"value\":100}]}",
            true);

    [Fact]
    public void NullComputedLeft_AppliesNullSemantics()
    {
        const string isNull = "{\"expression\":{\"op\":\"multiply\",\"operands\":[{\"field\":\"Order.Total\"},2]},\"operator\":\"IsNull\"}";
        const string greaterThan = "{\"expression\":{\"op\":\"multiply\",\"operands\":[{\"field\":\"Order.Total\"},2]},\"operator\":\"GreaterThan\",\"value\":1}";

        OrderFact typed = DefaultFact();
        typed.Order = null; // Order.Total resolves to null via null-safe navigation
        Assert.True(Matches(isNull, typed));
        Assert.False(Matches(greaterThan, typed));

        var dict = new Dictionary<string, object?> { ["Customer"] = new Dictionary<string, object?> { ["Age"] = 21L } };
        Assert.True(Matches(isNull, dict));
        Assert.False(Matches(greaterThan, dict));
    }

    [Fact]
    public void MissingMemberInComputedLeft_ThrowsAtCompileTimeForTypedFacts()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(WrapRule(
            "{\"expression\":{\"field\":\"Order.Nonexistent\"},\"operator\":\"GreaterThan\",\"value\":1}"));
        Assert.Throws<RuleCompilationException>(() => Engine.Evaluate(loaded, DefaultFact()));
    }

    [Fact]
    public void ComputedLeaf_IsTraced()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(WrapRule(
            "{\"expression\":{\"op\":\"multiply\",\"operands\":[{\"field\":\"Order.Total\"},0.9]},\"operator\":\"GreaterThan\",\"value\":100}"));
        RuleEvaluationResult result = Engine.Evaluate(loaded, DefaultFact(), new EvaluationOptions { EnableTrace = true });

        ConditionTraceNode root = result.Trace!.Rules.Single().Condition!;
        Assert.True(root.Passed);
    }
}
