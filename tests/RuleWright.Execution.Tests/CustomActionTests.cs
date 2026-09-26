using RuleWright.Core;
using RuleWright.Json.SystemText;
using RuleWright.Serialization;
using Xunit;

namespace RuleWright.Execution.Tests;

/// <summary>
/// Custom action types registered via <see cref="RuleWrightBuilder.RegisterAction(string, Action{RuleActionContext})"/>.
/// The rule document names the action; the behaviour is registered C# reached through a
/// <see cref="RuleActionContext"/>, whose writes land in both the merged outputs and the
/// firing rule's own snapshot — on both execution paths identically.
/// </summary>
public class CustomActionTests
{
    private static readonly RuleWrightEngine Engine = new RuleWrightBuilder()
        .UseJsonReader(new SystemTextJsonReader())
        // Keeps the highest value ever written to the target.
        .RegisterAction("setIfHigher", context =>
        {
            if (AsDecimal(context.Value) is not decimal incoming)
            {
                return;
            }

            context.TryGetOutput(context.Target, out object? current);
            if (AsDecimal(current) is not decimal held || incoming > held)
            {
                context.SetOutput(context.Target, incoming);
            }
        })
        // Takes no value; stamps which rule and branch fired.
        .RegisterAction("stamp", context =>
            context.SetOutput(context.Target, context.RuleId + ":" + context.Branch))
        .Build();

    private static decimal? AsDecimal(object? value) => value switch
    {
        decimal d => d,
        long l => l,
        double d => (decimal)d,
        _ => null,
    };

    private static Dictionary<string, object?> DictFact() => new()
    {
        ["Customer"] = new Dictionary<string, object?> { ["Age"] = 21L },
        ["Order"] = new Dictionary<string, object?> { ["Total"] = 120.5m },
    };

    private const string TwoBids = @"{
      ""rules"": [
        { ""id"": ""low"", ""priority"": 2,
          ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""IsNotNull"" },
          ""actions"": [ { ""type"": ""setIfHigher"", ""target"": ""Bid"", ""value"": { ""op"": ""multiply"", ""operands"": [ { ""field"": ""Order.Total"" }, 0.1 ] } } ] },
        { ""id"": ""high"", ""priority"": 1,
          ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""IsNotNull"" },
          ""actions"": [ { ""type"": ""setIfHigher"", ""target"": ""Bid"", ""value"": 5 } ] }
      ]
    }";

    [Fact]
    public void CustomAction_SeesRunningOutputs_OnBothPaths()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(TwoBids);

        foreach (object fact in new object[] { TestEngine.DefaultFact(), DictFact() })
        {
            RuleEvaluationResult result = Engine.Evaluate(loaded, fact);

            // 12.05 wrote first; the later constant 5 lost the setIfHigher comparison.
            Assert.Equal(12.05m, result.Outputs["Bid"]);

            // The winning rule's snapshot shows its write; the losing rule wrote nothing.
            Assert.Equal(12.05m, result.FiredRules[0].Outputs["Bid"]);
            Assert.Empty(result.FiredRules[1].Outputs);
        }
    }

    [Fact]
    public void CustomAction_MayOmitItsValue_AndRunsOnElseBranches()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(@"{
          ""id"": ""gate"",
          ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""GreaterThan"", ""value"": 99 },
          ""actions"": [ { ""type"": ""stamp"", ""target"": ""Mark"" } ],
          ""else"":    [ { ""type"": ""stamp"", ""target"": ""Mark"" } ]
        }");

        RuleEvaluationResult result = Engine.Evaluate(loaded, TestEngine.DefaultFact());
        Assert.Equal("gate:Else", result.Outputs["Mark"]);
        Assert.Equal(RuleBranch.Else, result.FiredRules[0].Branch);
    }

    [Fact]
    public void EngineValidate_AcceptsRegisteredTypes_StaticValidateRejectsThem()
    {
        Assert.True(Engine.Validate(TwoBids).IsValid);

        RuleSetValidationResult standalone = RuleSetValidator.Validate(new SystemTextJsonReader().Read(TwoBids));
        Assert.False(standalone.IsValid);
        Assert.Contains(standalone.Errors, e => e.Message.Contains("setIfHigher") || e.Message.Contains("Action 'type'"));
    }

    [Fact]
    public void UnregisteredType_IsStillRejected_WithRegisteredTypesNamed()
    {
        RuleSetValidationResult result = Engine.Validate(
            "{\"id\":\"r\",\"condition\":{\"field\":\"A\",\"operator\":\"IsNull\"},"
            + "\"actions\":[{\"type\":\"setOutputt\",\"target\":\"T\",\"value\":1}]}");

        RuleValidationError error = Assert.Single(result.Errors);
        Assert.Equal("/actions/0/type", error.Path);
        Assert.Contains("setIfHigher", error.Message);
        Assert.Contains("stamp", error.Message);
    }

    [Fact]
    public void HandBuiltRuleSet_WithUnregisteredType_FailsAtLoad()
    {
        var rule = new Rule(
            "r",
            new ConditionLeaf("Customer.Age", ConditionOperator.IsNotNull, null),
            new[] { new RuleAction("vanish", "T", 1L) });

        RuleCompilationException ex = Assert.Throws<RuleCompilationException>(
            () => Engine.LoadRuleSet(new RuleSet(new[] { rule })));
        Assert.Contains("vanish", ex.Message);
        Assert.Contains("RegisterAction", ex.Message);
    }

    [Fact]
    public void CustomType_InDecisionTableOutputColumn()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(@"{
          ""decisionTable"": {
            ""id"": ""bids"",
            ""inputs"": [ { ""field"": ""Customer.Age"", ""operator"": ""GreaterThanOrEqual"" } ],
            ""outputs"": [ { ""target"": ""Bid"", ""type"": ""setIfHigher"" } ],
            ""rows"": [
              { ""when"": [ 18 ], ""then"": [ 10 ] },
              { ""when"": [ 0 ],  ""then"": [ 3 ] }
            ]
          }
        }");

        Assert.Equal(10m, Engine.Evaluate(loaded, TestEngine.DefaultFact()).Outputs["Bid"]);
    }

    [Fact]
    public void BuiltInTypeNames_CannotBeReRegistered()
        => Assert.Throws<ArgumentException>(
            () => new RuleWrightBuilder().RegisterAction("setOutput", _ => { }));

    [Fact]
    public void RegisteredActions_AreDiscoverable()
        => Assert.Equal(new[] { "setIfHigher", "stamp" }, Engine.RegisteredActions);
}
