using RuleWright.Core;
using Xunit;
using static RuleWright.Execution.Tests.TestEngine;

namespace RuleWright.Execution.Tests;

public class TraceTests
{
    private const string SpecRule = @"{
      ""id"": ""discount-rule-01"",
      ""condition"": {
        ""type"": ""group"", ""operator"": ""AND"",
        ""rules"": [
          { ""field"": ""Customer.Age"", ""operator"": ""GreaterThan"", ""value"": 18 },
          {
            ""type"": ""group"", ""operator"": ""OR"",
            ""rules"": [
              { ""field"": ""Order.Total"", ""operator"": ""GreaterThanOrEqual"", ""value"": 100 },
              { ""field"": ""Customer.IsVip"", ""operator"": ""Equals"", ""value"": true }
            ]
          }
        ]
      }
    }";

    [Fact]
    public void TraceDisabled_ResultHasNoTrace()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(SpecRule);
        Assert.Null(Engine.Evaluate(loaded, DefaultFact()).Trace);
    }

    [Fact]
    public void Trace_RecordsPerNodeOutcomesAndDescriptions()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(SpecRule);
        RuleEvaluationResult result = Engine.Evaluate(loaded, DefaultFact(), new EvaluationOptions { EnableTrace = true });

        RuleTrace ruleTrace = Assert.Single(result.Trace!.Rules);
        Assert.True(ruleTrace.Fired);
        Assert.False(ruleTrace.Skipped);

        ConditionTraceNode root = ruleTrace.Condition!;
        Assert.Equal("AND", root.Description);
        Assert.True(root.Passed);
        Assert.Equal(2, root.Children.Count);

        ConditionTraceNode age = root.Children[0];
        Assert.Equal("Customer.Age GreaterThan 18", age.Description);
        Assert.True(age.Passed);

        ConditionTraceNode or = root.Children[1];
        Assert.Equal("OR", or.Description);
        Assert.True(or.Passed);

        // Order.Total >= 100 already passed, so OR short-circuits: the IsVip leaf never ran.
        Assert.True(or.Children[0].Passed);
        Assert.Null(or.Children[1].Passed);
    }

    [Fact]
    public void Trace_AndShortCircuit_MarksUnreachedNodesNull()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(SpecRule);
        OrderFact minor = DefaultFact();
        minor.Customer.Age = 16;

        RuleEvaluationResult result = Engine.Evaluate(loaded, minor, new EvaluationOptions { EnableTrace = true });
        ConditionTraceNode root = result.Trace!.Rules[0].Condition!;

        Assert.False(root.Passed);
        Assert.False(root.Children[0].Passed);       // Age > 18 failed
        Assert.Null(root.Children[1].Passed);        // OR group never evaluated
        Assert.Null(root.Children[1].Children[0].Passed);
        Assert.Null(root.Children[1].Children[1].Passed);
    }

    [Fact]
    public void Trace_InterpretedPath_MatchesCompiledShape()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(SpecRule);
        var fact = new Dictionary<string, object?>
        {
            ["Customer"] = new Dictionary<string, object?> { ["Age"] = 21L, ["IsVip"] = false },
            ["Order"] = new Dictionary<string, object?> { ["Total"] = 200L },
        };

        RuleEvaluationResult result = Engine.Evaluate(loaded, fact, new EvaluationOptions { EnableTrace = true });
        Assert.Equal(CompilationMode.Interpreted, result.CompilationMode);

        ConditionTraceNode root = result.Trace!.Rules[0].Condition!;
        Assert.True(root.Passed);
        Assert.True(root.Children[1].Children[0].Passed);
        Assert.Null(root.Children[1].Children[1].Passed); // OR short-circuited, same as compiled
    }
}
