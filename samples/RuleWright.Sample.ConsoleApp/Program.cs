using RuleWright.Core;
using RuleWright.Execution;
using RuleWright.Json.SystemText;

var engine = new RuleWrightBuilder()
    .UseJsonReader(new SystemTextJsonReader())
    .Build();

string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "rules", "discount-rule.json"));
LoadedRuleSet ruleSet = engine.LoadRuleSet(json);

// --- 1. Strongly-typed fact: compiled expression-tree path -----------------
var order = new CustomerOrder
{
    Customer = new Customer { Age = 32, IsVip = true, Name = "Alice" },
    Order = new Order { Total = 42.50m },
};

RuleEvaluationResult result = engine.Evaluate(ruleSet, order, new EvaluationOptions { EnableTrace = true });

Console.WriteLine($"Typed fact  -> mode: {result.CompilationMode}");
foreach (FiredRule fired in result.FiredRules)
{
    Console.WriteLine($"  fired: {fired.RuleId}");
    foreach (KeyValuePair<string, object?> output in fired.Outputs)
    {
        Console.WriteLine($"    {output.Key} = {output.Value}");
    }
}

Console.WriteLine("  trace:");
PrintTrace(result.Trace!.Rules[0].Condition!, indent: 4);

// --- 2. Dictionary fact: interpreter fallback ------------------------------
var dictionaryFact = new Dictionary<string, object?>
{
    ["Customer"] = new Dictionary<string, object?> { ["Age"] = 45L, ["IsVip"] = false },
    ["Order"] = new Dictionary<string, object?> { ["Total"] = 150L },
};

RuleEvaluationResult dynamicResult = engine.Evaluate(ruleSet, dictionaryFact);
Console.WriteLine();
Console.WriteLine($"Dictionary fact -> mode: {dynamicResult.CompilationMode}, fired: {dynamicResult.FiredRules.Count}, "
    + $"Discount = {(dynamicResult.Outputs.TryGetValue("Discount", out object? discount) ? discount : "n/a")}");

return result.FiredRules.Count == 1 && dynamicResult.FiredRules.Count == 1 ? 0 : 1;

static void PrintTrace(ConditionTraceNode node, int indent)
{
    string outcome = node.Passed switch
    {
        true => "[pass]",
        false => "[fail]",
        null => "[skip]",
    };
    Console.WriteLine($"{new string(' ', indent)}{outcome} {node.Description}");
    foreach (ConditionTraceNode child in node.Children)
    {
        PrintTrace(child, indent + 2);
    }
}

public sealed class CustomerOrder
{
    public Customer Customer { get; set; } = new();

    public Order Order { get; set; } = new();
}

public sealed class Customer
{
    public int Age { get; set; }

    public bool IsVip { get; set; }

    public string Name { get; set; } = string.Empty;
}

public sealed class Order
{
    public decimal Total { get; set; }
}
