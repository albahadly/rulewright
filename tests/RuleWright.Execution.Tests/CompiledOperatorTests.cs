using RuleWright.Core;
using Xunit;
using static RuleWright.Execution.Tests.TestEngine;

namespace RuleWright.Execution.Tests;

/// <summary>
/// Operator matrix for the compiled (typed-fact) path: every operator, null handling,
/// type conversion, and compile-time error surfaces.
/// </summary>
public class CompiledOperatorTests
{
    [Theory]
    [InlineData("{\"field\":\"Customer.Age\",\"operator\":\"Equals\",\"value\":21}", true)]
    [InlineData("{\"field\":\"Customer.Age\",\"operator\":\"Equals\",\"value\":22}", false)]
    [InlineData("{\"field\":\"Customer.Age\",\"operator\":\"NotEquals\",\"value\":22}", true)]
    [InlineData("{\"field\":\"Customer.Age\",\"operator\":\"GreaterThan\",\"value\":18}", true)]
    [InlineData("{\"field\":\"Customer.Age\",\"operator\":\"GreaterThan\",\"value\":21}", false)]
    [InlineData("{\"field\":\"Customer.Age\",\"operator\":\"GreaterThanOrEqual\",\"value\":21}", true)]
    [InlineData("{\"field\":\"Customer.Age\",\"operator\":\"LessThan\",\"value\":30}", true)]
    [InlineData("{\"field\":\"Customer.Age\",\"operator\":\"LessThanOrEqual\",\"value\":20}", false)]
    public void IntComparisons(string condition, bool expected)
        => Assert.Equal(expected, Matches(condition, DefaultFact()));

    [Theory]
    [InlineData("{\"field\":\"Customer.Age\",\"operator\":\"GreaterThan\",\"value\":20.5}", true)]
    [InlineData("{\"field\":\"Customer.Age\",\"operator\":\"GreaterThan\",\"value\":21.5}", false)]
    [InlineData("{\"field\":\"Customer.Age\",\"operator\":\"Equals\",\"value\":21.5}", false)]
    [InlineData("{\"field\":\"Customer.Age\",\"operator\":\"NotEquals\",\"value\":21.5}", true)]
    public void FractionalComparandAgainstIntField(string condition, bool expected)
        => Assert.Equal(expected, Matches(condition, DefaultFact()));

    [Theory]
    [InlineData("{\"field\":\"Order.Total\",\"operator\":\"GreaterThanOrEqual\",\"value\":100}", true)]
    [InlineData("{\"field\":\"Order.Total\",\"operator\":\"Equals\",\"value\":120.5}", true)]
    [InlineData("{\"field\":\"Order.Weight\",\"operator\":\"LessThanOrEqual\",\"value\":2.4}", true)]
    [InlineData("{\"field\":\"Order.Weight\",\"operator\":\"GreaterThan\",\"value\":3}", false)]
    public void DecimalAndDoubleComparisons(string condition, bool expected)
        => Assert.Equal(expected, Matches(condition, DefaultFact()));

    [Theory]
    [InlineData("{\"field\":\"Customer.IsVip\",\"operator\":\"Equals\",\"value\":true}", true)]
    [InlineData("{\"field\":\"Customer.IsVip\",\"operator\":\"NotEquals\",\"value\":true}", false)]
    [InlineData("{\"field\":\"Customer.Name\",\"operator\":\"Equals\",\"value\":\"Alice\"}", true)]
    [InlineData("{\"field\":\"Customer.Name\",\"operator\":\"Equals\",\"value\":\"alice\"}", false)]
    [InlineData("{\"field\":\"Customer.Tier\",\"operator\":\"Equals\",\"value\":\"Gold\"}", true)]
    [InlineData("{\"field\":\"Customer.Tier\",\"operator\":\"Equals\",\"value\":\"Silver\"}", false)]
    public void BoolStringAndEnumEquality(string condition, bool expected)
        => Assert.Equal(expected, Matches(condition, DefaultFact()));

    [Theory]
    [InlineData("{\"field\":\"Customer.JoinedOn\",\"operator\":\"GreaterThan\",\"value\":\"2020-01-01\"}", true)]
    [InlineData("{\"field\":\"Customer.JoinedOn\",\"operator\":\"LessThan\",\"value\":\"2021-01-01\"}", false)]
    public void DateTimeComparisonsFromIsoStrings(string condition, bool expected)
        => Assert.Equal(expected, Matches(condition, DefaultFact()));

