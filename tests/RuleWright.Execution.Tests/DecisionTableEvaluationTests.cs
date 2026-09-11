using RuleWright.Core;
using Xunit;
using static RuleWright.Execution.Tests.TestEngine;

namespace RuleWright.Execution.Tests;

/// <summary>
/// Decision tables expand to ordinary rules at load time, so they run through the normal
/// evaluation path. These tests exercise the observable behavior end to end for both hit
/// policies and both fact shapes.
/// </summary>
public class DecisionTableEvaluationTests
{
    private const string Inputs =
        @"""inputs"": [
            { ""field"": ""Customer.Tier"", ""operator"": ""Equals"" },
            { ""field"": ""Order.Total"", ""operator"": ""GreaterThanOrEqual"" } ]";

    private const string Rows =
        @"""rows"": [
            { ""when"": [""VIP"", 100], ""then"": [20] },
            { ""when"": [""VIP"", null], ""then"": [10] },
            { ""when"": [null, null], ""then"": [0] } ]";

    private static Dictionary<string, object?> Fact(string tier, long total) => new()
    {
        ["Customer"] = new Dictionary<string, object?> { ["Tier"] = tier },
        ["Order"] = new Dictionary<string, object?> { ["Total"] = total },
    };

    [Fact]
    public void CollectPolicy_AccumulatesEveryMatchingRow()
    {
        string table = "{ \"decisionTable\": { \"hitPolicy\": \"collect\", " + Inputs
            + ", \"outputs\": [ { \"target\": \"Score\", \"type\": \"addToOutput\" } ], "
            + Rows + " } }";

        LoadedRuleSet loaded = Engine.LoadRuleSet(table);
        RuleEvaluationResult result = Engine.Evaluate(loaded, Fact("VIP", 120));

        // All three rows match VIP/120, so their contributions accumulate: 20 + 10 + 0.
        Assert.Equal(3, result.FiredRules.Count);
        Assert.Equal(30m, result.Outputs["Score"]);
    }

    [Theory]
    [InlineData("VIP", 120, "row-0", 20L)]  // first row matches
    [InlineData("VIP", 50, "row-1", 10L)]   // total too low; falls to second row
    [InlineData("Bronze", 10, "row-2", 0L)] // nothing specific matches; catch-all
    public void FirstPolicy_OnlyFirstMatchingRowApplies(string tier, long total, string expectedRule, long expectedDiscount)
    {
        string table = "{ \"decisionTable\": { \"hitPolicy\": \"first\", " + Inputs
            + ", \"outputs\": [ { \"target\": \"Discount\" } ], " + Rows + " } }";

        LoadedRuleSet loaded = Engine.LoadRuleSet(table);
        RuleEvaluationResult result = Engine.Evaluate(loaded, Fact(tier, total));

        FiredRule fired = Assert.Single(result.FiredRules);
        Assert.Equal(expectedRule, fired.RuleId);
        Assert.Equal(expectedDiscount, result.Outputs["Discount"]);
    }

    [Fact]
    public void ExpandedTable_RunsOnTheCompiledPath()
    {
        // Age/Total-based table so it binds to the typed OrderFact (Age = 21).
        const string table = @"{ ""decisionTable"": {
            ""hitPolicy"": ""first"",
            ""inputs"": [ { ""field"": ""Customer.Age"", ""operator"": ""GreaterThanOrEqual"" } ],
            ""outputs"": [ { ""target"": ""Band"" } ],
            ""rows"": [
              { ""when"": [65], ""then"": [""senior""] },
              { ""when"": [18], ""then"": [""adult""] },
              { ""when"": [null], ""then"": [""minor""] } ] } }";

        LoadedRuleSet loaded = Engine.LoadRuleSet(table);
        RuleEvaluationResult result = Engine.Evaluate(loaded, DefaultFact());

        Assert.Equal(CompilationMode.Compiled, result.CompilationMode);
        Assert.Equal("adult", result.Outputs["Band"]);
        Assert.Equal("row-1", Assert.Single(result.FiredRules).RuleId);
    }
}
