using RuleWright.Core;
using Xunit;
using static RuleWright.Execution.Tests.TestEngine;

namespace RuleWright.Execution.Tests;

/// <summary>
/// <see cref="RuleSet.Merge"/>: composing several rule sets into one at load time —
/// RuleWright's equivalent of workflow injection, kept out of the JSON so documents stay
/// self-contained.
/// </summary>
public class RuleSetMergeTests
{
    private static RuleSet Parse(string json)
        => Engine.LoadRuleSet(json).RuleSet;

    private const string BaseRules = @"{
      ""name"": ""base"",
      ""rules"": [
        { ""id"": ""adult"", ""priority"": 5,
          ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""GreaterThanOrEqual"", ""value"": 18 },
          ""actions"": [ { ""type"": ""setOutput"", ""target"": ""Adult"", ""value"": true } ] }
      ]
    }";

    private const string SeasonalRules = @"{
      ""name"": ""seasonal"",
      ""rules"": [
        { ""id"": ""coupon"", ""priority"": 10,
          ""condition"": { ""field"": ""Order.Coupon"", ""operator"": ""Equals"", ""value"": ""SPRING"" },
          ""actions"": [ { ""type"": ""setOutput"", ""target"": ""CouponApplied"", ""value"": true } ] }
      ]
    }";

    [Fact]
    public void MergedSets_EvaluateAsOne_WithPriorityAcrossSources()
    {
        RuleSet merged = RuleSet.Merge(new[] { Parse(BaseRules), Parse(SeasonalRules) }, "combined");
        LoadedRuleSet loaded = Engine.LoadRuleSet(merged);

        RuleEvaluationResult result = Engine.Evaluate(loaded, DefaultFact());

        Assert.Equal("combined", merged.Name);
        Assert.True((bool)result.Outputs["Adult"]!);
        Assert.True((bool)result.Outputs["CouponApplied"]!);

        // Priority orders evaluation across the merged whole, not per source set.
        Assert.Equal(new[] { "coupon", "adult" }, result.FiredRules.Select(r => r.RuleId).ToArray());
    }

    [Fact]
    public void DuplicateRuleIds_AcrossSets_FailLoudly()
    {
        RuleSet one = Parse(BaseRules);
        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => RuleSet.Merge(new[] { one, Parse(BaseRules) }));
        Assert.Contains("adult", ex.Message);
    }

    [Fact]
    public void EmptyOrNullSources_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => RuleSet.Merge(Array.Empty<RuleSet>()));
        Assert.Throws<ArgumentNullException>(() => RuleSet.Merge(null!));
        Assert.Throws<ArgumentNullException>(() => RuleSet.Merge(new RuleSet[] { null! }));
    }

    [Fact]
    public void StopAfterFirstMatch_IsTheComposersChoice()
    {
        RuleSet merged = RuleSet.Merge(
            new[] { Parse(BaseRules), Parse(SeasonalRules) }, stopAfterFirstMatch: true);

        RuleEvaluationResult result = Engine.Evaluate(Engine.LoadRuleSet(merged), DefaultFact());
        Assert.Single(result.FiredRules);
    }
}