    [Fact]
    public void StringOrdering_UsesOrdinalComparerFallback()
    {
        Assert.True(Matches("{\"field\":\"Customer.Name\",\"operator\":\"GreaterThan\",\"value\":\"Aaa\"}", DefaultFact()));
        Assert.False(Matches("{\"field\":\"Customer.Name\",\"operator\":\"GreaterThan\",\"value\":\"Bob\"}", DefaultFact()));
    }

    [Theory]
    [InlineData("{\"field\":\"Customer.Name\",\"operator\":\"Contains\",\"value\":\"lic\"}", true)]
    [InlineData("{\"field\":\"Customer.Name\",\"operator\":\"Contains\",\"value\":\"LIC\"}", false)]
    [InlineData("{\"field\":\"Customer.Name\",\"operator\":\"StartsWith\",\"value\":\"Al\"}", true)]
    [InlineData("{\"field\":\"Customer.Name\",\"operator\":\"EndsWith\",\"value\":\"ce\"}", true)]
    [InlineData("{\"field\":\"Customer.Email\",\"operator\":\"MatchesRegex\",\"value\":\"^[^@]+@example\\\\.com$\"}", true)]
    [InlineData("{\"field\":\"Customer.Email\",\"operator\":\"MatchesRegex\",\"value\":\"^bob@\"}", false)]
    public void StringOperators_AreOrdinal(string condition, bool expected)
        => Assert.Equal(expected, Matches(condition, DefaultFact()));

    [Fact]
    public void StringOperators_NullField_AreFalse()
    {
        var fact = DefaultFact();
        fact.Customer.Email = null;
        Assert.False(Matches("{\"field\":\"Customer.Email\",\"operator\":\"Contains\",\"value\":\"a\"}", fact));
        Assert.False(Matches("{\"field\":\"Customer.Email\",\"operator\":\"MatchesRegex\",\"value\":\".*\"}", fact));
    }

    [Theory]
    [InlineData("{\"field\":\"Customer.Age\",\"operator\":\"In\",\"value\":[18,21,65]}", true)]
    [InlineData("{\"field\":\"Customer.Age\",\"operator\":\"In\",\"value\":[18,65]}", false)]
    [InlineData("{\"field\":\"Customer.Age\",\"operator\":\"NotIn\",\"value\":[18,65]}", true)]
    [InlineData("{\"field\":\"Customer.Tier\",\"operator\":\"In\",\"value\":[\"Gold\",\"Silver\"]}", true)]
    [InlineData("{\"field\":\"Customer.Age\",\"operator\":\"In\",\"value\":[17.5]}", false)]
    public void InAndNotIn(string condition, bool expected)
        => Assert.Equal(expected, Matches(condition, DefaultFact()));

    [Fact]
    public void InAndNotIn_NullField()
    {
        var fact = DefaultFact();
        fact.Order!.Coupon = null;
        Assert.False(Matches("{\"field\":\"Order.Coupon\",\"operator\":\"In\",\"value\":[\"SPRING\"]}", fact));
        Assert.True(Matches("{\"field\":\"Order.Coupon\",\"operator\":\"NotIn\",\"value\":[\"SPRING\"]}", fact));
    }

    [Fact]
    public void IsNullAndIsNotNull()
    {
        var fact = DefaultFact();
        fact.Order = null;
        fact.Customer.LoyaltyYears = null;

        Assert.True(Matches("{\"field\":\"Order\",\"operator\":\"IsNull\"}", fact));
        Assert.False(Matches("{\"field\":\"Order\",\"operator\":\"IsNotNull\"}", fact));
        Assert.True(Matches("{\"field\":\"Customer.LoyaltyYears\",\"operator\":\"IsNull\"}", fact));
        Assert.True(Matches("{\"field\":\"Customer\",\"operator\":\"IsNotNull\"}", fact));
    }

    [Fact]
    public void NullSafeNavigation_IntermediateNull()
    {
        var fact = DefaultFact();
        fact.Customer.Address = null;

        // Address is null: descending into Address.City applies the operator's null semantics.
        Assert.True(Matches("{\"field\":\"Customer.Address.City\",\"operator\":\"IsNull\"}", fact));
        Assert.False(Matches("{\"field\":\"Customer.Address.City\",\"operator\":\"Equals\",\"value\":\"Amsterdam\"}", fact));
        Assert.True(Matches("{\"field\":\"Customer.Address.City\",\"operator\":\"NotEquals\",\"value\":\"Amsterdam\"}", fact));
        Assert.False(Matches("{\"field\":\"Customer.Address.City\",\"operator\":\"Contains\",\"value\":\"dam\"}", fact));
    }

