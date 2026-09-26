using RuleWright.Core;
using RuleWright.Json.SystemText;
using Xunit;

namespace RuleWright.Execution.Tests;

/// <summary>
/// Registered value functions invoked from expressions via <c>{ "call": ..., "operands": [...] }</c>,
/// exercised across both execution paths. A call binds to its <see cref="IRuleValueFunction"/>
/// instance at compile time, so an unregistered name — or a wrong operand count when the
/// function declares one — must fail at <c>LoadRuleSet</c>, never mid-evaluation.
/// </summary>
public class ValueFunctionTests
{
    private static readonly RuleWrightEngine Engine = new RuleWrightBuilder()
        .UseJsonReader(new SystemTextJsonReader())
        .RegisterValueFunction("Upper", args => args[0] is string s ? s.ToUpperInvariant() : null)
        .RegisterValueFunction("Max", args =>
        {
            decimal? max = null;
            foreach (object? arg in args)
            {
                if (arg is null)
                {
                    continue;
                }

                try
                {
                    decimal value = System.Convert.ToDecimal(arg, System.Globalization.CultureInfo.InvariantCulture);
                    max = max is null || value > max ? value : max;
                }
                catch (System.Exception e) when (e is System.FormatException or System.InvalidCastException or System.OverflowException)
                {
                }
            }

            return max;
        })
        .RegisterValueFunction(new AnswerFunction())
        .RegisterValueFunction(new RoundToFunction())
        .Build();

    /// <summary>A no-argument value function.</summary>
    private sealed class AnswerFunction : IRuleValueFunction
    {
        public string Name => "Answer";

        public object? Invoke(object?[] arguments) => 42L;
    }

    /// <summary>Declares a required operand count, enforced at load.</summary>
    private sealed class RoundToFunction : IRuleValueFunction, IRuleValueFunctionMetadata
    {
        public string Name => "RoundTo";

        public string? Description => "Rounds a number to a digit count.";

        public int? RequiredOperandCount => 2;

        public object? Invoke(object?[] arguments)
            => arguments[0] is decimal value && arguments[1] is long digits
                ? decimal.Round(value, (int)digits)
                : null;
    }

    private static Dictionary<string, object?> DictFact() => new()
    {
        ["Customer"] = new Dictionary<string, object?> { ["Age"] = 21L, ["Name"] = "Alice" },
        ["Order"] = new Dictionary<string, object?> { ["Total"] = 120.5m },
    };

    private static LoadedRuleSet Load(string valueJson)
        => Engine.LoadRuleSet(
            "{\"id\":\"r\",\"condition\":{\"field\":\"Customer.Age\",\"operator\":\"IsNotNull\"},"
            + "\"actions\":[{\"type\":\"setOutput\",\"target\":\"D\",\"value\":" + valueJson + "}]}");

    private static void AssertBothPaths(string valueJson, object? expected)
    {
        LoadedRuleSet loaded = Load(valueJson);

        RuleEvaluationResult compiled = Engine.Evaluate(loaded, TestEngine.DefaultFact());
        Assert.Equal(CompilationMode.Compiled, compiled.CompilationMode);
        Assert.Equal(expected, compiled.Outputs["D"]);

        RuleEvaluationResult interpreted = Engine.Evaluate(loaded, DictFact());
        Assert.Equal(CompilationMode.Interpreted, interpreted.CompilationMode);
        Assert.Equal(expected, interpreted.Outputs["D"]);
    }

    [Fact]
    public void Call_WithFieldOperand()
        => AssertBothPaths("{\"call\":\"Upper\",\"operands\":[{\"field\":\"Customer.Name\"}]}", "ALICE");

    [Fact]
    public void Call_WithNoOperands()
        => AssertBothPaths("{\"call\":\"Answer\"}", 42L);

    [Fact]
    public void Call_VariadicOverMixedOperands()
        => AssertBothPaths(
            "{\"call\":\"Max\",\"operands\":[{\"field\":\"Order.Total\"},{\"field\":\"Customer.Age\"},7]}",
            120.5m);

