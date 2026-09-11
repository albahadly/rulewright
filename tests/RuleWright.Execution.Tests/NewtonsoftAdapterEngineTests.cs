using Newtonsoft.Json.Linq;
using RuleWright.Core;
using RuleWright.Json.NewtonsoftJson;
using RuleWright.Json.SystemText;
using Xunit;
using static RuleWright.Execution.Tests.TestEngine;

namespace RuleWright.Execution.Tests;

/// <summary>
/// End-to-end proof that the engine behaves identically whichever JSON adapter loads the rules or
/// builds the fact: the same document and payload through Newtonsoft.Json and System.Text.Json
/// must fire the same rules and produce the same outputs.
/// </summary>
public class NewtonsoftAdapterEngineTests
{
    private const string RulesJson = @"{
      ""rules"": [
        { ""id"": ""vip"", ""priority"": 10,
          ""condition"": { ""type"": ""group"", ""operator"": ""AND"", ""rules"": [
            { ""field"": ""Customer.Age"", ""operator"": ""GreaterThan"", ""value"": 18 },
            { ""field"": ""Customer.IsVip"", ""operator"": ""Equals"", ""value"": true } ] },
          ""actions"": [ { ""type"": ""setOutput"", ""target"": ""Discount"", ""value"": 10.5 } ] },
        { ""id"": ""big-order"",
          ""condition"": { ""field"": ""Order.Total"", ""operator"": ""GreaterThanOrEqual"", ""value"": 100 },
          ""actions"": [ { ""type"": ""appendToOutput"", ""target"": ""Tags"", ""value"": ""big"" } ] }
      ]
    }";

    private static RuleWrightEngine NewtonsoftEngine()
        => new RuleWrightBuilder().UseJsonReader(new NewtonsoftJsonReader()).Build();

    private static RuleWrightEngine SystemTextEngine()
        => new RuleWrightBuilder().UseJsonReader(new SystemTextJsonReader()).Build();

    [Fact]
    public void TypedFact_SameResultWhicheverReaderLoadsTheRules()
    {
        RuleEvaluationResult viaNewtonsoft = Evaluate(NewtonsoftEngine());
        RuleEvaluationResult viaSystemText = Evaluate(SystemTextEngine());

        Assert.Equal(
            viaSystemText.FiredRules.Select(f => f.RuleId),
            viaNewtonsoft.FiredRules.Select(f => f.RuleId));
        Assert.Equal(10.5m, viaNewtonsoft.Outputs["Discount"]);
        Assert.Equal(viaSystemText.Outputs["Discount"], viaNewtonsoft.Outputs["Discount"]);

        static RuleEvaluationResult Evaluate(RuleWrightEngine engine)
            => engine.Evaluate(engine.LoadRuleSet(RulesJson), DefaultFact());
    }

    [Fact]
    public void DictionaryFact_FromNewtonsoftMatchesSystemText()
    {
        const string payload = @"{ ""Customer"": { ""Age"": 21, ""IsVip"": true }, ""Order"": { ""Total"": 150.0 } }";

        var newtonsoftFact = NewtonsoftJsonFacts.ToDictionary(JToken.Parse(payload));
        var systemTextFact = SystemTextJsonFacts.ToDictionary(System.Text.Json.JsonDocument.Parse(payload).RootElement);

        RuleWrightEngine engine = NewtonsoftEngine();
        LoadedRuleSet rules = engine.LoadRuleSet(RulesJson);

        RuleEvaluationResult fromNewtonsoft = engine.Evaluate(rules, newtonsoftFact);
        RuleEvaluationResult fromSystemText = engine.Evaluate(rules, systemTextFact);

        Assert.Equal(
            fromSystemText.FiredRules.Select(f => f.RuleId),
            fromNewtonsoft.FiredRules.Select(f => f.RuleId));
        Assert.Equal(fromSystemText.Outputs["Discount"], fromNewtonsoft.Outputs["Discount"]);
    }
}
