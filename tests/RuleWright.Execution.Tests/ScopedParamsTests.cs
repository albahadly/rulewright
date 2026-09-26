using RuleWright.Core;
using Xunit;
using static RuleWright.Execution.Tests.TestEngine;

namespace RuleWright.Execution.Tests;

/// <summary>
/// Scoped params — named subexpressions declared in a rule set's or rule's <c>params</c> and
/// referenced as <c>{ "param": "..." }</c> — evaluated across both execution paths. Params
/// are inlined at load, so everything here must behave exactly as the hand-inlined document
/// would; the substitution itself is covered by the serialization tests.
/// </summary>
public class ScopedParamsTests
{
    private static Dictionary<string, object?> DictFact() => new()
    {
        ["Customer"] = new Dictionary<string, object?> { ["Age"] = 21L, ["Name"] = "Alice" },
        ["Order"] = new Dictionary<string, object?> { ["Total"] = 120.5m, ["ItemCount"] = 3L },
    };

    [Fact]
    public void GlobalParam_IsUsableFromEveryRule_OnBothPaths()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(@"{
          ""name"": ""set"",
          ""params"": {
            ""averageItem"": { ""op"": ""divide"", ""operands"": [ { ""field"": ""Order.Total"" }, { ""field"": ""Order.ItemCount"" } ] }
          },
          ""rules"": [
            { ""id"": ""pricey"",
              ""condition"": { ""expression"": { ""param"": ""averageItem"" }, ""operator"": ""GreaterThan"", ""value"": 25 },
              ""actions"": [ { ""type"": ""setOutput"", ""target"": ""Pricey"", ""value"": true } ] },
            { ""id"": ""echo"",
              ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""IsNotNull"" },
              ""actions"": [ { ""type"": ""setOutput"", ""target"": ""Average"", ""value"": { ""param"": ""averageItem"" } } ] }
          ]
        }");

        foreach (object fact in new object[] { DefaultFact(), DictFact() })
        {
            RuleEvaluationResult result = Engine.Evaluate(loaded, fact);
            Assert.True((bool)result.Outputs["Pricey"]!);
            Assert.Equal(120.5m / 3m, result.Outputs["Average"]);
        }
    }

    [Fact]
    public void LocalParam_ShadowsGlobal()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(@"{
          ""params"": { ""rate"": 0.1 },
          ""rules"": [
            { ""id"": ""global-rate"",
              ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""IsNotNull"" },
              ""actions"": [ { ""type"": ""setOutput"", ""target"": ""G"", ""value"": { ""op"": ""multiply"", ""operands"": [ { ""field"": ""Order.Total"" }, { ""param"": ""rate"" } ] } } ] },
            { ""id"": ""local-rate"",
              ""params"": { ""rate"": 0.5 },
              ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""IsNotNull"" },
              ""actions"": [ { ""type"": ""setOutput"", ""target"": ""L"", ""value"": { ""op"": ""multiply"", ""operands"": [ { ""field"": ""Order.Total"" }, { ""param"": ""rate"" } ] } } ] }
          ]
        }");

        RuleEvaluationResult result = Engine.Evaluate(loaded, DefaultFact());
        Assert.Equal(12.05m, result.Outputs["G"]);
        Assert.Equal(60.25m, result.Outputs["L"]);
    }

    [Fact]
    public void Param_MayReferenceAnotherParam()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(@"{
          ""params"": {
            ""net"": { ""op"": ""subtract"", ""operands"": [ { ""field"": ""Order.Total"" }, 20.5 ] },
            ""netWithFee"": { ""op"": ""add"", ""operands"": [ { ""param"": ""net"" }, 5 ] }
          },
          ""rules"": [
            { ""id"": ""r"",
              ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""IsNotNull"" },
              ""actions"": [ { ""type"": ""setOutput"", ""target"": ""D"", ""value"": { ""param"": ""netWithFee"" } } ] }
          ]
        }");

        Assert.Equal(105m, Engine.Evaluate(loaded, DefaultFact()).Outputs["D"]);
        Assert.Equal(105m, Engine.Evaluate(loaded, DictFact()).Outputs["D"]);
    }

    [Fact]
    public void Param_InDecisionTableCell()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(@"{
          ""decisionTable"": {
            ""id"": ""discount"",
            ""hitPolicy"": ""first"",
            ""params"": { ""tenPercent"": { ""op"": ""multiply"", ""operands"": [ { ""field"": ""Order.Total"" }, 0.1 ] } },
            ""inputs"": [ { ""field"": ""Customer.IsVip"", ""operator"": ""Equals"" } ],
            ""outputs"": [ { ""target"": ""Discount"" } ],
            ""rows"": [
              { ""when"": [ true ],  ""then"": [ { ""param"": ""tenPercent"" } ] },
              { ""when"": [ null ],  ""then"": [ 0 ] }
            ]
          }
        }");

        Assert.Equal(12.05m, Engine.Evaluate(loaded, DefaultFact()).Outputs["Discount"]);
    }

    [Fact]
    public void SingleRuleDocument_MayDeclareItsOwnParams()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(@"{
          ""id"": ""r"",
          ""params"": { ""doubled"": { ""op"": ""multiply"", ""operands"": [ { ""field"": ""Customer.Age"" }, 2 ] } },
          ""condition"": { ""expression"": { ""param"": ""doubled"" }, ""operator"": ""GreaterThanOrEqual"", ""value"": 42 },
          ""actions"": [ { ""type"": ""setOutput"", ""target"": ""D"", ""value"": { ""param"": ""doubled"" } } ]
        }");

        Assert.Equal(42m, Engine.Evaluate(loaded, DefaultFact()).Outputs["D"]);
        Assert.Equal(42m, Engine.Evaluate(loaded, DictFact()).Outputs["D"]);
    }
}
