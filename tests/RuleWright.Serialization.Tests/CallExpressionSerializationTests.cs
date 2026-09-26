using RuleWright.Core;
using RuleWright.Json.SystemText;
using RuleWright.Serialization;
using Xunit;

namespace RuleWright.Serialization.Tests;

/// <summary>
/// The <c>{ "call": ..., "operands": [...] }</c> expression node at the document level:
/// structural validation, parsing to <see cref="CallExpression"/>, and content hashing.
/// Whether the name is registered is the engine's load-time concern, not the document's.
/// </summary>
public class CallExpressionSerializationTests
{
    private static RuleSetValidationResult Validate(string json)
        => RuleSetValidator.Validate(new SystemTextJsonReader().Read(json));

    private static Rule ParseRule(string json)
        => RuleSetParser.Parse(new SystemTextJsonReader().Read(json)).Rules[0];

    private static string RuleWithValue(string valueJson)
        => @"{ ""id"": ""r"", ""condition"": { ""field"": ""A"", ""operator"": ""IsNotNull"" },
               ""actions"": [ { ""type"": ""setOutput"", ""target"": ""T"", ""value"": " + valueJson + " } ] }";

    [Fact]
    public void Call_Parses_WithAndWithoutOperands()
    {
        var withArgs = Assert.IsType<CallExpression>(
            ParseRule(RuleWithValue(@"{ ""call"": ""RoundTo"", ""operands"": [ { ""field"": ""Order.Total"" }, 2 ] }")).Actions[0].Value);
        Assert.Equal("RoundTo", withArgs.Name);
        Assert.Equal(2, withArgs.Operands.Count);

        var noArgs = Assert.IsType<CallExpression>(
            ParseRule(RuleWithValue(@"{ ""call"": ""Now"" }")).Actions[0].Value);
        Assert.Empty(noArgs.Operands);
    }

    [Fact]
    public void CallName_MustBeANonEmptyString()
    {
        Assert.Contains(
            Validate(RuleWithValue(@"{ ""call"": 5 }")).Errors,
            e => e.Path == "/actions/0/value/call");
        Assert.Contains(
            Validate(RuleWithValue(@"{ ""call"": """" }")).Errors,
            e => e.Path == "/actions/0/value/call");
    }

    [Fact]
    public void CallOperands_MustBeAnArray_AndAreValidatedRecursively()
    {
        Assert.Contains(
            Validate(RuleWithValue(@"{ ""call"": ""F"", ""operands"": 5 }")).Errors,
            e => e.Path == "/actions/0/value/operands");

        Assert.Contains(
            Validate(RuleWithValue(@"{ ""call"": ""F"", ""operands"": [ { ""op"": ""negate"", ""operands"": [1, 2] } ] }")).Errors,
            e => e.Path == "/actions/0/value/operands/0/operands");
    }

    [Fact]
    public void ExpressionDiscriminators_StayMutuallyExclusive()
    {
        RuleSetValidationResult result = Validate(RuleWithValue(@"{ ""call"": ""F"", ""field"": ""A"" }"));
        Assert.Contains(result.Errors, e => e.Message.Contains("exactly one of 'op', 'field', 'literal', 'call', or 'param'"));
    }

    [Fact]
    public void Hash_DistinguishesCallsFromOperators_AndByNameAndOperands()
    {
        string opHash = RuleHasher.ComputeHash(ParseRule(RuleWithValue(@"{ ""op"": ""count"", ""operands"": [ { ""field"": ""A"" } ] }")));
        string callHash = RuleHasher.ComputeHash(ParseRule(RuleWithValue(@"{ ""call"": ""count"", ""operands"": [ { ""field"": ""A"" } ] }")));
        string otherName = RuleHasher.ComputeHash(ParseRule(RuleWithValue(@"{ ""call"": ""size"", ""operands"": [ { ""field"": ""A"" } ] }")));
        string otherArgs = RuleHasher.ComputeHash(ParseRule(RuleWithValue(@"{ ""call"": ""count"", ""operands"": [ { ""field"": ""B"" } ] }")));
        string same = RuleHasher.ComputeHash(ParseRule(RuleWithValue(@"{ ""call"": ""count"", ""operands"": [ { ""field"": ""A"" } ] }")));

        Assert.NotEqual(opHash, callHash);
        Assert.NotEqual(callHash, otherName);
        Assert.NotEqual(callHash, otherArgs);
        Assert.Equal(callHash, same);
    }

    [Fact]
    public void CustomActionTypes_ValidateOnlyThroughDocumentOptions()
    {
        string json = RuleWithValue("1").Replace("setOutput", "escalate");
        var options = new RuleDocumentOptions(new[] { "escalate" });

        Assert.False(RuleSetValidator.Validate(new SystemTextJsonReader().Read(json)).IsValid);
        Assert.True(RuleSetValidator.Validate(new SystemTextJsonReader().Read(json), options).IsValid);

        // The parse overload accepts the same options and produces the custom-typed action.
        RuleSet parsed = RuleSetParser.Parse(new SystemTextJsonReader().Read(json), options);
        Assert.Equal("escalate", parsed.Rules[0].Actions[0].Type);
    }

    [Fact]
    public void CustomActionValue_IsOptional_BuiltInValuesStayRequired()
    {
        var options = new RuleDocumentOptions(new[] { "escalate" });
        const string noValue = @"{ ""id"": ""r"", ""condition"": { ""field"": ""A"", ""operator"": ""IsNotNull"" },
               ""actions"": [ { ""type"": ""escalate"", ""target"": ""T"" } ] }";

        Assert.True(RuleSetValidator.Validate(new SystemTextJsonReader().Read(noValue), options).IsValid);

        var parsed = RuleSetParser.Parse(new SystemTextJsonReader().Read(noValue), options);
        var literal = Assert.IsType<LiteralExpression>(parsed.Rules[0].Actions[0].Value);
        Assert.Null(literal.Value);

        const string builtInNoValue = @"{ ""id"": ""r"", ""condition"": { ""field"": ""A"", ""operator"": ""IsNotNull"" },
               ""actions"": [ { ""type"": ""setOutput"", ""target"": ""T"" } ] }";
        Assert.False(RuleSetValidator.Validate(new SystemTextJsonReader().Read(builtInNoValue), options).IsValid);
    }
}
