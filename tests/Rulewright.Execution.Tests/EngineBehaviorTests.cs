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

    /// <summary>
    /// The document's own "stop after the first match", rather than the caller's option. A tool
    /// that expands a `first` decision table into its equivalent rules writes this, so the expanded
    /// form must evaluate the same way the table did.
    /// </summary>
    [Fact]
    public void StopAfterFirstMatchDocument_SkipsRemainingRules_WithoutTheCallerAskingForIt()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(
            "{ \"stopAfterFirstMatch\": true, " + TwoRuleSet.TrimStart().TrimStart('{'));

        // Default options: the caller does NOT pass StopOnFirstMatch.
        RuleEvaluationResult result = Engine.Evaluate(loaded, DefaultFact(), new EvaluationOptions { EnableTrace = true });

        FiredRule fired = Assert.Single(result.FiredRules);
        Assert.Equal("high", fired.RuleId);
        Assert.Equal(10L, result.Outputs["Discount"]);
        Assert.True(result.Trace!.Rules.Single(r => r.RuleId == "low").Skipped);
    }

    /// <summary>Absent means collect - every document written before the property is unaffected.</summary>
    [Fact]
    public void WithoutTheDocumentFlag_AllMatchingRulesStillFire()
        => Assert.Equal(2, Engine.Evaluate(Engine.LoadRuleSet(TwoRuleSet), DefaultFact()).FiredRules.Count);

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
        Assert.Equal(2, root.Children.Count);
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
        Assert.Equal(3, root.Children.Count);
        Assert.False(root.Children[0].Passed);
        Assert.True(root.Children[1].Passed);
        // OR short-circuits after the second child, so the third was never reached.
        Assert.Null(root.Children[2].Passed);
    }

    // --- Load-time operand guard: both paths fail the same way, at the same moment ---

    /// <summary>
    /// A hand-built rule reaches the engine without passing through the JSON validator, so the
    /// engine screens operand shapes itself. Without this the failure was an InvalidCastException
    /// from inside the compiler, or - for dictionary facts - mid-evaluation.
    /// </summary>
    [Theory]
    [MemberData(nameof(MalformedLeaves))]
    public void MalformedOperand_IsARuleCompilationExceptionAtLoad(ConditionLeaf leaf)
    {
        var ruleSet = new RuleSet(new[] { new Rule("bad", leaf) });
        RuleCompilationException error = Assert.Throws<RuleCompilationException>(() => Engine.LoadRuleSet(ruleSet));
        Assert.Contains("bad", error.Message, StringComparison.Ordinal);
    }

    public static TheoryData<ConditionLeaf> MalformedLeaves() => new TheoryData<ConditionLeaf>
    {
        new ConditionLeaf("Customer.Name", ConditionOperator.Contains, 42L),        // string op, number operand
        new ConditionLeaf("Customer.Name", ConditionOperator.StartsWith, null),     // string op, no operand
        new ConditionLeaf("Customer.Name", ConditionOperator.MatchesRegex, "("),    // unparsable pattern
        new ConditionLeaf("Customer.Tier", ConditionOperator.In, "gold"),           // set op, scalar operand
        new ConditionLeaf("Customer.Age", ConditionOperator.GreaterThan, null),     // ordering, null operand
    };

    // --- Fact shapes that have no compile-time shape fall back to the interpreter ---

    /// <summary>
    /// A fact whose *static* type is object would compile field paths against System.Object and
    /// bind to nothing. The interpreter resolves against the runtime type instead, and the result
    /// says so rather than the call simply failing.
    /// </summary>
    [Fact]
    public void ObjectTypedFact_RunsInterpretedInsteadOfFailingToCompile()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(TwoRuleSet);
        object fact = DefaultFact();

        RuleEvaluationResult result = Engine.Evaluate(loaded, fact);

        Assert.Equal(CompilationMode.Interpreted, result.CompilationMode);
        Assert.Equal(2, result.FiredRules.Count);
        Assert.Equal(new[] { "high", "low" }, result.FiredRules.Select(r => r.RuleId).ToArray());
        Assert.Equal(5L, result.Outputs["Discount"]);   // "low" has the lower priority, so it writes last
    }

    /// <summary>A read-only dictionary is a dictionary fact too.</summary>
    [Fact]
    public void ReadOnlyDictionaryFact_RunsInterpreted()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(TwoRuleSet);
        IReadOnlyDictionary<string, object?> fact = new Dictionary<string, object?>
        {
            ["Customer"] = new Dictionary<string, object?> { ["Age"] = 30L, ["IsVip"] = true },
        };

        RuleEvaluationResult result = Engine.Evaluate(loaded, fact);

        Assert.Equal(CompilationMode.Interpreted, result.CompilationMode);
        Assert.Equal(2, result.FiredRules.Count);
        Assert.Equal(5L, result.Outputs["Discount"]);
    }

    // --- Outputs ---

    /// <summary>
    /// addToOutput accumulates numbers. A target already holding something non-numeric is left
    /// alone rather than being replaced with null - one rule must not wipe what another wrote.
    /// </summary>
    [Fact]
    public void AddToOutput_LeavesANonNumericTargetUntouched()
    {
        const string json = @"{ ""rules"": [
            { ""id"": ""label"", ""priority"": 2,
              ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""GreaterThan"", ""value"": 1 },
              ""actions"": [ { ""type"": ""setOutput"", ""target"": ""Score"", ""value"": ""N/A"" } ] },
            { ""id"": ""bump"", ""priority"": 1,
              ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""GreaterThan"", ""value"": 1 },
              ""actions"": [ { ""type"": ""addToOutput"", ""target"": ""Score"", ""value"": 5 } ] } ] }";

        RuleEvaluationResult result = Engine.Evaluate(Engine.LoadRuleSet(json), DefaultFact());

        Assert.Equal("N/A", result.Outputs["Score"]);
    }

    // --- Trace ---

    /// <summary>
    /// "The author turned this off" and "evaluation had already finished" are very different
    /// facts about a rule, and a trace that renders both as Skipped=true cannot tell them apart.
    /// </summary>
    [Fact]
    public void Trace_DistinguishesDisabledFromStoppedAfterMatch()
    {
        const string json = @"{ ""rules"": [
            { ""id"": ""off"", ""priority"": 4, ""enabled"": false, ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""GreaterThan"", ""value"": 1 }, ""actions"": [] },
            { ""id"": ""hit"", ""priority"": 3, ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""GreaterThan"", ""value"": 1 }, ""actions"": [] },
            { ""id"": ""later"", ""priority"": 1, ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""GreaterThan"", ""value"": 1 }, ""actions"": [] } ] }";

        RuleEvaluationResult result = Engine.Evaluate(
            Engine.LoadRuleSet(json),
            DefaultFact(),
            new EvaluationOptions { EnableTrace = true, StopOnFirstMatch = true });

        RuleTrace hit = result.Trace!.Rules.Single(r => r.RuleId == "hit");
        RuleTrace off = result.Trace.Rules.Single(r => r.RuleId == "off");
        RuleTrace later = result.Trace.Rules.Single(r => r.RuleId == "later");

        Assert.Equal(RuleSkipReason.None, hit.SkipReason);
        Assert.True(hit.Fired);
        Assert.Equal(RuleSkipReason.Disabled, off.SkipReason);
        Assert.Equal(RuleSkipReason.StoppedAfterMatch, later.SkipReason);
        Assert.True(off.Skipped && later.Skipped);
    }

    /// <summary>A computed left-hand side names what was computed, not "(fact)".</summary>
    [Fact]
    public void Trace_DescribesAComputedLeftHandSide()
    {
        const string json = @"{ ""id"": ""avg"", ""condition"": {
            ""expression"": { ""op"": ""divide"", ""operands"": [ { ""field"": ""Order.Total"" }, { ""field"": ""Order.ItemCount"" } ] },
            ""operator"": ""GreaterThan"", ""value"": 25 }, ""actions"": [] }";

        RuleEvaluationResult result = Engine.Evaluate(
            Engine.LoadRuleSet(json), DefaultFact(), new EvaluationOptions { EnableTrace = true });

        string description = result.Trace!.Rules.Single().Condition!.Description;
        Assert.Equal("divide(Order.Total, Order.ItemCount) GreaterThan 25", description);
    }
}
