using Rulewright.Core;
using Rulewright.Serialization;
using Xunit;
using static Rulewright.Execution.Tests.TestEngine;

namespace Rulewright.Execution.Tests;

public class EngineBehaviorTests
{
    private const string TwoRuleSet = @"{
      ""rules"": [
        {
          ""id"": ""low"", ""priority"": 1,
          ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""GreaterThan"", ""value"": 18 },
          ""actions"": [ { ""type"": ""setOutput"", ""target"": ""Discount"", ""value"": 5 } ]
        },
        {
          ""id"": ""high"", ""priority"": 10,
          ""condition"": { ""field"": ""Customer.IsVip"", ""operator"": ""Equals"", ""value"": true },
          ""actions"": [ { ""type"": ""setOutput"", ""target"": ""Discount"", ""value"": 10 } ]
        }
      ]
    }";

    [Fact]
    public void HigherPriorityRulesEvaluateFirst()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(TwoRuleSet);
        RuleEvaluationResult result = Engine.Evaluate(loaded, DefaultFact());

        Assert.Equal(new[] { "high", "low" }, result.FiredRules.Select(r => r.RuleId).ToArray());
        // Both fired; the later (lower-priority) rule's output wins the merge.
        Assert.Equal(5L, result.Outputs["Discount"]);
    }

    [Fact]
    public void StopOnFirstMatch_SkipsRemainingRules()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(TwoRuleSet);
        RuleEvaluationResult result = Engine.Evaluate(
            loaded, DefaultFact(), new EvaluationOptions { StopOnFirstMatch = true, EnableTrace = true });

        FiredRule fired = Assert.Single(result.FiredRules);
        Assert.Equal("high", fired.RuleId);
        Assert.Equal(10L, result.Outputs["Discount"]);

        RuleTrace lowTrace = result.Trace!.Rules.Single(r => r.RuleId == "low");
        Assert.True(lowTrace.Skipped);
        Assert.False(lowTrace.Fired);
    }

    [Fact]
    public void EqualPriority_KeepsDocumentOrder()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(@"{
          ""rules"": [
            { ""id"": ""first"", ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""IsNotNull"" } },
            { ""id"": ""second"", ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""IsNotNull"" } }
          ]
        }");
        RuleEvaluationResult result = Engine.Evaluate(loaded, DefaultFact());
        Assert.Equal(new[] { "first", "second" }, result.FiredRules.Select(r => r.RuleId).ToArray());
    }

    [Fact]
    public void DisabledRules_AreSkipped()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(@"{
          ""rules"": [
            { ""id"": ""off"", ""enabled"": false, ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""IsNotNull"" } },
            { ""id"": ""on"", ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""IsNotNull"" } }
          ]
        }");
        RuleEvaluationResult result = Engine.Evaluate(loaded, DefaultFact(), new EvaluationOptions { EnableTrace = true });

        Assert.Equal("on", Assert.Single(result.FiredRules).RuleId);
        RuleTrace offTrace = result.Trace!.Rules.Single(r => r.RuleId == "off");
        Assert.True(offTrace.Skipped);
        Assert.Null(offTrace.Condition);
    }

    [Fact]
    public void NoMatch_ReturnsEmptyResult()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(WrapRule("{\"field\":\"Customer.Age\",\"operator\":\"GreaterThan\",\"value\":99}"));
        RuleEvaluationResult result = Engine.Evaluate(loaded, DefaultFact());
        Assert.Empty(result.FiredRules);
        Assert.Empty(result.Outputs);
    }

    [Fact]
    public void ChangedRuleBody_SameId_IsRecompiledNotServedStale()
    {
        LoadedRuleSet v1 = Engine.LoadRuleSet(WrapRule("{\"field\":\"Customer.Age\",\"operator\":\"GreaterThan\",\"value\":100}"));
        Assert.Empty(Engine.Evaluate(v1, DefaultFact()).FiredRules);

        // Same rule id, different body: the content-hash cache key must differ.
        LoadedRuleSet v2 = Engine.LoadRuleSet(WrapRule("{\"field\":\"Customer.Age\",\"operator\":\"GreaterThan\",\"value\":10}"));
        Assert.Single(Engine.Evaluate(v2, DefaultFact()).FiredRules);
    }

    [Fact]
    public void ConcurrentEvaluations_AreConsistent()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(TwoRuleSet);
        OrderFact fact = DefaultFact();

        Parallel.For(0, 500, i =>
        {
            var options = new EvaluationOptions { EnableTrace = i % 2 == 0 };
            RuleEvaluationResult result = Engine.Evaluate(loaded, fact, options);
            Assert.Equal(2, result.FiredRules.Count);
            Assert.Equal(5L, result.Outputs["Discount"]);
        });
    }

    [Fact]
    public void NullFact_Throws()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(WrapRule("{\"field\":\"Customer.Age\",\"operator\":\"IsNotNull\"}"));
        Assert.Throws<ArgumentNullException>(() => Engine.Evaluate<OrderFact>(loaded, null!));
    }

    [Fact]
    public void EngineWithoutReader_RejectsJsonButAcceptsDomainRuleSets()
    {
        RulewrightEngine engine = new RulewrightBuilder().Build();
        Assert.Throws<InvalidOperationException>(() => engine.LoadRuleSet("{}"));

        var rule = new Rule("r", new ConditionLeaf("Customer.Age", ConditionOperator.GreaterThan, 18L));
        LoadedRuleSet loaded = engine.LoadRuleSet(new RuleSet(new[] { rule }));
        Assert.Single(engine.Evaluate(loaded, DefaultFact()).FiredRules);
    }

    [Fact]
    public void Validate_ReturnsPointerErrorsWithoutThrowing()
    {
        RuleSetValidationResult malformed = Engine.Validate("{ not json");
        Assert.False(malformed.IsValid);
        Assert.Equal(string.Empty, malformed.Errors.Single().Path);

        RuleSetValidationResult invalid = Engine.Validate("{\"id\":\"r\",\"condition\":{\"field\":\"A\",\"operator\":\"Nope\",\"value\":1}}");
        Assert.False(invalid.IsValid);
        Assert.Equal("/condition/operator", invalid.Errors.Single().Path);

        Assert.True(Engine.Validate(WrapRule("{\"field\":\"A\",\"operator\":\"IsNull\"}")).IsValid);
    }

    [Fact]
    public void FiredRuleOutputs_ArePerRule()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(TwoRuleSet);
        RuleEvaluationResult result = Engine.Evaluate(loaded, DefaultFact());
        Assert.Equal(10L, result.FiredRules[0].Outputs["Discount"]);
        Assert.Equal(5L, result.FiredRules[1].Outputs["Discount"]);
    }

    /// <summary>
    /// The domain model is immutable, so sharing one condition instance in several places is a
    /// natural thing to write. Node bookkeeping is positional, not identity-keyed, so a reused
    /// instance evaluates and traces once per position instead of colliding.
    /// </summary>
    [Fact]
    public void SharedConditionInstance_EvaluatesAndTracesPerPosition()
    {
        var shared = new ConditionLeaf("Customer.Age", ConditionOperator.GreaterThan, 18L);
        var group = new ConditionGroup(LogicalOperator.And, new ConditionNode[] { shared, shared });
        var ruleSet = new RuleSet(new[]
        {
            new Rule("shared", group, new[] { new RuleAction("setOutput", "Ok", true) }),
        });

        LoadedRuleSet loaded = Engine.LoadRuleSet(ruleSet);
        RuleEvaluationResult result = Engine.Evaluate(loaded, DefaultFact(), new EvaluationOptions { EnableTrace = true });

        Assert.Single(result.FiredRules);
        Assert.Equal(true, result.Outputs["Ok"]);

        ConditionTraceNode root = result.Trace!.Rules.Single().Condition!;
        Assert.Equal(2, root.Children!.Count);
        Assert.All(root.Children, child => Assert.True(child.Passed));
    }

    /// <summary>The same, on the interpreter - a shared instance must not confuse its trace slots.</summary>
    [Fact]
    public void SharedConditionInstance_TracesPerPosition_Interpreted()
    {
        var passes = new ConditionLeaf("Age", ConditionOperator.GreaterThan, 18L);
        var fails = new ConditionLeaf("Age", ConditionOperator.GreaterThan, 999L);
        var group = new ConditionGroup(LogicalOperator.Or, new ConditionNode[] { fails, passes, fails });
        var ruleSet = new RuleSet(new[] { new Rule("shared", group) });

        LoadedRuleSet loaded = Engine.LoadRuleSet(ruleSet);
        var fact = new Dictionary<string, object?> { ["Age"] = 21L };
        RuleEvaluationResult result = Engine.Evaluate(loaded, fact, new EvaluationOptions { EnableTrace = true });

        ConditionTraceNode root = result.Trace!.Rules.Single().Condition!;
        Assert.Equal(3, root.Children!.Count);
        Assert.False(root.Children[0].Passed);
        Assert.True(root.Children[1].Passed);
        // OR short-circuits after the second child, so the third was never reached.
        Assert.Null(root.Children[2].Passed);
    }
}
