using RuleWright.Core;
using Xunit;
using static RuleWright.Execution.Tests.TestEngine;

namespace RuleWright.Execution.Tests;

/// <summary>
/// Computed actions (<c>setOutput</c> with an <c>expression</c>) exercised across both
/// execution paths. The compiled path runs against the typed <see cref="OrderFact"/>; the
/// interpreted path runs against an equivalent dictionary fact. Arithmetic funnels through
/// the shared <see cref="ValueExpressionOps"/>, so both paths must agree by construction.
/// </summary>
public class ComputedActionTests
{
    private const string AlwaysCondition = "\"condition\":{\"field\":\"Customer.Age\",\"operator\":\"IsNotNull\"}";

    private static Dictionary<string, object?> DictFact() => new()
    {
        ["Customer"] = new Dictionary<string, object?>
        {
            ["Age"] = 21L,
            ["Name"] = "Alice",
            ["Email"] = "alice@example.com",
            ["LoyaltyYears"] = 3L,
        },
        ["Order"] = new Dictionary<string, object?>
        {
            ["Total"] = 120.5m,
            ["ItemCount"] = 3L,
            ["Weight"] = 2.4,
        },
    };

    private static LoadedRuleSet Load(string actionsJson)
        => Engine.LoadRuleSet("{\"id\":\"r\"," + AlwaysCondition + ",\"actions\":" + actionsJson + "}");

    private static string ActionFor(string expressionJson)
        => "[{\"type\":\"setOutput\",\"target\":\"D\",\"value\":" + expressionJson + "}]";

    private static object? CompiledOutput(string actionsJson)
        => Engine.Evaluate(Load(actionsJson), DefaultFact()).Outputs["D"];

    private static object? InterpretedOutput(string actionsJson)
        => Engine.Evaluate(Load(actionsJson), DictFact()).Outputs["D"];

    private static void AssertBothPaths(string expressionJson, object? expected)
    {
        string actions = ActionFor(expressionJson);
        Assert.Equal(expected, CompiledOutput(actions));
        Assert.Equal(expected, InterpretedOutput(actions));
    }

    [Fact]
    public void Multiply_FieldByLiteral()
        => AssertBothPaths("{\"op\":\"multiply\",\"operands\":[{\"field\":\"Order.Total\"},0.1]}", 12.05m);

    [Fact]
    public void Add_TwoFields()
        => AssertBothPaths("{\"op\":\"add\",\"operands\":[{\"field\":\"Order.Total\"},{\"field\":\"Customer.Age\"}]}", 141.5m);

    [Fact]
    public void Subtract_FieldMinusLiteral()
        => AssertBothPaths("{\"op\":\"subtract\",\"operands\":[{\"field\":\"Order.Total\"},20.5]}", 100m);

    [Fact]
    public void Divide_IsNeverIntegerTruncated()
        => AssertBothPaths("{\"op\":\"divide\",\"operands\":[7,2]}", 3.5m);

    [Fact]
    public void Modulo_Remainder()
        => AssertBothPaths("{\"op\":\"modulo\",\"operands\":[7,3]}", 1m);

    [Fact]
    public void Negate_Field()
        => AssertBothPaths("{\"op\":\"negate\",\"operands\":[{\"field\":\"Customer.Age\"}]}", -21m);

    [Fact]
    public void Nested_ArithmeticEvaluatesInnerFirst()
        => AssertBothPaths(
            "{\"op\":\"multiply\",\"operands\":[{\"op\":\"subtract\",\"operands\":[{\"field\":\"Order.Total\"},100]},2]}",
            41m);

    [Fact]
    public void Concat_StringsAndFields()
        => AssertBothPaths("{\"op\":\"concat\",\"operands\":[\"Hi \",{\"field\":\"Customer.Name\"},\"!\"]}", "Hi Alice!");

    [Fact]
    public void Concat_RendersNumbersInvariantly()
        => AssertBothPaths("{\"op\":\"concat\",\"operands\":[\"Age: \",{\"field\":\"Customer.Age\"}]}", "Age: 21");

    [Fact]
    public void Coalesce_ReturnsFirstNonNull()
        => AssertBothPaths("{\"op\":\"coalesce\",\"operands\":[{\"literal\":null},\"fallback\"]}", "fallback");

    [Fact]
    public void Coalesce_PrefersPresentField()
        => AssertBothPaths("{\"op\":\"coalesce\",\"operands\":[{\"field\":\"Customer.Name\"},\"fallback\"]}", "Alice");

    [Fact]
    public void DivideByZero_YieldsNull()
        => AssertBothPaths("{\"op\":\"divide\",\"operands\":[{\"field\":\"Order.Total\"},0]}", null);

    [Fact]
    public void ModuloByZero_YieldsNull()
        => AssertBothPaths("{\"op\":\"modulo\",\"operands\":[{\"field\":\"Order.Total\"},0]}", null);

    [Fact]
    public void NullField_PropagatesToNullOutput()
    {
        LoadedRuleSet loaded = Load(ActionFor("{\"op\":\"multiply\",\"operands\":[{\"field\":\"Order.Total\"},2]}"));

        OrderFact typed = DefaultFact();
        typed.Order = null;
        Assert.Null(Engine.Evaluate(loaded, typed).Outputs["D"]);

        Dictionary<string, object?> dict = DictFact();
        dict["Order"] = null;
        Assert.Null(Engine.Evaluate(loaded, dict).Outputs["D"]);
    }

    [Fact]
    public void FieldCopy_CompiledPreservesClrType()
    {
        Assert.Equal(120.50m, CompiledOutput(ActionFor("{\"field\":\"Order.Total\"}")));
        Assert.Equal("Alice", CompiledOutput(ActionFor("{\"field\":\"Customer.Name\"}")));
    }

    [Fact]
    public void MixedActions_LastWritePerTargetWins()
    {
        string actions =
            "[{\"type\":\"setOutput\",\"target\":\"C\",\"value\":1},"
            + "{\"type\":\"setOutput\",\"target\":\"D\",\"value\":{\"op\":\"multiply\",\"operands\":[{\"field\":\"Order.Total\"},2]}},"
            + "{\"type\":\"setOutput\",\"target\":\"C\",\"value\":{\"field\":\"Customer.Age\"}}]";

        RuleEvaluationResult result = Engine.Evaluate(Load(actions), DefaultFact());
        Assert.Equal(241m, result.Outputs["D"]);
        Assert.Equal(21, result.Outputs["C"]);
    }

    [Fact]
    public void FiredRuleOutputs_IncludeComputedValues()
    {
        RuleEvaluationResult result = Engine.Evaluate(Load(ActionFor("{\"op\":\"add\",\"operands\":[1,2]}")), DefaultFact());
        Assert.Equal(3m, result.FiredRules[0].Outputs["D"]);
    }

    [Fact]
    public void ConstantAction_StillProducesConstant()
    {
        string actions = "[{\"type\":\"setOutput\",\"target\":\"D\",\"value\":7}]";
        Assert.Equal(7L, CompiledOutput(actions));
        Assert.Equal(7L, InterpretedOutput(actions));
    }

    [Fact]
    public void MissingMemberInExpression_ThrowsAtCompileTimeForTypedFacts()
    {
        LoadedRuleSet loaded = Load(ActionFor("{\"field\":\"Customer.Nonexistent\"}"));
        Assert.Throws<RuleCompilationException>(() => Engine.Evaluate(loaded, DefaultFact()));

        // Dictionary facts have no compile-time shape: a missing key resolves to null.
        Assert.Null(Engine.Evaluate(loaded, DictFact()).Outputs["D"]);
    }
}
