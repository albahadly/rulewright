using System.Linq;
using RuleWright.Core;
using RuleWright.Json.SystemText;
using RuleWright.Serialization;
using Xunit;

namespace RuleWright.Serialization.Tests;

public class ComputedActionSerializationTests
{
    private static Rule ParseRule(string json)
        => RuleSetParser.Parse(new SystemTextJsonReader().Read(json)).Rules[0];

    private static RuleSetValidationResult Validate(string json)
        => RuleSetValidator.Validate(new SystemTextJsonReader().Read(json));

    private static string RuleWithActions(string actionsJson)
        => "{\"id\":\"r\",\"condition\":{\"field\":\"A\",\"operator\":\"IsNotNull\"},\"actions\":" + actionsJson + "}";

    // --- Parsing ---

    [Fact]
    public void ParsesOperatorExpressionWithFieldAndScalarLiteral()
    {
        Rule rule = ParseRule(RuleWithActions(
            "[{\"type\":\"setOutput\",\"target\":\"D\",\"value\":{\"op\":\"multiply\",\"operands\":[{\"field\":\"Order.Total\"},0.1]}}]"));

        var op = Assert.IsType<OperatorExpression>(rule.Actions[0].Value);
        Assert.Equal(ExpressionOperator.Multiply, op.Operator);
        Assert.Equal("Order.Total", Assert.IsType<FieldExpression>(op.Operands[0]).Path);
        Assert.Equal(0.1m, Assert.IsType<LiteralExpression>(op.Operands[1]).Value);
    }

    [Fact]
    public void ParsesConstantScalarAsLiteral()
    {
        Rule rule = ParseRule(RuleWithActions("[{\"type\":\"setOutput\",\"target\":\"D\",\"value\":7}]"));

        Assert.Equal(7L, Assert.IsType<LiteralExpression>(rule.Actions[0].Value).Value);
    }

    [Fact]
    public void BareScalarAndExplicitLiteral_ParseAndHashEquivalently()
    {
        Rule bare = ParseRule(RuleWithActions("[{\"type\":\"setOutput\",\"target\":\"D\",\"value\":42}]"));
        Rule wrapped = ParseRule(RuleWithActions(
            "[{\"type\":\"setOutput\",\"target\":\"D\",\"value\":{\"literal\":42}}]"));

        Assert.Equal(42L, Assert.IsType<LiteralExpression>(bare.Actions[0].Value).Value);
        Assert.Equal(42L, Assert.IsType<LiteralExpression>(wrapped.Actions[0].Value).Value);
        Assert.Equal(RuleHasher.ComputeHash(bare), RuleHasher.ComputeHash(wrapped));
    }

    // --- Validation ---

    [Fact]
    public void ValidComputedAction_Passes()
    {
        Assert.True(Validate(RuleWithActions(
            "[{\"type\":\"setOutput\",\"target\":\"D\",\"value\":{\"op\":\"add\",\"operands\":[{\"field\":\"A\"},1,2]}}]")).IsValid);
    }

    [Fact]
    public void AccumulatorActionTypes_AreValid()
    {
        Assert.True(Validate(RuleWithActions("[{\"type\":\"addToOutput\",\"target\":\"S\",\"value\":1}]")).IsValid);
        Assert.True(Validate(RuleWithActions("[{\"type\":\"appendToOutput\",\"target\":\"R\",\"value\":\"x\"}]")).IsValid);
        Assert.True(Validate(RuleWithActions(
            "[{\"type\":\"addToOutput\",\"target\":\"S\",\"value\":{\"op\":\"multiply\",\"operands\":[{\"field\":\"A\"},2]}}]")).IsValid);
    }

    [Fact]
    public void UnknownActionType_ErrorsAtTypePointer()
    {
        RuleSetValidationResult result = Validate(RuleWithActions(
            "[{\"type\":\"multiplyOutput\",\"target\":\"S\",\"value\":1}]"));
        Assert.False(result.IsValid);
        Assert.Equal("/actions/0/type", result.Errors.Single().Path);
    }

    [Fact]
    public void MissingValue_IsRejected()
    {
        RuleSetValidationResult result = Validate(RuleWithActions("[{\"type\":\"setOutput\",\"target\":\"D\"}]"));
        Assert.False(result.IsValid);
        Assert.Equal("/actions/0", result.Errors.Single().Path);
    }

    [Fact]
    public void UnknownOperator_ErrorsAtOpPointer()
    {
        RuleSetValidationResult result = Validate(RuleWithActions(
            "[{\"type\":\"setOutput\",\"target\":\"D\",\"value\":{\"op\":\"power\",\"operands\":[1,2]}}]"));
        Assert.False(result.IsValid);
        Assert.Equal("/actions/0/value/op", result.Errors.Single().Path);
    }

