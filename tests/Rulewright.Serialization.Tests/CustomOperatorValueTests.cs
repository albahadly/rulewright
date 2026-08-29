using Rulewright.Core;
using Rulewright.Json.SystemText;
using Rulewright.Serialization;
using Xunit;

namespace Rulewright.Serialization.Tests;

/// <summary>
/// A <c>custom</c> leaf's <c>value</c> is whatever the registered function expects
/// (<see cref="RuleFunctionValueKind"/> spans None/Scalar/Text/Array), so the parser must
/// carry an array value through to the function the same way In/NotIn does — and whatever
/// the validator accepts, the parser must be able to parse.
/// </summary>
public class CustomOperatorValueTests
{
    private static RuleJsonValue Read(string json) => new SystemTextJsonReader().Read(json);

    private static Rule ParseRule(string json) => RuleSetParser.Parse(Read(json)).Rules[0];

    private const string ArrayValueRule =
        "{\"id\":\"r\",\"condition\":{\"operator\":\"custom\",\"name\":\"IsBetweenInclusive\","
        + "\"field\":\"Age\",\"value\":[18,65]}}";

    [Fact]
    public void ArrayValue_IsValid()
    {
        Assert.True(RuleSetValidator.Validate(Read(ArrayValueRule)).IsValid);
    }

    [Fact]
    public void ArrayValue_ParsesToObjectArray()
    {
        var leaf = (ConditionLeaf)ParseRule(ArrayValueRule).Condition;
        object?[] bounds = Assert.IsType<object?[]>(leaf.Value);
        Assert.Equal(new object?[] { 18L, 65L }, bounds);
    }

    [Fact]
    public void NestedArrayValue_ParsesRecursively()
    {
        var leaf = (ConditionLeaf)ParseRule(
            "{\"id\":\"r\",\"condition\":{\"operator\":\"custom\",\"name\":\"F\",\"field\":\"A\",\"value\":[[1,2],[3]]}}").Condition;
        object?[] outer = Assert.IsType<object?[]>(leaf.Value);
        Assert.Equal(new object?[] { 1L, 2L }, Assert.IsType<object?[]>(outer[0]));
    }

    [Fact]
    public void ObjectValue_IsRejectedWithAPointer()
    {
        RuleSetValidationResult result = RuleSetValidator.Validate(Read(
            "{\"id\":\"r\",\"condition\":{\"operator\":\"custom\",\"name\":\"F\",\"field\":\"A\",\"value\":{\"min\":1}}}"));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Path == "/condition/value");
    }

    [Fact]
    public void ObjectInsideArrayValue_IsRejectedWithAPointer()
    {
        RuleSetValidationResult result = RuleSetValidator.Validate(Read(
            "{\"id\":\"r\",\"condition\":{\"operator\":\"custom\",\"name\":\"F\",\"field\":\"A\",\"value\":[1,{\"min\":1}]}}"));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Path == "/condition/value/1");
    }
}
