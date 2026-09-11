using RuleWright.Core;
using RuleWright.Json.SystemText;
using RuleWright.Serialization;
using Xunit;

namespace RuleWright.Serialization.Tests;

public class RuleHasherTests
{
    private static Rule ParseRule(string json)
        => RuleSetParser.Parse(new SystemTextJsonReader().Read(json)).Rules[0];

    [Fact]
    public void Hash_IgnoresWhitespaceAndKeyOrder()
    {
        Rule compact = ParseRule(
            "{\"id\":\"r\",\"condition\":{\"field\":\"A\",\"operator\":\"Equals\",\"value\":1},\"actions\":[{\"type\":\"setOutput\",\"target\":\"T\",\"value\":true}]}");
        Rule reordered = ParseRule(@"{
          ""actions"": [ { ""value"": true, ""target"": ""T"", ""type"": ""setOutput"" } ],
          ""condition"": { ""value"": 1, ""operator"": ""Equals"", ""field"": ""A"" },
          ""id"": ""r""
        }");

        Assert.Equal(RuleHasher.ComputeHash(compact), RuleHasher.ComputeHash(reordered));
    }

    [Fact]
    public void Hash_IgnoresLayoutDescriptionPriorityEnabledAndId()
    {
        Rule bare = ParseRule(
            "{\"id\":\"r1\",\"condition\":{\"field\":\"A\",\"operator\":\"Equals\",\"value\":1}}");
        Rule decorated = ParseRule(@"{
          ""id"": ""r2-different"",
          ""description"": ""different description"",
          ""priority"": 99,
          ""enabled"": false,
          ""condition"": { ""field"": ""A"", ""operator"": ""Equals"", ""value"": 1 },
          ""layout"": { ""position"": { ""x"": 5, ""y"": 6 } }
        }");

        Assert.Equal(RuleHasher.ComputeHash(bare), RuleHasher.ComputeHash(decorated));
    }

    [Fact]
    public void Hash_ChangesWhenConditionValueChanges()
    {
        Rule one = ParseRule("{\"id\":\"r\",\"condition\":{\"field\":\"A\",\"operator\":\"Equals\",\"value\":1}}");
        Rule two = ParseRule("{\"id\":\"r\",\"condition\":{\"field\":\"A\",\"operator\":\"Equals\",\"value\":2}}");
        Assert.NotEqual(RuleHasher.ComputeHash(one), RuleHasher.ComputeHash(two));
    }

    [Fact]
    public void Hash_ChangesWhenActionsChange()
    {
        Rule one = ParseRule(
            "{\"id\":\"r\",\"condition\":{\"field\":\"A\",\"operator\":\"IsNull\"},\"actions\":[{\"type\":\"setOutput\",\"target\":\"T\",\"value\":1}]}");
        Rule two = ParseRule(
            "{\"id\":\"r\",\"condition\":{\"field\":\"A\",\"operator\":\"IsNull\"},\"actions\":[{\"type\":\"setOutput\",\"target\":\"T\",\"value\":2}]}");
        Assert.NotEqual(RuleHasher.ComputeHash(one), RuleHasher.ComputeHash(two));
    }

    [Fact]
    public void Hash_TreatsNumericallyEqualDecimalsAsEqual()
    {
        Rule plain = ParseRule("{\"id\":\"r\",\"condition\":{\"field\":\"A\",\"operator\":\"Equals\",\"value\":10.5}}");
        Rule trailingZero = ParseRule("{\"id\":\"r\",\"condition\":{\"field\":\"A\",\"operator\":\"Equals\",\"value\":10.50}}");
        Assert.Equal(RuleHasher.ComputeHash(plain), RuleHasher.ComputeHash(trailingZero));
    }

    [Fact]
    public void CanonicalForm_IsDeterministicJson()
    {
        Rule rule = ParseRule(
            "{\"id\":\"r\",\"condition\":{\"field\":\"A\",\"operator\":\"Equals\",\"value\":\"x\"},\"actions\":[{\"type\":\"setOutput\",\"target\":\"T\",\"value\":10}]}");
        Assert.Equal(
            "{\"actions\":[{\"target\":\"T\",\"type\":\"setOutput\",\"value\":10}],"
            + "\"condition\":{\"field\":\"A\",\"operator\":\"Equals\",\"value\":\"x\"}}",
            RuleHasher.GetCanonicalForm(rule));
    }

