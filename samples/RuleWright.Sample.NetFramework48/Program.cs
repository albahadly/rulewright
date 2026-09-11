using RuleWright.Core;
using RuleWright.Execution;
using RuleWright.Json.SystemText;

namespace RuleWright.Sample.NetFramework48;

/// <summary>
/// Smoke test proving the netstandard2.0 RuleWright packages work end-to-end on
/// .NET Framework 4.8: JSON load, schema validation, expression-tree compilation,
/// compiled evaluation, interpreter fallback, and tracing. Exits non-zero on any
/// unexpected result so CI can run it as a hard check.
/// </summary>
public static class Program
{
    private const string RuleJson = @"{
      ""id"": ""discount-rule-01"",
      ""priority"": 10,
      ""condition"": {
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
      },
      ""actions"": [ { ""type"": ""setOutput"", ""target"": ""Discount"", ""value"": 10 } ]
    }";

    public static int Main()
    {
        Console.WriteLine($".NET Framework runtime: {Environment.Version}");

        RuleWrightEngine engine = new RuleWrightBuilder()
            .UseJsonReader(new SystemTextJsonReader())
            .Build();

        LoadedRuleSet ruleSet = engine.LoadRuleSet(RuleJson);

        var fact = new OrderFact
        {
            Customer = new Customer { Age = 40, IsVip = true },
            Order = new Order { Total = 20m },
        };

        RuleEvaluationResult compiled = engine.Evaluate(ruleSet, fact, new EvaluationOptions { EnableTrace = true });
        Check(compiled.CompilationMode == CompilationMode.Compiled, "typed facts use the compiled path");
        Check(compiled.FiredRules.Count == 1, "the discount rule fires for a VIP adult");
        Check(Equals(compiled.Outputs["Discount"], 10L), "the discount output is 10");
        Check(compiled.Trace != null && compiled.Trace.Rules[0].Fired, "tracing works");

        var dictionaryFact = new Dictionary<string, object>
        {
            ["Customer"] = new Dictionary<string, object> { ["Age"] = 30L, ["IsVip"] = false },
            ["Order"] = new Dictionary<string, object> { ["Total"] = 250L },
        };
        RuleEvaluationResult interpreted = engine.Evaluate(ruleSet, dictionaryFact);
        Check(interpreted.CompilationMode == CompilationMode.Interpreted, "dictionary facts use the interpreter");
        Check(interpreted.FiredRules.Count == 1, "the discount rule fires for a high-value order");

        Console.WriteLine(_failed == 0 ? "PASS: RuleWright works on .NET Framework 4.8." : "FAIL");
        return _failed;
    }

    private static int _failed;

    private static void Check(bool condition, string description)
    {
        Console.WriteLine($"  [{(condition ? "ok" : "FAIL")}] {description}");
        if (!condition)
        {
            _failed = 1;
        }
    }
}

public sealed class OrderFact
{
    public Customer Customer { get; set; } = new Customer();

    public Order Order { get; set; } = new Order();
}

public sealed class Customer
{
    public int Age { get; set; }

    public bool IsVip { get; set; }
}

public sealed class Order
{
    public decimal Total { get; set; }
}
