using System.Linq;
using RuleWright.Core;
using RuleWright.Json.SystemText;
using RuleWright.Serialization;
using Xunit;

namespace RuleWright.Serialization.Tests;

public class ComputedConditionSerializationTests
{
    private const string ComputedLeaf =
        "{\"expression\":{\"op\":\"multiply\",\"operands\":[{\"field\":\"Order.Total\"},0.9]},\"operator\":\"GreaterThan\",\"value\":100}";

    private static Rule Parse(string conditionJson)
        => RuleSetParser.Parse(new SystemTextJsonReader().Read("{\"id\":\"r\",\"condition\":" + conditionJson + "}")).Rules[0];

    private static RuleSetValidationResult Validate(string conditionJson)
        => RuleSetValidator.Validate(new SystemTextJsonReader().Read("{\"id\":\"r\",\"condition\":" + conditionJson + "}"));

    [Fact]
    public void ParsesComputedLeftHandSide()
    {
        var leaf = Assert.IsType<ConditionLeaf>(Parse(ComputedLeaf).Condition);
        Assert.Null(leaf.Field);
        var op = Assert.IsType<OperatorExpression>(leaf.Left);
        Assert.Equal(ExpressionOperator.Multiply, op.Operator);
        Assert.Equal(ConditionOperator.GreaterThan, leaf.Operator);
        Assert.Equal(100L, leaf.Value);
    }

    [Fact]
    public void ValidComputedCondition_Passes() => Assert.True(Validate(ComputedLeaf).IsValid);

    [Fact]
    public void FieldAndExpression_IsRejected()
    {
        RuleSetValidationResult result = Validate(
            "{\"field\":\"A\",\"expression\":{\"field\":\"B\"},\"operator\":\"Equals\",\"value\":1}");
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Path == "/condition");
    }

    [Fact]
    public void CustomWithExpression_IsRejected()
    {
        RuleSetValidationResult result = Validate(
            "{\"expression\":{\"field\":\"A\"},\"operator\":\"custom\",\"name\":\"F\"}");
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Path == "/condition/expression");
    }

    [Fact]
    public void BadLeftExpression_ReportsNestedPointer()
    {
        RuleSetValidationResult result = Validate(
            "{\"expression\":{\"op\":\"divide\",\"operands\":[1]},\"operator\":\"GreaterThan\",\"value\":1}");
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Path == "/condition/expression/operands");
    }

    [Fact]
    public void ComputedLeaf_CanonicalForm_IsDeterministic()
    {
        Assert.Equal(
            "{\"actions\":[],\"condition\":{\"expression\":{\"op\":\"multiply\",\"operands\":[{\"field\":\"Order.Total\"},0.9]},"
            + "\"operator\":\"GreaterThan\",\"value\":100}}",
            RuleHasher.GetCanonicalForm(Parse(ComputedLeaf)));
    }

    [Fact]
    public void Hash_ChangesWhenLeftExpressionChanges()
    {
        Rule one = Parse(ComputedLeaf);
        Rule two = Parse("{\"expression\":{\"op\":\"multiply\",\"operands\":[{\"field\":\"Order.Total\"},0.8]},\"operator\":\"GreaterThan\",\"value\":100}");
        Assert.NotEqual(RuleHasher.ComputeHash(one), RuleHasher.ComputeHash(two));
    }
}
