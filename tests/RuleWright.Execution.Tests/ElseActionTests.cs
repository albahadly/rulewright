using RuleWright.Core;
using Xunit;
using static RuleWright.Execution.Tests.TestEngine;

namespace RuleWright.Execution.Tests;

/// <summary>
/// A rule's <c>else</c> actions run when its condition does not match. Verified on both the
/// compiled (typed <see cref="OrderFact"/>) and interpreted (dictionary) paths, which must
/// agree, and across the pre-materialized and complex output paths.
/// </summary>
public class ElseActionTests
{
    // Mirrors the relevant fields of TestEngine.DefaultFact() so the two paths compare.
    private static Dictionary<string, object?> DictFact() => new()
    {
        ["Customer"] = new Dictionary<string, object?> { ["Age"] = 21L, ["IsVip"] = true, ["Name"] = "Alice" },
        ["Order"] = new Dictionary<string, object?> { ["Total"] = 120.50m, ["ItemCount"] = 3L },
    };

    private static RuleEvaluationResult Typed(string rulesJson, EvaluationOptions? options = null)
        => Engine.Evaluate(Engine.LoadRuleSet(rulesJson), DefaultFact(), options);

    private static RuleEvaluationResult Interpreted(string rulesJson, EvaluationOptions? options = null)
        => Engine.Evaluate(Engine.LoadRuleSet(rulesJson), DictFact(), options);

    private const string TierRule = @"{
      ""id"": ""tier"",
      ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""GreaterThan"", ""value"": 100 },
      ""actions"": [ { ""type"": ""setOutput"", ""target"": ""Tier"", ""value"": ""gold"" } ],
      ""else"":    [ { ""type"": ""setOutput"", ""target"": ""Tier"", ""value"": ""standard"" } ]
    }";

    [Fact]
    public void Else_Runs_WhenConditionFails()
    {
        foreach (RuleEvaluationResult result in new[] { Typed(TierRule), Interpreted(TierRule) })
        {
            Assert.Equal("standard", result.Outputs["Tier"]);
            FiredRule fired = Assert.Single(result.FiredRules);
            Assert.Equal("tier", fired.RuleId);
            Assert.Equal(RuleBranch.Else, fired.Branch);
            Assert.Equal("standard", fired.Outputs["Tier"]);
        }
    }

    [Fact]
    public void Then_Runs_WhenConditionMatches()
    {
        const string rule = @"{
          ""id"": ""tier"",
          ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""GreaterThan"", ""value"": 10 },
          ""actions"": [ { ""type"": ""setOutput"", ""target"": ""Tier"", ""value"": ""gold"" } ],
          ""else"":    [ { ""type"": ""setOutput"", ""target"": ""Tier"", ""value"": ""standard"" } ]
        }";

        foreach (RuleEvaluationResult result in new[] { Typed(rule), Interpreted(rule) })
        {
            Assert.Equal("gold", result.Outputs["Tier"]);
            Assert.Equal(RuleBranch.Then, Assert.Single(result.FiredRules).Branch);
        }
    }

    [Fact]
    public void NoElse_AndConditionFails_ContributesNothing()
    {
        const string rule = @"{
          ""id"": ""tier"",
          ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""GreaterThan"", ""value"": 100 },
          ""actions"": [ { ""type"": ""setOutput"", ""target"": ""Tier"", ""value"": ""gold"" } ]
        }";

        foreach (RuleEvaluationResult result in new[] { Typed(rule), Interpreted(rule) })
        {
            Assert.Empty(result.FiredRules);
            Assert.Empty(result.Outputs);
        }
    }

    [Fact]
    public void ElseBranch_DoesNotTriggerStopOnFirstMatch()
    {
        const string rules = @"{
          ""rules"": [
            { ""id"": ""a"", ""priority"": 10,
              ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""GreaterThan"", ""value"": 100 },
              ""actions"": [ { ""type"": ""setOutput"", ""target"": ""A"", ""value"": 1 } ],
              ""else"":    [ { ""type"": ""setOutput"", ""target"": ""A"", ""value"": 0 } ] },
            { ""id"": ""b"", ""priority"": 5,
              ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""GreaterThan"", ""value"": 10 },
              ""actions"": [ { ""type"": ""setOutput"", ""target"": ""B"", ""value"": 1 } ] }
          ]
        }";

        var options = new EvaluationOptions { StopOnFirstMatch = true };
        foreach (RuleEvaluationResult result in new[] { Typed(rules, options), Interpreted(rules, options) })
        {
            // a's else runs (not a match, so it does not stop); b then matches and stops.
            Assert.Equal(new[] { "a", "b" }, result.FiredRules.Select(f => f.RuleId).ToArray());
            Assert.Equal(new[] { RuleBranch.Else, RuleBranch.Then }, result.FiredRules.Select(f => f.Branch).ToArray());
            Assert.Equal(0L, result.Outputs["A"]);
            Assert.Equal(1L, result.Outputs["B"]);
        }
    }

    [Fact]
    public void ComplexElse_ComputesAndAccumulates_WithParity()
    {
        // The else branch is not a pure literal set, so it runs through the per-evaluation
        // (compiled steps / interpreter) path rather than a pre-materialized dictionary.
        const string rule = @"{
          ""id"": ""score"",
          ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""GreaterThan"", ""value"": 100 },
          ""actions"": [ { ""type"": ""addToOutput"", ""target"": ""Score"", ""value"": 100 } ],
          ""else"":    [ { ""type"": ""addToOutput"", ""target"": ""Score"", ""value"": { ""field"": ""Order.ItemCount"" } } ]
        }";

        Assert.Equal(3m, Typed(rule).Outputs["Score"]);
        Assert.Equal(3m, Interpreted(rule).Outputs["Score"]);
    }
}
