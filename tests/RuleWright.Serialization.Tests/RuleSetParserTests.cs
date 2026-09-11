using RuleWright.Core;
using RuleWright.Json.SystemText;
using RuleWright.Serialization;
using Xunit;

namespace RuleWright.Serialization.Tests;

public class RuleSetParserTests
{
    private static RuleSet Parse(string json)
        => RuleSetParser.Parse(new SystemTextJsonReader().Read(json));

    [Fact]
    public void SingleRuleDocument_BecomesOneRuleSet()
    {
        RuleSet set = Parse(@"{
          ""id"": ""discount-rule-01"",
          ""description"": ""VIP discount"",
          ""priority"": 10,
          ""enabled"": true,
          ""condition"": {
            ""type"": ""group"", ""operator"": ""AND"",
            ""rules"": [
              { ""field"": ""Customer.Age"", ""operator"": ""GreaterThan"", ""value"": 18 },
              { ""field"": ""Customer.IsVip"", ""operator"": ""Equals"", ""value"": true }
            ]
          },
          ""actions"": [ { ""type"": ""setOutput"", ""target"": ""Discount"", ""value"": 10 } ],
          ""layout"": { ""position"": { ""x"": 1, ""y"": 2 } }
        }");

        Rule rule = Assert.Single(set.Rules);
        Assert.Equal("discount-rule-01", rule.Id);
        Assert.Equal("VIP discount", rule.Description);
        Assert.Equal(10, rule.Priority);
        Assert.True(rule.Enabled);

        var root = Assert.IsType<ConditionGroup>(rule.Condition);
        Assert.Equal(LogicalOperator.And, root.Operator);
        Assert.Equal(2, root.Children.Count);

        var age = Assert.IsType<ConditionLeaf>(root.Children[0]);
        Assert.Equal("Customer.Age", age.Field);
        Assert.Equal(ConditionOperator.GreaterThan, age.Operator);
        Assert.Equal(18L, age.Value);

        var vip = Assert.IsType<ConditionLeaf>(root.Children[1]);
        Assert.Equal(ConditionOperator.Equal, vip.Operator);
        Assert.True((bool)vip.Value!);

        RuleAction action = Assert.Single(rule.Actions);
        Assert.Equal(RuleAction.SetOutputType, action.Type);
        Assert.Equal("Discount", action.Target);
        Assert.Equal(10L, Assert.IsType<LiteralExpression>(action.Value).Value);
    }

    [Fact]
    public void RuleSetDocument_ParsesNameAndAllRules()
    {
        RuleSet set = Parse(@"{
          ""name"": ""pricing"",
          ""rules"": [
            { ""id"": ""a"", ""condition"": { ""field"": ""X"", ""operator"": ""IsNull"" } },
            { ""id"": ""b"", ""enabled"": false, ""condition"": { ""field"": ""Y"", ""operator"": ""In"", ""value"": [1, 2, 3] } }
          ]
        }");

        Assert.Equal("pricing", set.Name);
        Assert.Equal(2, set.Rules.Count);
        Assert.False(set.Rules[1].Enabled);

        var leaf = Assert.IsType<ConditionLeaf>(set.Rules[1].Condition);
        var items = Assert.IsType<object?[]>(leaf.Value);
        Assert.Equal(new object?[] { 1L, 2L, 3L }, items);
    }

    [Fact]
    public void NotGroup_ParsesToNotOperator()
    {
        RuleSet set = Parse(@"{
          ""id"": ""r"",
          ""condition"": {
            ""type"": ""group"", ""operator"": ""NOT"",
            ""rules"": [ { ""field"": ""A"", ""operator"": ""IsNull"" } ]
          }
        }");
        var group = Assert.IsType<ConditionGroup>(set.Rules[0].Condition);
        Assert.Equal(LogicalOperator.Not, group.Operator);
        Assert.Single(group.Children);
    }

    [Fact]
    public void CustomLeaf_ParsesFunctionName()
    {
        RuleSet set = Parse(
            "{\"id\":\"r\",\"condition\":{\"field\":\"Order.Date\",\"operator\":\"custom\",\"name\":\"IsBusinessDay\"}}");
        var leaf = Assert.IsType<ConditionLeaf>(set.Rules[0].Condition);
        Assert.Equal(ConditionOperator.Custom, leaf.Operator);
        Assert.Equal("IsBusinessDay", leaf.FunctionName);
        Assert.Equal("Order.Date", leaf.Field);
    }

    [Fact]
    public void InvalidDocument_ThrowsRuleValidationExceptionWithPointers()
    {
        var exception = Assert.Throws<RuleValidationException>(
            () => Parse("{\"id\":\"r\",\"condition\":{\"field\":\"A\",\"operator\":\"Bogus\",\"value\":1}}"));
        RuleValidationError error = Assert.Single(exception.Errors);
        Assert.Equal("/condition/operator", error.Path);
    }

    [Fact]
    public void DecimalValues_ParseAsDecimal()
    {
        RuleSet set = Parse(
            "{\"id\":\"r\",\"condition\":{\"field\":\"A\",\"operator\":\"GreaterThan\",\"value\":10.5}}");
        var leaf = Assert.IsType<ConditionLeaf>(set.Rules[0].Condition);
        Assert.Equal(10.5m, leaf.Value);
    }

    [Fact]
    public void ElseActions_Parse()
    {
        RuleSet set = Parse(@"{
          ""id"": ""r"",
          ""condition"": { ""field"": ""A"", ""operator"": ""IsNotNull"" },
          ""actions"": [ { ""type"": ""setOutput"", ""target"": ""T"", ""value"": ""yes"" } ],
          ""else"": [ { ""type"": ""setOutput"", ""target"": ""T"", ""value"": ""no"" } ]
        }");

        Rule rule = Assert.Single(set.Rules);
        RuleAction elseAction = Assert.Single(rule.ElseActions);
        Assert.Equal(RuleAction.SetOutputType, elseAction.Type);
        Assert.Equal("T", elseAction.Target);
        Assert.Equal("no", Assert.IsType<LiteralExpression>(elseAction.Value).Value);
    }

    [Fact]
    public void MissingElse_YieldsEmptyElseActions()
    {
        RuleSet set = Parse("{\"id\":\"r\",\"condition\":{\"field\":\"A\",\"operator\":\"IsNull\"}}");
        Assert.Empty(set.Rules[0].ElseActions);
    }

    [Fact]
    public void RemoveOutputAction_ParsesWithoutValue()
    {
        RuleSet set = Parse(@"{
          ""id"": ""r"",
          ""condition"": { ""field"": ""A"", ""operator"": ""IsNotNull"" },
          ""actions"": [ { ""type"": ""removeOutput"", ""target"": ""Discount"" } ]
        }");

        RuleAction action = Assert.Single(set.Rules[0].Actions);
        Assert.Equal(RuleAction.RemoveOutputType, action.Type);
        Assert.Equal("Discount", action.Target);
        Assert.Null(Assert.IsType<LiteralExpression>(action.Value).Value);
    }
}
