using RuleWright.Core;
using Xunit;
using static RuleWright.Execution.Tests.TestEngine;

namespace RuleWright.Execution.Tests;

/// <summary>
/// <c>failureMessage</c>: a rule's authored explanation for saying no, surfaced as
/// <see cref="RuleEvaluationResult.Failures"/> when the rule is evaluated and its condition
/// does not pass. A message means the rule was actually asked — skipped rules report nothing.
/// </summary>
public class FailureMessageTests
{
    private static Dictionary<string, object?> DictFact() => new()
    {
        ["Customer"] = new Dictionary<string, object?> { ["Age"] = 21L },
        ["Order"] = new Dictionary<string, object?> { ["Total"] = 120.5m },
    };

    [Fact]
    public void FailedRule_ReportsItsMessage_OnBothPaths()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(@"{
          ""rules"": [
            { ""id"": ""senior"",
              ""failureMessage"": ""Customer must be at least 65."",
              ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""GreaterThanOrEqual"", ""value"": 65 },
              ""actions"": [ { ""type"": ""setOutput"", ""target"": ""SeniorDiscount"", ""value"": 15 } ] },
            { ""id"": ""adult"",
              ""failureMessage"": ""Customer must be an adult."",
              ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""GreaterThanOrEqual"", ""value"": 18 },
              ""actions"": [ { ""type"": ""setOutput"", ""target"": ""Eligible"", ""value"": true } ] }
          ]
        }");

        foreach (object fact in new object[] { DefaultFact(), DictFact() })
        {
            RuleEvaluationResult result = Engine.Evaluate(loaded, fact);

            RuleFailure failure = Assert.Single(result.Failures);
            Assert.Equal("senior", failure.RuleId);
            Assert.Equal("Customer must be at least 65.", failure.Message);
            Assert.True((bool)result.Outputs["Eligible"]!);
        }
    }

    [Fact]
    public void MatchedRule_ReportsNothing()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(@"{
          ""id"": ""adult"",
          ""failureMessage"": ""never shown"",
          ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""GreaterThanOrEqual"", ""value"": 18 }
        }");

        Assert.Empty(Engine.Evaluate(loaded, DefaultFact()).Failures);
    }

    [Fact]
    public void RuleWithoutMessage_FailsSilently()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(@"{
          ""id"": ""senior"",
          ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""GreaterThanOrEqual"", ""value"": 65 }
        }");

        Assert.Empty(Engine.Evaluate(loaded, DefaultFact()).Failures);
    }

    [Fact]
    public void EmptyMessage_OnAHandBuiltRule_MeansNoMessage()
    {
        var rule = new Rule(
            "r",
            new ConditionLeaf("Customer.Age", ConditionOperator.GreaterThanOrEqual, 999L),
            failureMessage: string.Empty);

        Assert.Null(rule.FailureMessage);
        Assert.Empty(Engine.Evaluate(Engine.LoadRuleSet(new RuleSet(new[] { rule })), DefaultFact()).Failures);
    }

    [Fact]
    public void SkippedRules_ReportNothing()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(@"{
          ""stopAfterFirstMatch"": true,
          ""rules"": [
            { ""id"": ""disabled"", ""enabled"": false, ""priority"": 3,
              ""failureMessage"": ""never asked (disabled)"",
              ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""GreaterThanOrEqual"", ""value"": 999 } },
            { ""id"": ""matches"", ""priority"": 2,
              ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""GreaterThanOrEqual"", ""value"": 18 } },
            { ""id"": ""unreached"", ""priority"": 1,
              ""failureMessage"": ""never asked (stopped)"",
              ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""GreaterThanOrEqual"", ""value"": 999 } }
          ]
        }");

        Assert.Empty(Engine.Evaluate(loaded, DefaultFact()).Failures);
    }

    [Fact]
    public void ElseBranch_StillCountsAsAFailedCondition()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(@"{
          ""id"": ""free-shipping"",
          ""failureMessage"": ""Order total below the free-shipping threshold."",
          ""condition"": { ""field"": ""Order.Total"", ""operator"": ""GreaterThanOrEqual"", ""value"": 500 },
          ""actions"": [ { ""type"": ""setOutput"", ""target"": ""Shipping"", ""value"": 0 } ],
          ""else"":    [ { ""type"": ""setOutput"", ""target"": ""Shipping"", ""value"": 4.95 } ]
        }");

        RuleEvaluationResult result = Engine.Evaluate(loaded, DefaultFact());

        // The else branch fired, and the condition's failure is still explained.
        Assert.Equal(RuleBranch.Else, result.FiredRules[0].Branch);
        Assert.Equal(4.95m, result.Outputs["Shipping"]);
        Assert.Equal("free-shipping", Assert.Single(result.Failures).RuleId);
    }
}
