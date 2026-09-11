using RuleWright.Core;
using Xunit;
using static RuleWright.Execution.Tests.TestEngine;

namespace RuleWright.Execution.Tests;

/// <summary>
/// <c>removeOutput</c> deletes a key from the running outputs, undoing what an earlier-fired
/// rule wrote. Verified on both the compiled (typed <see cref="OrderFact"/>) and interpreted
/// (dictionary) paths, which must agree.
/// </summary>
public class RemoveOutputActionTests
{
    private static Dictionary<string, object?> DictFact() => new()
    {
        ["Customer"] = new Dictionary<string, object?> { ["IsVip"] = true, ["Age"] = 21L },
        ["Order"] = new Dictionary<string, object?> { ["Total"] = 120.50m },
    };

    private static RuleEvaluationResult Typed(string rulesJson)
        => Engine.Evaluate(Engine.LoadRuleSet(rulesJson), DefaultFact());

    private static RuleEvaluationResult Interpreted(string rulesJson)
        => Engine.Evaluate(Engine.LoadRuleSet(rulesJson), DictFact());

    [Fact]
    public void RemoveOutput_DeletesKeyWrittenByEarlierRule()
    {
        const string rules = @"{
          ""rules"": [
            { ""id"": ""grant"", ""priority"": 10,
              ""condition"": { ""field"": ""Customer.IsVip"", ""operator"": ""Equals"", ""value"": true },
              ""actions"": [ { ""type"": ""setOutput"", ""target"": ""Discount"", ""value"": 10 } ] },
            { ""id"": ""revoke"", ""priority"": 5,
              ""condition"": { ""field"": ""Order.Total"", ""operator"": ""LessThan"", ""value"": 200 },
              ""actions"": [ { ""type"": ""removeOutput"", ""target"": ""Discount"" } ] }
          ]
        }";

        foreach (RuleEvaluationResult result in new[] { Typed(rules), Interpreted(rules) })
        {
            Assert.False(result.Outputs.ContainsKey("Discount"));
            // Both rules still fired; the remover's snapshot reports no value for the removed key.
            Assert.Equal(new[] { "grant", "revoke" }, result.FiredRules.Select(f => f.RuleId).ToArray());
            Assert.False(result.FiredRules[1].Outputs.ContainsKey("Discount"));
        }
    }

    [Fact]
    public void RemoveOutput_OnMissingKey_IsNoOp()
    {
        const string rules = @"{
          ""id"": ""r"",
          ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""IsNotNull"" },
          ""actions"": [ { ""type"": ""removeOutput"", ""target"": ""Nonexistent"" } ]
        }";

        foreach (RuleEvaluationResult result in new[] { Typed(rules), Interpreted(rules) })
        {
            Assert.Empty(result.Outputs);
            Assert.Empty(Assert.Single(result.FiredRules).Outputs);
        }
    }

    [Fact]
    public void RemoveOutput_AfterSetOnSameTarget_WithinOneRule()
    {
        const string rules = @"{
          ""id"": ""r"",
          ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""IsNotNull"" },
          ""actions"": [
            { ""type"": ""setOutput"", ""target"": ""X"", ""value"": 1 },
            { ""type"": ""removeOutput"", ""target"": ""X"" } ]
        }";

        foreach (RuleEvaluationResult result in new[] { Typed(rules), Interpreted(rules) })
        {
            Assert.False(result.Outputs.ContainsKey("X"));
            Assert.False(Assert.Single(result.FiredRules).Outputs.ContainsKey("X"));
        }
    }

    [Fact]
    public void RemoveOutput_FromElseBranch()
    {
        // The condition fails, so the else branch runs and removes an earlier rule's output.
        const string rules = @"{
          ""rules"": [
            { ""id"": ""grant"", ""priority"": 10,
              ""condition"": { ""field"": ""Customer.IsVip"", ""operator"": ""Equals"", ""value"": true },
              ""actions"": [ { ""type"": ""setOutput"", ""target"": ""Discount"", ""value"": 10 } ] },
            { ""id"": ""gate"", ""priority"": 5,
              ""condition"": { ""field"": ""Order.Total"", ""operator"": ""GreaterThan"", ""value"": 1000 },
              ""actions"": [ { ""type"": ""setOutput"", ""target"": ""Note"", ""value"": ""big order"" } ],
              ""else"":    [ { ""type"": ""removeOutput"", ""target"": ""Discount"" } ] }
          ]
        }";

        foreach (RuleEvaluationResult result in new[] { Typed(rules), Interpreted(rules) })
        {
            Assert.False(result.Outputs.ContainsKey("Discount"));
            Assert.Equal(RuleBranch.Else, result.FiredRules[1].Branch);
        }
    }
}