    [Fact]
    public void NullSafeNavigation_NullOrderComparisons()
    {
        var fact = DefaultFact();
        fact.Order = null;
        Assert.False(Matches("{\"field\":\"Order.Total\",\"operator\":\"GreaterThan\",\"value\":10}", fact));
        Assert.True(Matches("{\"field\":\"Order.Total\",\"operator\":\"NotEquals\",\"value\":10}", fact));
    }

    [Fact]
    public void NullableField_ComparesThroughHasValue()
    {
        var fact = DefaultFact();
        Assert.True(Matches("{\"field\":\"Customer.LoyaltyYears\",\"operator\":\"GreaterThan\",\"value\":2}", fact));

        fact.Customer.LoyaltyYears = null;
        Assert.False(Matches("{\"field\":\"Customer.LoyaltyYears\",\"operator\":\"GreaterThan\",\"value\":2}", fact));
        Assert.True(Matches("{\"field\":\"Customer.LoyaltyYears\",\"operator\":\"Equals\",\"value\":null}", fact));
    }

    [Fact]
    public void PublicField_IsAccessible()
    {
        Assert.True(Matches("{\"field\":\"Tag\",\"operator\":\"Equals\",\"value\":\"priority\"}", DefaultFact()));
    }

    [Fact]
    public void GroupCombinators_AndOrNot()
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

        Assert.True(Matches(specCondition, DefaultFact()));

        var minor = DefaultFact();
        minor.Customer.Age = 16;
        Assert.False(Matches(specCondition, minor));

        var poorNonVip = DefaultFact();
        poorNonVip.Customer.IsVip = false;
        poorNonVip.Order!.Total = 50;
        Assert.False(Matches(specCondition, poorNonVip));

        const string notCondition = @"{
          ""type"": ""group"", ""operator"": ""NOT"",
          ""rules"": [ { ""field"": ""Customer.IsVip"", ""operator"": ""Equals"", ""value"": true } ]
        }";
        Assert.False(Matches(notCondition, DefaultFact()));
    }

    [Fact]
    public void CustomFunctions_WithAndWithoutField()
    {
        var fact = DefaultFact();
        fact.Customer.Age = 42;
        Assert.True(Matches("{\"field\":\"Customer.Age\",\"operator\":\"custom\",\"name\":\"FieldIsFortyTwo\"}", fact));
        Assert.False(Matches("{\"field\":\"Customer.Age\",\"operator\":\"custom\",\"name\":\"FieldIsFortyTwo\"}", DefaultFact()));
        Assert.True(Matches("{\"operator\":\"custom\",\"name\":\"AlwaysTrue\"}", DefaultFact()));
    }

    [Fact]
    public void TypedResult_ReportsCompiledMode()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(WrapRule("{\"field\":\"Customer.Age\",\"operator\":\"GreaterThan\",\"value\":18}"));
        Assert.Equal(CompilationMode.Compiled, Engine.Evaluate(loaded, DefaultFact()).CompilationMode);
    }

    [Fact]
    public void MissingMemberPath_ThrowsRuleCompilationException()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(WrapRule("{\"field\":\"Customer.Nope\",\"operator\":\"IsNull\"}"));
        var exception = Assert.Throws<RuleCompilationException>(() => Engine.Evaluate(loaded, DefaultFact()));
        Assert.Contains("Nope", exception.Message);
        Assert.Equal("test-rule", exception.RuleId);
    }

    [Fact]
    public void StringValueAgainstNumericField_ThrowsRuleCompilationException()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(WrapRule("{\"field\":\"Customer.Age\",\"operator\":\"Equals\",\"value\":\"21\"}"));
        Assert.Throws<RuleCompilationException>(() => Engine.Evaluate(loaded, DefaultFact()));
    }

    [Fact]
    public void StringOperatorOnNumericField_ThrowsRuleCompilationException()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(WrapRule("{\"field\":\"Customer.Age\",\"operator\":\"Contains\",\"value\":\"1\"}"));
        Assert.Throws<RuleCompilationException>(() => Engine.Evaluate(loaded, DefaultFact()));
    }

    [Fact]
    public void UnregisteredFunction_ThrowsAtLoad()
    {
        Assert.Throws<RuleCompilationException>(
            () => Engine.LoadRuleSet(WrapRule("{\"operator\":\"custom\",\"name\":\"NoSuchFunction\"}")));
    }
}
