using Rulewright.Core;
using Xunit;
using static Rulewright.Execution.Tests.TestEngine;

namespace Rulewright.Execution.Tests;

/// <summary>
/// Dictionary facts run the interpreter; its semantics must match the compiled path
/// and the result must report <see cref="CompilationMode.Interpreted"/>.
/// </summary>
public class InterpreterParityTests
{
    private static Dictionary<string, object?> DictionaryFact() => new Dictionary<string, object?>
    {
        ["Customer"] = new Dictionary<string, object?>
        {
            ["Age"] = 21L,
            ["IsVip"] = true,
            ["Name"] = "Alice",
            ["Email"] = "alice@example.com",
            ["Tier"] = "Gold",
        },
        ["Order"] = new Dictionary<string, object?>
        {
            ["Total"] = 120.5m,
            ["Coupon"] = "SPRING",
        },
    };

    [Fact]
    public void DictionaryFact_ReportsInterpretedMode()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(WrapRule("{\"field\":\"Customer.Age\",\"operator\":\"GreaterThan\",\"value\":18}"));
        RuleEvaluationResult result = Engine.Evaluate(loaded, DictionaryFact());
        Assert.Equal(CompilationMode.Interpreted, result.CompilationMode);
        Assert.Single(result.FiredRules);
    }

    [Theory]
    [InlineData("{\"field\":\"Customer.Age\",\"operator\":\"Equals\",\"value\":21}", true)]
    [InlineData("{\"field\":\"Customer.Age\",\"operator\":\"GreaterThan\",\"value\":21}", false)]
    [InlineData("{\"field\":\"Customer.Age\",\"operator\":\"GreaterThan\",\"value\":20.5}", true)]
    [InlineData("{\"field\":\"Order.Total\",\"operator\":\"GreaterThanOrEqual\",\"value\":100}", true)]
    [InlineData("{\"field\":\"Customer.Name\",\"operator\":\"StartsWith\",\"value\":\"Al\"}", true)]
    [InlineData("{\"field\":\"Customer.Name\",\"operator\":\"Contains\",\"value\":\"LIC\"}", false)]
    [InlineData("{\"field\":\"Customer.Email\",\"operator\":\"MatchesRegex\",\"value\":\"^[^@]+@example\\\\.com$\"}", true)]
    [InlineData("{\"field\":\"Customer.Tier\",\"operator\":\"In\",\"value\":[\"Gold\",\"Silver\"]}", true)]
    [InlineData("{\"field\":\"Customer.Age\",\"operator\":\"NotIn\",\"value\":[18,65]}", true)]
    [InlineData("{\"field\":\"Customer.IsVip\",\"operator\":\"Equals\",\"value\":true}", true)]
    public void OperatorParityWithCompiledPath(string condition, bool expected)
    {
        Assert.Equal(expected, Matches(condition, DictionaryFact()));
    }

    [Fact]
    public void MissingKeys_ApplyNullSemantics()
    {
        Dictionary<string, object?> fact = DictionaryFact();
        Assert.True(Matches("{\"field\":\"Customer.Nope\",\"operator\":\"IsNull\"}", fact));
        Assert.False(Matches("{\"field\":\"Customer.Nope\",\"operator\":\"Equals\",\"value\":1}", fact));
        Assert.True(Matches("{\"field\":\"Customer.Nope\",\"operator\":\"NotEquals\",\"value\":1}", fact));
        Assert.True(Matches("{\"field\":\"Missing.Deep.Path\",\"operator\":\"IsNull\"}", fact));
        Assert.False(Matches("{\"field\":\"Missing.Deep.Path\",\"operator\":\"GreaterThan\",\"value\":1}", fact));
    }

    [Fact]
    public void PocoInsideDictionary_ResolvesViaReflection()
    {
        var fact = new Dictionary<string, object?>
        {
            ["Customer"] = new Customer { Age = 30, Name = "Bob", Address = new Address { City = "Berlin" } },
        };

        Assert.True(Matches("{\"field\":\"Customer.Age\",\"operator\":\"Equals\",\"value\":30}", fact));
        Assert.True(Matches("{\"field\":\"Customer.Address.City\",\"operator\":\"Equals\",\"value\":\"Berlin\"}", fact));
    }

    [Fact]
    public void CustomFunctions_RunInInterpretedMode()
    {
        var fact = new Dictionary<string, object?> { ["Answer"] = 42L };
        Assert.True(Matches("{\"field\":\"Answer\",\"operator\":\"custom\",\"name\":\"FieldIsFortyTwo\"}", fact));
        Assert.True(Matches("{\"operator\":\"custom\",\"name\":\"AlwaysTrue\"}", fact));
    }

    [Fact]
    public void GroupCombinators_MatchCompiledSemantics()
    {
        const string specCondition = @"{
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
        }";

        Assert.True(Matches(specCondition, DictionaryFact()));

        Dictionary<string, object?> minor = DictionaryFact();
        ((Dictionary<string, object?>)minor["Customer"]!)["Age"] = 16L;
        Assert.False(Matches(specCondition, minor));
    }

    [Fact]
    public void NoImplicitStringNumberCoercion()
    {
        var fact = new Dictionary<string, object?> { ["Code"] = "18" };
        Assert.False(Matches("{\"field\":\"Code\",\"operator\":\"Equals\",\"value\":18}", fact));
        Assert.False(Matches("{\"field\":\"Code\",\"operator\":\"GreaterThan\",\"value\":1}", fact));
    }

    // --- Differential parity: the same rule through both paths must agree ---

    /// <summary>
    /// Runs one condition against a typed fact (compiled delegates) and an equivalent dictionary
    /// fact (interpreter) and asserts they agree. Hand-written expectations can only catch a
    /// divergence someone anticipated; this catches any divergence at all.
    /// </summary>
    private static void AssertPathsAgree<TFact>(ConditionNode condition, TFact typedFact, Dictionary<string, object?> dictionaryFact)
    {
        var ruleSet = new RuleSet(new[] { new Rule("parity", condition) });
        LoadedRuleSet loaded = Engine.LoadRuleSet(ruleSet);

        RuleEvaluationResult compiled = Engine.Evaluate(loaded, typedFact);
        RuleEvaluationResult interpreted = Engine.Evaluate(loaded, dictionaryFact);

        Assert.Equal(CompilationMode.Compiled, compiled.CompilationMode);
        Assert.Equal(CompilationMode.Interpreted, interpreted.CompilationMode);
        Assert.Equal(compiled.FiredRules.Count, interpreted.FiredRules.Count);
    }

    /// <summary>
    /// A null inside an In/NotIn set contributes nothing on either path. The JSON schema forbids
    /// it outright, but a hand-built <see cref="ConditionLeaf"/> can still carry one, and the two
    /// paths must not answer differently — a null field is not "in" any set, so In is false and
    /// NotIn is true, exactly as the documented null semantics say.
    /// </summary>
    [Fact]
    public void NullInsideAnInSet_ContributesNothingOnBothPaths()
    {
        var typed = new OrderFact { Customer = new Customer { Email = null } };
        var dictionary = new Dictionary<string, object?>
        {
            ["Customer"] = new Dictionary<string, object?> { ["Email"] = null },
        };

        var inSet = new ConditionLeaf("Customer.Email", ConditionOperator.In, new object?[] { null, "a@b.c" });
        var notInSet = new ConditionLeaf("Customer.Email", ConditionOperator.NotIn, new object?[] { null, "a@b.c" });

        AssertPathsAgree(inSet, typed, dictionary);
        AssertPathsAgree(notInSet, typed, dictionary);

        Assert.False(Fires(inSet, typed));
        Assert.False(Fires(inSet, dictionary));
        Assert.True(Fires(notInSet, typed));
        Assert.True(Fires(notInSet, dictionary));
    }

    /// <summary>A non-null value still matches around the null hole in the set, on both paths.</summary>
    [Fact]
    public void NullInsideAnInSet_DoesNotHideTheOtherMembers()
    {
        var typed = new OrderFact { Customer = new Customer { Email = "a@b.c" } };
        var dictionary = new Dictionary<string, object?>
        {
            ["Customer"] = new Dictionary<string, object?> { ["Email"] = "a@b.c" },
        };

        var inSet = new ConditionLeaf("Customer.Email", ConditionOperator.In, new object?[] { null, "a@b.c" });
        AssertPathsAgree(inSet, typed, dictionary);
        Assert.True(Fires(inSet, typed));
        Assert.True(Fires(inSet, dictionary));
    }

    private static bool Fires<TFact>(ConditionNode condition, TFact fact)
    {
        var ruleSet = new RuleSet(new[] { new Rule("parity", condition) });
        return Engine.Evaluate(Engine.LoadRuleSet(ruleSet), fact).FiredRules.Count == 1;
    }
}
