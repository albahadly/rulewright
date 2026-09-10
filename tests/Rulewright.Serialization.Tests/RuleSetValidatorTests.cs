using System.Linq;
using Rulewright.Json.SystemText;
using Rulewright.Serialization;
using Xunit;

namespace Rulewright.Serialization.Tests;

public class RuleSetValidatorTests
{
    private static RuleSetValidationResult Validate(string json)
        => RuleSetValidator.Validate(new SystemTextJsonReader().Read(json));

    private const string SpecExample = @"{
      ""id"": ""discount-rule-01"",
      ""description"": ""VIP or high-value customers get 10% off"",
      ""priority"": 10,
      ""enabled"": true,
      ""condition"": {
        ""type"": ""group"",
        ""operator"": ""AND"",
        ""rules"": [
          { ""field"": ""Customer.Age"", ""operator"": ""GreaterThan"", ""value"": 18 },
          {
            ""type"": ""group"",
            ""operator"": ""OR"",
            ""rules"": [
              { ""field"": ""Order.Total"", ""operator"": ""GreaterThanOrEqual"", ""value"": 100 },
              { ""field"": ""Customer.IsVip"", ""operator"": ""Equals"", ""value"": true }
            ]
          }
        ]
      },
      ""actions"": [
        { ""type"": ""setOutput"", ""target"": ""Discount"", ""value"": 10 },
        { ""type"": ""setOutput"", ""target"": ""DiscountReason"", ""value"": ""VIP or high-value order"" }
      ],
      ""layout"": {
        ""position"": { ""x"": 120, ""y"": 40 },
        ""nodeIds"": { ""condition-root"": ""n1"", ""action-0"": ""n2"" }
      }
    }";

    [Fact]
    public void SpecExample_IsValid()
    {
        RuleSetValidationResult result = Validate(SpecExample);
        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.ToString())));
    }

    [Fact]
    public void NonObjectRoot_ErrorAtRoot()
    {
        RuleSetValidationResult result = Validate("[1, 2]");
        Assert.False(result.IsValid);
        Assert.Equal(string.Empty, result.Errors.Single().Path);
    }

    [Fact]
    public void MissingId_IsReported()
    {
        RuleSetValidationResult result = Validate(
            "{\"condition\": {\"field\": \"A\", \"operator\": \"IsNull\"}}");
        Assert.Contains(result.Errors, e => e.Message.Contains("'id'"));
    }

    [Fact]
    public void UnknownOperator_PointerTargetsNestedNode()
    {
        RuleSetValidationResult result = Validate(@"{
          ""id"": ""r"",
          ""condition"": {
            ""type"": ""group"", ""operator"": ""AND"",
            ""rules"": [
              { ""field"": ""A"", ""operator"": ""IsNull"" },
              { ""field"": ""B"", ""operator"": ""LooksLike"", ""value"": 1 }
            ]
          }
        }");
        RuleValidationError error = Assert.Single(result.Errors);
        Assert.Equal("/condition/rules/1/operator", error.Path);
    }

    [Fact]
    public void NotGroup_WithTwoChildren_IsReported()
    {
        RuleSetValidationResult result = Validate(@"{
          ""id"": ""r"",
          ""condition"": {
            ""type"": ""group"", ""operator"": ""NOT"",
            ""rules"": [
              { ""field"": ""A"", ""operator"": ""IsNull"" },
              { ""field"": ""B"", ""operator"": ""IsNull"" }
            ]
          }
        }");
        Assert.Contains(result.Errors, e => e.Path == "/condition/rules" && e.Message.Contains("exactly one"));
    }

    [Fact]
    public void InOperator_RequiresNonEmptyArray()
    {
        Assert.False(Validate("{\"id\":\"r\",\"condition\":{\"field\":\"A\",\"operator\":\"In\",\"value\":5}}").IsValid);
        Assert.False(Validate("{\"id\":\"r\",\"condition\":{\"field\":\"A\",\"operator\":\"In\",\"value\":[]}}").IsValid);
        Assert.True(Validate("{\"id\":\"r\",\"condition\":{\"field\":\"A\",\"operator\":\"In\",\"value\":[1,2]}}").IsValid);
    }

    [Fact]
    public void IsNull_WithValue_IsReported()
    {
        RuleSetValidationResult result = Validate(
            "{\"id\":\"r\",\"condition\":{\"field\":\"A\",\"operator\":\"IsNull\",\"value\":1}}");
        Assert.Contains(result.Errors, e => e.Path == "/condition/value");
    }

    [Fact]
    public void CustomOperator_RequiresName()
    {
        Assert.False(Validate("{\"id\":\"r\",\"condition\":{\"operator\":\"custom\"}}").IsValid);
        Assert.True(Validate("{\"id\":\"r\",\"condition\":{\"operator\":\"custom\",\"name\":\"F\"}}").IsValid);
    }

    [Fact]
    public void InvalidRegex_IsReported()
    {
        RuleSetValidationResult result = Validate(
            "{\"id\":\"r\",\"condition\":{\"field\":\"A\",\"operator\":\"MatchesRegex\",\"value\":\"[unclosed\"}}");
        Assert.Contains(result.Errors, e => e.Path == "/condition/value" && e.Message.Contains("regular expression"));
    }

    [Fact]
    public void FractionalPriority_IsReported()
    {
        RuleSetValidationResult result = Validate(
            "{\"id\":\"r\",\"priority\":1.5,\"condition\":{\"field\":\"A\",\"operator\":\"IsNull\"}}");
        Assert.Contains(result.Errors, e => e.Path == "/priority");
    }

    [Fact]
    public void UnknownActionType_IsReported()
    {
        RuleSetValidationResult result = Validate(@"{
          ""id"": ""r"",
          ""condition"": { ""field"": ""A"", ""operator"": ""IsNull"" },
          ""actions"": [ { ""type"": ""sendEmail"", ""target"": ""T"", ""value"": 1 } ]
        }");
        Assert.Contains(result.Errors, e => e.Path == "/actions/0/type");
    }

    [Fact]
    public void RuleSet_DuplicateIds_AreReported()
    {
        RuleSetValidationResult result = Validate(@"{
          ""rules"": [
            { ""id"": ""a"", ""condition"": { ""field"": ""X"", ""operator"": ""IsNull"" } },
            { ""id"": ""a"", ""condition"": { ""field"": ""Y"", ""operator"": ""IsNull"" } }
          ]
        }");
        Assert.Contains(result.Errors, e => e.Path == "/rules/1/id" && e.Message.Contains("Duplicate"));
    }

    [Fact]
    public void RuleSet_ErrorsCarryRuleIndexInPointer()
    {
        RuleSetValidationResult result = Validate(@"{
          ""rules"": [
            { ""id"": ""a"", ""condition"": { ""field"": ""X"", ""operator"": ""IsNull"" } },
            { ""id"": ""b"", ""condition"": { ""field"": ""Y"", ""operator"": ""Nope"" } }
          ]
        }");
        RuleValidationError error = Assert.Single(result.Errors);
        Assert.Equal("/rules/1/condition/operator", error.Path);
    }

    [Fact]
    public void RemoveOutput_WithoutValue_IsValid()
    {
        RuleSetValidationResult result = Validate(@"{
          ""id"": ""r"",
          ""condition"": { ""field"": ""A"", ""operator"": ""IsNull"" },
          ""actions"": [ { ""type"": ""removeOutput"", ""target"": ""T"" } ]
        }");
        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.ToString())));
    }

    [Fact]
    public void RemoveOutput_WithValue_IsReported()
    {
        RuleSetValidationResult result = Validate(@"{
          ""id"": ""r"",
          ""condition"": { ""field"": ""A"", ""operator"": ""IsNull"" },
          ""actions"": [ { ""type"": ""removeOutput"", ""target"": ""T"", ""value"": 1 } ]
        }");
        Assert.Contains(result.Errors, e => e.Path == "/actions/0/value" && e.Message.Contains("not allowed"));
    }

    [Fact]
    public void NonRemoveActionWithoutValue_IsReported()
    {
        RuleSetValidationResult result = Validate(@"{
          ""id"": ""r"",
          ""condition"": { ""field"": ""A"", ""operator"": ""IsNull"" },
          ""actions"": [ { ""type"": ""setOutput"", ""target"": ""T"" } ]
        }");
        Assert.Contains(result.Errors, e => e.Path == "/actions/0" && e.Message.Contains("'value' is required"));
    }

    [Fact]
    public void ElseActions_AreValidatedWithElsePointer()
    {
        RuleSetValidationResult result = Validate(@"{
          ""id"": ""r"",
          ""condition"": { ""field"": ""A"", ""operator"": ""IsNull"" },
          ""else"": [ { ""type"": ""sendEmail"", ""target"": ""T"", ""value"": 1 } ]
        }");
        Assert.Contains(result.Errors, e => e.Path == "/else/0/type");
    }

    [Fact]
    public void Else_MustBeArray()
    {
        RuleSetValidationResult result = Validate(@"{
          ""id"": ""r"",
          ""condition"": { ""field"": ""A"", ""operator"": ""IsNull"" },
          ""else"": { ""type"": ""setOutput"", ""target"": ""T"", ""value"": 1 }
        }");
        Assert.Contains(result.Errors, e => e.Path == "/else" && e.Message.Contains("must be an array"));
    }

    /// <summary>
    /// The vocabulary is closed, so an undefined key is almost always a typo. Silently ignoring
    /// "actons" produced a rule that fired and wrote nothing - far harder to spot than an error.
    /// </summary>
    [Fact]
    public void UnknownProperty_IsReportedAtItsOwnPointer()
    {
        RuleSetValidationResult rule = Validate(@"{ ""id"": ""r"",
            ""condition"": { ""field"": ""A"", ""operator"": ""IsNull"" },
            ""actons"": [] }");
        Assert.Contains(rule.Errors, e => e.Path == "/actons" && e.Message.Contains("Unknown property"));

        RuleSetValidationResult leaf = Validate(@"{ ""id"": ""r"",
            ""condition"": { ""feild"": ""A"", ""operator"": ""IsNull"" } }");
        Assert.Contains(leaf.Errors, e => e.Path == "/condition/feild");

        RuleSetValidationResult action = Validate(@"{ ""id"": ""r"",
            ""condition"": { ""field"": ""A"", ""operator"": ""IsNull"" },
            ""actions"": [ { ""type"": ""setOutput"", ""targt"": ""X"", ""value"": 1 } ] }");
        Assert.Contains(action.Errors, e => e.Path == "/actions/0/targt");
    }

    /// <summary>The keys the schema does define stay accepted, including free-text documentation.</summary>
    [Fact]
    public void KnownProperties_IncludingDescriptions_AreAccepted()
    {
        RuleSetValidationResult result = Validate(@"{
            ""name"": ""set"", ""description"": ""what this set is for"",
            ""rules"": [ { ""id"": ""r"", ""description"": ""d"", ""priority"": 1, ""enabled"": true,
                ""layout"": { ""x"": 1 },
                ""condition"": { ""field"": ""A"", ""operator"": ""IsNull"" },
                ""actions"": [ { ""type"": ""setOutput"", ""target"": ""X"", ""value"": 1 } ],
                ""else"": [ { ""type"": ""removeOutput"", ""target"": ""X"" } ] } ] }");
        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.Path + " " + e.Message)));
    }

    /// <summary>A document is a table or a rule set; carrying both silently dropped the rules.</summary>
    [Fact]
    public void DecisionTableAndRulesTogether_IsReported()
    {
        RuleSetValidationResult result = Validate(@"{
            ""decisionTable"": { ""inputs"": [ { ""field"": ""A"" } ], ""outputs"": [ { ""target"": ""D"" } ],
                ""rows"": [ { ""when"": [1], ""then"": [1] } ] },
            ""rules"": [ { ""id"": ""r"", ""condition"": { ""field"": ""A"", ""operator"": ""IsNull"" } } ] }");
        Assert.Contains(result.Errors, e => e.Message.Contains("not both"));
    }
}
