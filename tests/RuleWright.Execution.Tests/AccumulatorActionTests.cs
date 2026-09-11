using RuleWright.Core;
using Xunit;
using static RuleWright.Execution.Tests.TestEngine;

namespace RuleWright.Execution.Tests;

/// <summary>
/// <c>addToOutput</c> and <c>appendToOutput</c> accumulate across fired rules in priority
/// order. Verified on both the compiled (typed <see cref="OrderFact"/>) and interpreted
/// (dictionary) paths, which must agree.
/// </summary>
public class AccumulatorActionTests
{
    private const string TwoAccumulatorRules = @"{
      ""rules"": [
        { ""id"": ""r1"", ""priority"": 10, ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""IsNotNull"" },
          ""actions"": [
            { ""type"": ""addToOutput"", ""target"": ""Score"", ""value"": 10 },
            { ""type"": ""appendToOutput"", ""target"": ""Reasons"", ""value"": ""high-value"" } ] },
        { ""id"": ""r2"", ""priority"": 5, ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""IsNotNull"" },
          ""actions"": [
            { ""type"": ""addToOutput"", ""target"": ""Score"", ""value"": 5 },
            { ""type"": ""appendToOutput"", ""target"": ""Reasons"", ""value"": ""loyal"" } ] }
      ]
    }";

    private static Dictionary<string, object?> DictFact() => new()
    {
        ["Customer"] = new Dictionary<string, object?> { ["Age"] = 21L, ["Name"] = "Alice" },
        ["Order"] = new Dictionary<string, object?> { ["ItemCount"] = 3L, ["Total"] = 120.5m },
    };

    private static RuleEvaluationResult Typed(string rulesJson) => Engine.Evaluate(Engine.LoadRuleSet(rulesJson), DefaultFact());

    private static RuleEvaluationResult Interpreted(string rulesJson) => Engine.Evaluate(Engine.LoadRuleSet(rulesJson), DictFact());

    private static object?[] List(RuleEvaluationResult result, string key)
        => ((List<object?>)result.Outputs[key]!).ToArray();

    [Fact]
    public void AddAndAppend_AccumulateAcrossRules()
    {
        foreach (RuleEvaluationResult result in new[] { Typed(TwoAccumulatorRules), Interpreted(TwoAccumulatorRules) })
        {
            Assert.Equal(15m, result.Outputs["Score"]);
            Assert.Equal(new object?[] { "high-value", "loyal" }, List(result, "Reasons"));
        }
    }

    [Fact]
    public void FiredRuleSnapshots_ReflectStateAtEachFiring()
    {
        RuleEvaluationResult result = Typed(TwoAccumulatorRules);

        Assert.Equal(new[] { "r1", "r2" }, result.FiredRules.Select(f => f.RuleId).ToArray());

        // r1's snapshot is frozen at its firing — copy-on-append means r2 does not mutate it.
        Assert.Equal(10m, result.FiredRules[0].Outputs["Score"]);
        Assert.Equal(new object?[] { "high-value" }, ((List<object?>)result.FiredRules[0].Outputs["Reasons"]!).ToArray());

        Assert.Equal(15m, result.FiredRules[1].Outputs["Score"]);
        Assert.Equal(new object?[] { "high-value", "loyal" }, ((List<object?>)result.FiredRules[1].Outputs["Reasons"]!).ToArray());
    }

    [Fact]
    public void NullValue_ContributesNothing()
    {
        const string rules = @"{
          ""rules"": [
            { ""id"": ""r1"", ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""IsNotNull"" },
              ""actions"": [
                { ""type"": ""addToOutput"", ""target"": ""Score"", ""value"": 10 },
                { ""type"": ""appendToOutput"", ""target"": ""Reasons"", ""value"": ""a"" } ] },
            { ""id"": ""r2"", ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""IsNotNull"" },
              ""actions"": [
                { ""type"": ""addToOutput"", ""target"": ""Score"", ""value"": { ""literal"": null } },
                { ""type"": ""appendToOutput"", ""target"": ""Reasons"", ""value"": { ""literal"": null } } ] }
          ]
        }";

        foreach (RuleEvaluationResult result in new[] { Typed(rules), Interpreted(rules) })
        {
            Assert.Equal(10m, result.Outputs["Score"]);
            Assert.Equal(new object?[] { "a" }, List(result, "Reasons"));
        }
    }

    [Fact]
    public void AddToOutput_InitializesFromZeroAsDecimal()
    {
        const string rules = @"{
          ""rules"": [
            { ""id"": ""r1"", ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""IsNotNull"" },
              ""actions"": [ { ""type"": ""addToOutput"", ""target"": ""Score"", ""value"": 7 } ] } ] }";

        RuleEvaluationResult result = Typed(rules);
        Assert.IsType<decimal>(result.Outputs["Score"]);
        Assert.Equal(7m, result.Outputs["Score"]);
    }

    [Fact]
    public void AddToOutput_ComputedValue_HasParity()
    {
        const string rules = @"{
          ""rules"": [
            { ""id"": ""r1"", ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""IsNotNull"" },
              ""actions"": [ { ""type"": ""addToOutput"", ""target"": ""Sum"", ""value"": { ""field"": ""Order.ItemCount"" } } ] } ] }";

        Assert.Equal(3m, Typed(rules).Outputs["Sum"]);
        Assert.Equal(3m, Interpreted(rules).Outputs["Sum"]);
    }

    [Fact]
    public void SetOutput_ThenAddToOutput_OnSameTarget()
    {
        const string rules = @"{
          ""rules"": [
            { ""id"": ""base"", ""priority"": 10, ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""IsNotNull"" },
              ""actions"": [ { ""type"": ""setOutput"", ""target"": ""Score"", ""value"": 100 } ] },
            { ""id"": ""bonus"", ""priority"": 5, ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""IsNotNull"" },
              ""actions"": [ { ""type"": ""addToOutput"", ""target"": ""Score"", ""value"": 10 } ] }
          ]
        }";

        Assert.Equal(110m, Typed(rules).Outputs["Score"]);
        Assert.Equal(110m, Interpreted(rules).Outputs["Score"]);
    }

    [Fact]
    public void NonNumericContribution_IsIgnored()
    {
        const string rules = @"{
          ""rules"": [
            { ""id"": ""r1"", ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""IsNotNull"" },
              ""actions"": [ { ""type"": ""addToOutput"", ""target"": ""Score"", ""value"": 10 } ] },
            { ""id"": ""r2"", ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""IsNotNull"" },
              ""actions"": [ { ""type"": ""addToOutput"", ""target"": ""Score"", ""value"": ""oops"" } ] }
          ]
        }";

        Assert.Equal(10m, Typed(rules).Outputs["Score"]);
        Assert.Equal(10m, Interpreted(rules).Outputs["Score"]);
    }

    [Fact]
    public void AppendToOutput_ProducesAList()
    {
        const string rules = @"{
          ""rules"": [
            { ""id"": ""r1"", ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""IsNotNull"" },
              ""actions"": [ { ""type"": ""appendToOutput"", ""target"": ""Tags"", ""value"": ""x"" } ] } ] }";

        RuleEvaluationResult result = Typed(rules);
        var list = Assert.IsType<List<object?>>(result.Outputs["Tags"]);
        Assert.Equal(new object?[] { "x" }, list.ToArray());
    }
}
