using RuleWright.Core;
using RuleWright.Json.SystemText;
using RuleWright.Serialization;
using Xunit;

namespace RuleWright.Serialization.Tests;

/// <summary>
/// <see cref="RuleSet.StopAfterFirstMatch"/> is the rule set's own semantics, but until now only a
/// <c>first</c>-hit-policy decision table could produce it - no rule-set document could say it. Any
/// tool that expands a table into its equivalent rules (the Blazor builder does exactly this) had
/// nowhere to put the flag, so a <c>first</c> table silently came back as a collecting one.
/// </summary>
public class RuleSetStopAfterFirstMatchTests
{
    private static RuleSet Parse(string json)
        => RuleSetParser.Parse(new SystemTextJsonReader().Read(json));

    private static RuleSetValidationResult Validate(string json)
        => RuleSetValidator.Validate(new SystemTextJsonReader().Read(json));

    private const string TwoRules = @"
      ""rules"": [
        { ""id"": ""a"", ""condition"": { ""field"": ""A"", ""operator"": ""IsNotNull"" },
          ""actions"": [ { ""type"": ""setOutput"", ""target"": ""Out"", ""value"": 1 } ] },
        { ""id"": ""b"", ""condition"": { ""field"": ""B"", ""operator"": ""IsNotNull"" },
          ""actions"": [ { ""type"": ""setOutput"", ""target"": ""Out"", ""value"": 2 } ] }
      ]";

    [Fact]
    public void StopAfterFirstMatch_True_IsCarriedOntoTheRuleSet()
        => Assert.True(Parse("{ \"stopAfterFirstMatch\": true, " + TwoRules + " }").StopAfterFirstMatch);

    [Fact]
    public void StopAfterFirstMatch_False_LeavesTheSetCollecting()
        => Assert.False(Parse("{ \"stopAfterFirstMatch\": false, " + TwoRules + " }").StopAfterFirstMatch);

    /// <summary>Absent means collect, which is what every existing document relies on.</summary>
    [Fact]
    public void Absent_DefaultsToCollecting()
        => Assert.False(Parse("{ " + TwoRules + " }").StopAfterFirstMatch);

    [Fact]
    public void StopAfterFirstMatch_IsAccepted()
        => Assert.True(Validate("{ \"stopAfterFirstMatch\": true, " + TwoRules + " }").IsValid);

    [Theory]
    [InlineData("\"yes\"")]
    [InlineData("1")]
    [InlineData("null")]
    public void StopAfterFirstMatch_MustBeABoolean(string badValue)
    {
        RuleSetValidationResult result = Validate(
            "{ \"stopAfterFirstMatch\": " + badValue + ", " + TwoRules + " }");

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            e => e.Path == "/stopAfterFirstMatch" && e.Message.Contains("must be a boolean"));
    }

    /// <summary>
    /// The flag belongs to a rule set. A single-rule document has no set to carry it, and a
    /// decision table states it as `hitPolicy` instead - accepting it in either place would give
    /// two spellings for one thing.
    /// </summary>
    [Fact]
    public void StopAfterFirstMatch_IsNotAcceptedOnASingleRuleDocument()
    {
        RuleSetValidationResult result = Validate(@"{
          ""id"": ""only"", ""stopAfterFirstMatch"": true,
          ""condition"": { ""field"": ""A"", ""operator"": ""IsNotNull"" },
          ""actions"": [ { ""type"": ""setOutput"", ""target"": ""Out"", ""value"": 1 } ] }");

        Assert.False(result.IsValid);
    }

    /// <summary>
    /// The losslessness direction, which an output-vs-output round-trip cannot see: a `first`
    /// table expanded to its rules and written back out must still stop after the first match.
    /// </summary>
    [Fact]
    public void FirstPolicyTable_SurvivesExpansionToARuleSetDocument()
    {
        const string table = @"{ ""decisionTable"": {
          ""name"": ""shipping"", ""hitPolicy"": ""first"",
          ""inputs"": [ { ""field"": ""Order.Total"", ""operator"": ""GreaterThanOrEqual"" } ],
          ""outputs"": [ { ""target"": ""Cost"" } ],
          ""rows"": [ { ""when"": [ 100 ], ""then"": [ 0 ] }, { ""when"": [ null ], ""then"": [ 9.95 ] } ] } }";

        RuleSet expanded = Parse(table);
        Assert.True(expanded.StopAfterFirstMatch);

        // Re-emit as a rule-set document the way a tool that expands tables would.
        string reEmitted = "{ \"name\": \"shipping\", \"stopAfterFirstMatch\": true, \"rules\": ["
            + string.Join(",", expanded.Rules.Select(r =>
                $"{{ \"id\": \"{r.Id}\", \"priority\": {r.Priority}, "
                + "\"condition\": { \"field\": \"Order.Total\", \"operator\": \"IsNotNull\" }, "
                + "\"actions\": [ { \"type\": \"setOutput\", \"target\": \"Cost\", \"value\": 0 } ] }"))
            + "] }";

        Assert.True(Validate(reEmitted).IsValid);
        Assert.True(Parse(reEmitted).StopAfterFirstMatch);
    }
}
