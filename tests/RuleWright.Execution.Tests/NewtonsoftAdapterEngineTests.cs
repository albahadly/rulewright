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

    // A camelCase payload, as HttpClient's JSON helpers and ASP.NET Core write by default. The
    // rules below read PascalCase paths, including one through a collection's elements.
    private const string CamelCasePayload =
        @"{ ""customer"": { ""age"": 21, ""isVip"": true }, ""order"": { ""total"": 150.0, ""lines"": [ { ""category"": ""alcohol"" } ] } }";

    private const string CaseRulesJson = @"{
      ""rules"": [
        { ""id"": ""vip"",
          ""condition"": { ""type"": ""group"", ""operator"": ""AND"", ""rules"": [
            { ""field"": ""Customer.Age"", ""operator"": ""GreaterThan"", ""value"": 18 },
            { ""field"": ""Customer.IsVip"", ""operator"": ""Equals"", ""value"": true } ] },
          ""actions"": [ { ""type"": ""setOutput"", ""target"": ""Discount"", ""value"": { ""op"": ""multiply"", ""operands"": [ { ""field"": ""Order.Total"" }, 0.1 ] } } ] },
        { ""id"": ""age-check"",
          ""condition"": { ""field"": ""Order.Lines"", ""operator"": ""Any"",
            ""condition"": { ""field"": ""Category"", ""operator"": ""Equals"", ""value"": ""alcohol"" } },
          ""actions"": [ { ""type"": ""setOutput"", ""target"": ""IdRequired"", ""value"": true } ] }
      ]
    }";

    public static IEnumerable<object[]> Converters()
    {
        yield return new object[] { "System.Text.Json", (Func<string, IEqualityComparer<string>?, Dictionary<string, object?>>)((json, comparer) =>
        {
            System.Text.Json.JsonElement root = System.Text.Json.JsonDocument.Parse(json).RootElement;
            return comparer is null ? SystemTextJsonFacts.ToDictionary(root) : SystemTextJsonFacts.ToDictionary(root, comparer);
        }) };
        yield return new object[] { "Newtonsoft.Json", (Func<string, IEqualityComparer<string>?, Dictionary<string, object?>>)((json, comparer) =>
        {
            JToken root = JToken.Parse(json);
            return comparer is null ? NewtonsoftJsonFacts.ToDictionary(root) : NewtonsoftJsonFacts.ToDictionary(root, comparer);
        }) };
    }

    [Theory]
    [MemberData(nameof(Converters))]
    public void DictionaryFact_KeysAreCaseSensitiveByDefault(string adapter, Func<string, IEqualityComparer<string>?, Dictionary<string, object?>> convert)
    {
        _ = adapter;
        RuleWrightEngine engine = SystemTextEngine();

        RuleEvaluationResult result = engine.Evaluate(engine.LoadRuleSet(CaseRulesJson), convert(CamelCasePayload, null));

        Assert.Empty(result.FiredRules);
    }

    [Theory]
    [MemberData(nameof(Converters))]
    public void DictionaryFact_CaseInsensitiveComparerMatchesAtEveryLevel(string adapter, Func<string, IEqualityComparer<string>?, Dictionary<string, object?>> convert)
    {
        _ = adapter;
        RuleWrightEngine engine = SystemTextEngine();

        Dictionary<string, object?> fact = convert(CamelCasePayload, StringComparer.OrdinalIgnoreCase);
        RuleEvaluationResult result = engine.Evaluate(engine.LoadRuleSet(CaseRulesJson), fact);

        Assert.Equal(new[] { "vip", "age-check" }, result.FiredRules.Select(f => f.RuleId));
        Assert.Equal(15.0m, result.Outputs["Discount"]);
        Assert.Equal(true, result.Outputs["IdRequired"]);
        Assert.Same(StringComparer.OrdinalIgnoreCase, fact.Comparer);
        Assert.Same(StringComparer.OrdinalIgnoreCase, ((Dictionary<string, object?>)fact["Customer"]!).Comparer);
        object?[] lines = (object?[])((Dictionary<string, object?>)fact["ORDER"]!)["Lines"]!;
        Assert.Same(StringComparer.OrdinalIgnoreCase, ((Dictionary<string, object?>)lines[0]!).Comparer);
    }

    [Theory]
    [MemberData(nameof(Converters))]
    public void DictionaryFact_KeysThatCollideUnderTheComparerAreRejected(string adapter, Func<string, IEqualityComparer<string>?, Dictionary<string, object?>> convert)
    {
        _ = adapter;
        const string clashing = @"{ ""Order"": { ""Total"": 10, ""total"": 20 } }";

        ArgumentException ex = Assert.Throws<ArgumentException>(() => convert(clashing, StringComparer.OrdinalIgnoreCase));

        Assert.Contains("'Total' and 'total'", ex.Message);
        Assert.Equal(2, ((Dictionary<string, object?>)convert(clashing, null)["Order"]!).Count);
    }

    [Fact]
    public void DictionaryFact_NullComparerIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => SystemTextJsonFacts.ToDictionary(System.Text.Json.JsonDocument.Parse("{}").RootElement, null!));
        Assert.Throws<ArgumentNullException>(() => NewtonsoftJsonFacts.ToDictionary(JToken.Parse("{}"), null!));
    }
}