    [Fact]
    public void Hash_ChangesWhenElseBranchAdded()
    {
        Rule without = ParseRule(
            "{\"id\":\"r\",\"condition\":{\"field\":\"A\",\"operator\":\"IsNull\"},\"actions\":[{\"type\":\"setOutput\",\"target\":\"T\",\"value\":1}]}");
        Rule with = ParseRule(
            "{\"id\":\"r\",\"condition\":{\"field\":\"A\",\"operator\":\"IsNull\"},\"actions\":[{\"type\":\"setOutput\",\"target\":\"T\",\"value\":1}],"
            + "\"else\":[{\"type\":\"setOutput\",\"target\":\"T\",\"value\":2}]}");
        Assert.NotEqual(RuleHasher.ComputeHash(without), RuleHasher.ComputeHash(with));
    }

    [Fact]
    public void CanonicalForm_IncludesElseBranch()
    {
        Rule rule = ParseRule(
            "{\"id\":\"r\",\"condition\":{\"field\":\"A\",\"operator\":\"IsNull\"},\"actions\":[{\"type\":\"setOutput\",\"target\":\"T\",\"value\":1}],"
            + "\"else\":[{\"type\":\"setOutput\",\"target\":\"T\",\"value\":2}]}");
        Assert.Equal(
            "{\"actions\":[{\"target\":\"T\",\"type\":\"setOutput\",\"value\":1}],"
            + "\"condition\":{\"field\":\"A\",\"operator\":\"IsNull\"},"
            + "\"else\":[{\"target\":\"T\",\"type\":\"setOutput\",\"value\":2}]}",
            RuleHasher.GetCanonicalForm(rule));
    }

    [Fact]
    public void CanonicalForm_RemoveOutput_OmitsValue()
    {
        Rule rule = ParseRule(
            "{\"id\":\"r\",\"condition\":{\"field\":\"A\",\"operator\":\"IsNull\"},\"actions\":[{\"type\":\"removeOutput\",\"target\":\"T\"}]}");
        Assert.Equal(
            "{\"actions\":[{\"target\":\"T\",\"type\":\"removeOutput\"}],"
            + "\"condition\":{\"field\":\"A\",\"operator\":\"IsNull\"}}",
            RuleHasher.GetCanonicalForm(rule));
    }

    [Fact]
    public void Hash_RemoveOutput_IsValueIndependent()
    {
        // removeOutput ignores its value, so a value built by the factory versus none hash alike.
        var viaFactory = new Rule(
            "r",
            new ConditionLeaf("A", ConditionOperator.IsNull, null),
            new[] { RuleAction.RemoveOutput("T") });
        var withStrayValue = new Rule(
            "r",
            new ConditionLeaf("A", ConditionOperator.IsNull, null),
            new[] { new RuleAction(RuleAction.RemoveOutputType, "T", "ignored") });
        Assert.Equal(RuleHasher.ComputeHash(viaFactory), RuleHasher.ComputeHash(withStrayValue));
    }

    [Fact]
    public void Hash_DistinguishesBinaryFloatingLiteralsFromIntegralOnes()
    {
        // double/float arithmetic runs in double; long/decimal runs in decimal. Two rules that
        // differ only in that respect must not share a compiled-delegate cache entry.
        var integral = new Rule(
            "r",
            new ConditionLeaf("A", ConditionOperator.IsNull, null),
            new[] { new RuleAction(RuleAction.SetOutputType, "T", new LiteralExpression(1L)) });
        var floating = new Rule(
            "r",
            new ConditionLeaf("A", ConditionOperator.IsNull, null),
            new[] { new RuleAction(RuleAction.SetOutputType, "T", new LiteralExpression(1.0d)) });
        var single = new Rule(
            "r",
            new ConditionLeaf("A", ConditionOperator.IsNull, null),
            new[] { new RuleAction(RuleAction.SetOutputType, "T", new LiteralExpression(1.0f)) });

        Assert.NotEqual(RuleHasher.ComputeHash(integral), RuleHasher.ComputeHash(floating));
        Assert.NotEqual(RuleHasher.ComputeHash(integral), RuleHasher.ComputeHash(single));
    }

    [Fact]
    public void Hash_DistinguishesFloatingFromDecimalWithTheSameText()
    {
        var asDecimal = new Rule(
            "r",
            new ConditionLeaf("A", ConditionOperator.Equal, 2.5m),
            null);
        var asDouble = new Rule(
            "r",
            new ConditionLeaf("A", ConditionOperator.Equal, 2.5d),
            null);

        Assert.NotEqual(RuleHasher.ComputeHash(asDecimal), RuleHasher.ComputeHash(asDouble));
    }
}