    [Fact]
    public void Call_NestsInsideOperatorExpressions_AndOtherCalls()
        => AssertBothPaths(
            "{\"call\":\"RoundTo\",\"operands\":[{\"op\":\"multiply\",\"operands\":[{\"field\":\"Order.Total\"},0.1]},0]}",
            12m);

    [Fact]
    public void Call_OnConditionLeftHandSide()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(
            "{\"id\":\"r\",\"condition\":{\"expression\":{\"call\":\"Upper\",\"operands\":[{\"field\":\"Customer.Name\"}]},"
            + "\"operator\":\"Equals\",\"value\":\"ALICE\"}}");

        Assert.Single(Engine.Evaluate(loaded, TestEngine.DefaultFact()).FiredRules);
        Assert.Single(Engine.Evaluate(loaded, DictFact()).FiredRules);
    }

    [Fact]
    public void UnregisteredCall_FailsAtLoad_NamingTheFunction()
    {
        RuleCompilationException ex = Assert.Throws<RuleCompilationException>(
            () => Load("{\"call\":\"Nope\",\"operands\":[1]}"));
        Assert.Contains("Nope", ex.Message);
        Assert.Contains("RegisterValueFunction", ex.Message);
    }

    [Fact]
    public void DeclaredArity_IsEnforcedAtLoad()
    {
        RuleCompilationException ex = Assert.Throws<RuleCompilationException>(
            () => Load("{\"call\":\"RoundTo\",\"operands\":[1]}"));
        Assert.Contains("RoundTo", ex.Message);
        Assert.Contains("2", ex.Message);
    }

    [Fact]
    public void UnregisteredCall_InHandBuiltRuleSet_FailsAtLoad()
    {
        var rule = new Rule(
            "r",
            new ConditionLeaf("Customer.Age", ConditionOperator.GreaterThanOrEqual, 0L),
            new[] { new RuleAction(RuleAction.SetOutputType, "D", new CallExpression("Nope")) });

        Assert.Throws<RuleCompilationException>(() => Engine.LoadRuleSet(new RuleSet(new[] { rule })));
    }

    [Fact]
    public void Catalog_ListsRegisteredValueFunctions_WithMetadata()
    {
        Assert.Equal(new[] { "Answer", "Max", "RoundTo", "Upper" }, Engine.RegisteredValueFunctions);

        RuleValueFunctionDescriptor roundTo = Engine.ValueFunctionCatalog.Single(d => d.Name == "RoundTo");
        Assert.Equal("Rounds a number to a digit count.", roundTo.Description);
        Assert.Equal(2, roundTo.RequiredOperandCount);

        RuleValueFunctionDescriptor upper = Engine.ValueFunctionCatalog.Single(d => d.Name == "Upper");
        Assert.Null(upper.Description);
        Assert.Null(upper.RequiredOperandCount);
    }

    [Fact]
    public void DuplicateRegistration_Throws()
    {
        var builder = new RuleWrightBuilder().RegisterValueFunction("X", _ => 1);
        Assert.Throws<ArgumentException>(() => builder.RegisterValueFunction("X", _ => 2));
    }

    [Fact]
    public void ConditionAndValueFunctionNamespaces_AreIndependent()
    {
        // The same name can serve as a custom condition and a value function; the operator
        // ("custom" vs "call") decides which registry answers.
        RuleWrightEngine engine = new RuleWrightBuilder()
            .UseJsonReader(new SystemTextJsonReader())
            .RegisterFunction("Twin", (field, value) => field is int i && i > 0 || field is long l && l > 0)
            .RegisterValueFunction("Twin", args => "value")
            .Build();

        LoadedRuleSet loaded = engine.LoadRuleSet(
            "{\"id\":\"r\",\"condition\":{\"field\":\"Customer.Age\",\"operator\":\"custom\",\"name\":\"Twin\"},"
            + "\"actions\":[{\"type\":\"setOutput\",\"target\":\"D\",\"value\":{\"call\":\"Twin\"}}]}");

        Assert.Equal("value", engine.Evaluate(loaded, TestEngine.DefaultFact()).Outputs["D"]);
    }
}
