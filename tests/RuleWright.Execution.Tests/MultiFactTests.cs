using RuleWright.Core;
using Xunit;
using static RuleWright.Execution.Tests.TestEngine;

namespace RuleWright.Execution.Tests;

/// <summary>
/// <see cref="RuleFacts"/>: several named facts evaluated as one, each addressed by its name
/// as the first field-path segment. A <see cref="RuleFacts"/> is a dictionary fact, so it runs
/// the interpreter and says so via <see cref="CompilationMode.Interpreted"/>.
/// </summary>
public class MultiFactTests
{
    private sealed class Basket
    {
        public decimal Total { get; set; }
    }

    private sealed class Person
    {
        public int Age { get; set; }
    }

    private const string Json = @"{
      ""id"": ""vip"",
      ""condition"": {
        ""type"": ""group"", ""operator"": ""AND"",
        ""rules"": [
          { ""field"": ""customer.Age"",  ""operator"": ""GreaterThanOrEqual"", ""value"": 18 },
          { ""field"": ""order.Total"",   ""operator"": ""GreaterThan"",        ""value"": 100 }
        ]
      },
      ""actions"": [
        { ""type"": ""setOutput"", ""target"": ""Discount"",
          ""value"": { ""op"": ""multiply"", ""operands"": [ { ""field"": ""order.Total"" }, 0.1 ] } }
      ]
    }";

    [Fact]
    public void NamedFacts_ResolveByFirstPathSegment()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(Json);
        RuleFacts facts = RuleFacts.With("customer", new Person { Age = 30 })
            .And("order", new Basket { Total = 150m });

        RuleEvaluationResult result = Engine.Evaluate(loaded, facts);

        Assert.Equal(CompilationMode.Interpreted, result.CompilationMode);
        Assert.Single(result.FiredRules);
        Assert.Equal(15.0m, result.Outputs["Discount"]);
    }

    [Fact]
    public void MissingNamedFact_BehavesAsNull()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(Json);
        RuleFacts facts = RuleFacts.With("customer", new Person { Age = 30 });

        Assert.Empty(Engine.Evaluate(loaded, facts).FiredRules);
    }

    [Fact]
    public void FactNames_MatchCaseInsensitively_WhenAsked()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(Json);
        RuleFacts facts = RuleFacts.With("Customer", new Person { Age = 30 }, ignoreCase: true)
            .And("ORDER", new Basket { Total = 150m });

        Assert.Single(Engine.Evaluate(loaded, facts).FiredRules);
    }

    [Fact]
    public void DuplicateFactName_Throws()
        => Assert.Throws<ArgumentException>(
            () => RuleFacts.With("customer", new Person()).And("customer", new Person()));
}