    [Fact]
    public void WrongArity_ErrorsAtOperandsPointer()
    {
        RuleSetValidationResult subtract = Validate(RuleWithActions(
            "[{\"type\":\"setOutput\",\"target\":\"D\",\"value\":{\"op\":\"subtract\",\"operands\":[1,2,3]}}]"));
        Assert.False(subtract.IsValid);
        Assert.Equal("/actions/0/value/operands", subtract.Errors.Single().Path);

        RuleSetValidationResult add = Validate(RuleWithActions(
            "[{\"type\":\"setOutput\",\"target\":\"D\",\"value\":{\"op\":\"add\",\"operands\":[1]}}]"));
        Assert.False(add.IsValid);
        Assert.Equal("/actions/0/value/operands", add.Errors.Single().Path);
    }

    [Fact]
    public void NestedOperandError_ReportsNestedPointer()
    {
        RuleSetValidationResult result = Validate(RuleWithActions(
            "[{\"type\":\"setOutput\",\"target\":\"D\",\"value\":{\"op\":\"add\",\"operands\":[1,{\"op\":\"negate\",\"operands\":[]}]}}]"));
        Assert.False(result.IsValid);
        Assert.Equal("/actions/0/value/operands/1/operands", result.Errors.Single().Path);
    }

    [Fact]
    public void EmptyFieldPath_IsRejected()
    {
        RuleSetValidationResult result = Validate(RuleWithActions(
            "[{\"type\":\"setOutput\",\"target\":\"D\",\"value\":{\"field\":\"\"}}]"));
        Assert.False(result.IsValid);
        Assert.Equal("/actions/0/value/field", result.Errors.Single().Path);
    }

    [Fact]
    public void ArrayValue_IsRejected()
    {
        RuleSetValidationResult result = Validate(RuleWithActions(
            "[{\"type\":\"setOutput\",\"target\":\"D\",\"value\":[1,2]}]"));
        Assert.False(result.IsValid);
        Assert.Equal("/actions/0/value", result.Errors.Single().Path);
    }

    // --- Hashing ---

    [Fact]
    public void ComputedAction_CanonicalForm_IsDeterministic()
    {
        Rule rule = ParseRule(RuleWithActions(
            "[{\"type\":\"setOutput\",\"target\":\"D\",\"value\":{\"op\":\"multiply\",\"operands\":[{\"field\":\"Order.Total\"},0.1]}}]"));

        Assert.Equal(
            "{\"actions\":[{\"target\":\"D\",\"type\":\"setOutput\","
            + "\"value\":{\"op\":\"multiply\",\"operands\":[{\"field\":\"Order.Total\"},0.1]}}],"
            + "\"condition\":{\"field\":\"A\",\"operator\":\"IsNotNull\"}}",
            RuleHasher.GetCanonicalForm(rule));
    }

    [Fact]
    public void ConstantAction_CanonicalForm_RendersBareScalar()
    {
        Rule rule = ParseRule(RuleWithActions("[{\"type\":\"setOutput\",\"target\":\"T\",\"value\":10}]"));
        Assert.Equal(
            "{\"actions\":[{\"target\":\"T\",\"type\":\"setOutput\",\"value\":10}],"
            + "\"condition\":{\"field\":\"A\",\"operator\":\"IsNotNull\"}}",
            RuleHasher.GetCanonicalForm(rule));
    }

    [Fact]
    public void Hash_ChangesWhenExpressionChanges()
    {
        Rule one = ParseRule(RuleWithActions(
            "[{\"type\":\"setOutput\",\"target\":\"D\",\"value\":{\"op\":\"multiply\",\"operands\":[{\"field\":\"Order.Total\"},0.1]}}]"));
        Rule two = ParseRule(RuleWithActions(
            "[{\"type\":\"setOutput\",\"target\":\"D\",\"value\":{\"op\":\"multiply\",\"operands\":[{\"field\":\"Order.Total\"},0.2]}}]"));
        Assert.NotEqual(RuleHasher.ComputeHash(one), RuleHasher.ComputeHash(two));
    }

    [Fact]
    public void Hash_ChangesWhenConstantBecomesFieldRef()
    {
        Rule constant = ParseRule(RuleWithActions("[{\"type\":\"setOutput\",\"target\":\"D\",\"value\":5}]"));
        Rule fieldRef = ParseRule(RuleWithActions(
            "[{\"type\":\"setOutput\",\"target\":\"D\",\"value\":{\"field\":\"A\"}}]"));
        Assert.NotEqual(RuleHasher.ComputeHash(constant), RuleHasher.ComputeHash(fieldRef));
    }
}
