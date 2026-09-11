// RuleWright.Json.SystemText and RuleWright.Json.NewtonsoftJson are two interchangeable
// IRuleJsonReader adapters — pick whichever JSON library your application already uses. This
// sample is the console quickstart with SystemTextJsonReader swapped for NewtonsoftJsonReader;
// nothing else about loading, compiling, or evaluating rules changes.

using Newtonsoft.Json.Linq;
using RuleWright.Core;
using RuleWright.Execution;
using RuleWright.Json.NewtonsoftJson;

RuleWrightEngine engine = new RuleWrightBuilder()
    .UseJsonReader(new NewtonsoftJsonReader())
    .Build();

string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "rules", "discount-rule.json"));
LoadedRuleSet ruleSet = engine.LoadRuleSet(json);

// --- 1. Strongly-typed fact: compiled expression-tree path (unaffected by which JSON
//        library loaded the rule set — only the rule *document* went through Newtonsoft) ------
var order = new CustomerOrder
{
    Customer = new Customer { Age = 32, IsVip = true },
    Order = new Order { Total = 42.50m },
};

RuleEvaluationResult typedResult = engine.Evaluate(ruleSet, order);
Console.WriteLine($"Typed fact      -> mode: {typedResult.CompilationMode}, "
    + $"Discount = {(typedResult.Outputs.TryGetValue("Discount", out object? discount1) ? discount1 : "n/a")}");

// --- 2. A fact that arrived as a Newtonsoft JToken (e.g. from a webhook payload parsed with
//        JObject.Parse) — NewtonsoftJsonFacts.ToDictionary feeds it to the interpreter path. --
JObject payload = JObject.Parse("""
    {
      "Customer": { "Age": 45, "IsVip": false },
      "Order": { "Total": 150 }
    }
    """);
Dictionary<string, object?> dictionaryFact = NewtonsoftJsonFacts.ToDictionary(payload);

RuleEvaluationResult dictionaryResult = engine.Evaluate(ruleSet, dictionaryFact);
Console.WriteLine($"Dictionary fact -> mode: {dictionaryResult.CompilationMode}, "
    + $"Discount = {(dictionaryResult.Outputs.TryGetValue("Discount", out object? discount2) ? discount2 : "n/a")}");

return typedResult.FiredRules.Count == 1 && dictionaryResult.FiredRules.Count == 1 ? 0 : 1;

public sealed class CustomerOrder
{
    public Customer Customer { get; set; } = new();

    public Order Order { get; set; } = new();
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
