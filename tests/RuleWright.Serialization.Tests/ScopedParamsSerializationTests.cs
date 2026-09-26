using RuleWright.Core;
using RuleWright.Json.SystemText;
using RuleWright.Serialization;
using Xunit;

namespace RuleWright.Serialization.Tests;

/// <summary>
/// Scoped params at the document level: structural validation (scope, shadowing, cycles) and
/// the parser's substitution, which inlines every <c>{ "param": ... }</c> reference so the
/// domain model, the content hash, and the engine never see params at all.
/// </summary>
public class ScopedParamsSerializationTests
{
    private static RuleSetValidationResult Validate(string json)
        => RuleSetValidator.Validate(new SystemTextJsonReader().Read(json));

    private static RuleSet Parse(string json)
        => RuleSetParser.Parse(new SystemTextJsonReader().Read(json));

    [Fact]
    public void UnknownParam_IsRejected_AtItsPointer()
    {
        RuleSetValidationResult result = Validate(@"{
          ""id"": ""r"",
          ""condition"": { ""field"": ""A"", ""operator"": ""IsNotNull"" },
          ""actions"": [ { ""type"": ""setOutput"", ""target"": ""T"", ""value"": { ""param"": ""ghost"" } } ]
        }");

        RuleValidationError error = Assert.Single(result.Errors);
        Assert.Equal("/actions/0/value/param", error.Path);
        Assert.Contains("ghost", error.Message);
    }

    [Fact]
    public void LocalParams_AreInvisibleToOtherRules()
    {
        RuleSetValidationResult result = Validate(@"{
          ""rules"": [
            { ""id"": ""a"", ""params"": { ""mine"": 1 },
              ""condition"": { ""field"": ""A"", ""operator"": ""IsNotNull"" } },
            { ""id"": ""b"",
              ""condition"": { ""field"": ""A"", ""operator"": ""IsNotNull"" },
              ""actions"": [ { ""type"": ""setOutput"", ""target"": ""T"", ""value"": { ""param"": ""mine"" } } ] }
          ]
        }");

        Assert.False(result.IsValid);
        Assert.Equal("/rules/1/actions/0/value/param", Assert.Single(result.Errors).Path);
    }

    [Fact]
    public void CircularParams_AreRejected()
    {
        RuleSetValidationResult result = Validate(@"{
          ""params"": {
            ""a"": { ""op"": ""add"", ""operands"": [ { ""param"": ""b"" }, 1 ] },
            ""b"": { ""op"": ""add"", ""operands"": [ { ""param"": ""a"" }, 1 ] }
          },
          ""rules"": [ { ""id"": ""r"", ""condition"": { ""field"": ""A"", ""operator"": ""IsNotNull"" } } ]
        }");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Path == "/params/a" && e.Message.Contains("circular"));
    }

    [Fact]
    public void SelfReferencingParam_IsACycle()
    {
        RuleSetValidationResult result = Validate(@"{
          ""params"": { ""loop"": { ""op"": ""add"", ""operands"": [ { ""param"": ""loop"" }, 1 ] } },
          ""rules"": [ { ""id"": ""r"", ""condition"": { ""field"": ""A"", ""operator"": ""IsNotNull"" } } ]
        }");

        Assert.False(result.IsValid);
        Assert.Equal("/params/loop", Assert.Single(result.Errors).Path);
    }

    [Fact]
    public void ParamReference_InsideQuantifierCondition_IsRejected()
    {
        RuleSetValidationResult result = Validate(@"{
          ""id"": ""r"",
          ""params"": { ""limit"": 3 },
          ""condition"": {
            ""field"": ""Order.Lines"", ""operator"": ""Any"",
            ""condition"": { ""expression"": { ""param"": ""limit"" }, ""operator"": ""GreaterThan"", ""value"": 1 }
          }
        }");

        Assert.False(result.IsValid);
        Assert.Contains("quantifier", Assert.Single(result.Errors).Message);
    }

    [Fact]
    public void ParamsMustBeAnObject_AndNamesNonEmpty()
    {
        Assert.Contains(
            Validate(@"{ ""id"": ""r"", ""params"": [], ""condition"": { ""field"": ""A"", ""operator"": ""IsNotNull"" } }").Errors,
            e => e.Path == "/params");

        Assert.Contains(
            Validate(@"{ ""id"": ""r"", ""params"": { """": 1 }, ""condition"": { ""field"": ""A"", ""operator"": ""IsNotNull"" } }").Errors,
            e => e.Path == "/params" && e.Message.Contains("empty"));
    }

    [Fact]
    public void Substitution_InlinesTheNamedExpression_SharedAcrossReferences()
    {
        RuleSet parsed = Parse(@"{
          ""params"": { ""tenth"": { ""op"": ""multiply"", ""operands"": [ { ""field"": ""Order.Total"" }, 0.1 ] } },
          ""rules"": [
            { ""id"": ""r"",
              ""condition"": { ""expression"": { ""param"": ""tenth"" }, ""operator"": ""GreaterThan"", ""value"": 5 },
              ""actions"": [ { ""type"": ""setOutput"", ""target"": ""T"", ""value"": { ""param"": ""tenth"" } } ] }
          ]
        }");

        Rule rule = parsed.Rules[0];
        var conditionSide = Assert.IsType<OperatorExpression>(((ConditionLeaf)rule.Condition).Left);
        var actionSide = Assert.IsType<OperatorExpression>(rule.Actions[0].Value);

        Assert.Equal(ExpressionOperator.Multiply, conditionSide.Operator);

        // One immutable instance serves every reference — substitution, not copying.
        Assert.Same(conditionSide, actionSide);
    }

    [Fact]
    public void ParamAuthoredDocument_HashesIdenticallyToItsInlinedForm()
    {
        Rule withParams = Parse(@"{
          ""params"": { ""tenth"": { ""op"": ""multiply"", ""operands"": [ { ""field"": ""Order.Total"" }, 0.1 ] } },
          ""rules"": [
            { ""id"": ""r"",
              ""condition"": { ""field"": ""A"", ""operator"": ""IsNotNull"" },
              ""actions"": [ { ""type"": ""setOutput"", ""target"": ""T"", ""value"": { ""param"": ""tenth"" } } ] }
          ]
        }").Rules[0];

        Rule inlined = Parse(@"{
          ""id"": ""r"",
          ""condition"": { ""field"": ""A"", ""operator"": ""IsNotNull"" },
          ""actions"": [ { ""type"": ""setOutput"", ""target"": ""T"",
                           ""value"": { ""op"": ""multiply"", ""operands"": [ { ""field"": ""Order.Total"" }, 0.1 ] } } ]
        }").Rules[0];

        Assert.Equal(RuleHasher.ComputeHash(inlined), RuleHasher.ComputeHash(withParams));
    }

    [Fact]
    public void FailureMessage_MustBeANonEmptyString_AndStaysOutOfTheHash()
    {
        Assert.Contains(
            Validate(@"{ ""id"": ""r"", ""failureMessage"": 5, ""condition"": { ""field"": ""A"", ""operator"": ""IsNotNull"" } }").Errors,
            e => e.Path == "/failureMessage");

        Rule bare = Parse(@"{ ""id"": ""r"", ""condition"": { ""field"": ""A"", ""operator"": ""IsNotNull"" } }").Rules[0];
        Rule messaged = Parse(@"{ ""id"": ""r"", ""failureMessage"": ""why not"", ""condition"": { ""field"": ""A"", ""operator"": ""IsNotNull"" } }").Rules[0];

        Assert.Equal("why not", messaged.FailureMessage);
        Assert.Equal(RuleHasher.ComputeHash(bare), RuleHasher.ComputeHash(messaged));
    }
}
