// A decisionTable document expands into an ordinary rule set at parse time (one rule per row) —
// no separate engine concept. This sample loads two tables that use the two hit policies:
// 'first' (shipping-cost-first.json), where row order is precedence and only one row applies,
// and 'collect' (loyalty-points-collect.json, the default), where every matching row's actions
// apply, which is what makes it pair naturally with addToOutput/appendToOutput accumulators.

using RuleWright.Core;
using RuleWright.Execution;
using RuleWright.Json.SystemText;

RuleWrightEngine engine = new RuleWrightBuilder()
    .UseJsonReader(new SystemTextJsonReader())
    .Build();

string rulesDirectory = Path.Combine(AppContext.BaseDirectory, "rules");

LoadedRuleSet shipping = engine.LoadRuleSet(File.ReadAllText(Path.Combine(rulesDirectory, "shipping-cost-first.json")));
LoadedRuleSet points = engine.LoadRuleSet(File.ReadAllText(Path.Combine(rulesDirectory, "loyalty-points-collect.json")));

Console.WriteLine($"'shipping' expanded to {shipping.RuleSet.Rules.Count} rules (one per row, "
    + "plus any synthesized catch-all).");
Console.WriteLine($"'points' expanded to {points.RuleSet.Rules.Count} rules.");
Console.WriteLine();

Console.WriteLine("-- hitPolicy: first --");
Evaluate(engine, shipping, "vip customer, $40 order", new()
{
    ["Customer"] = new Dictionary<string, object?> { ["Tier"] = "vip" },
    ["Order"] = new Dictionary<string, object?> { ["Total"] = 40m },
});
Evaluate(engine, shipping, "gold customer, $120 order", new()
{
    ["Customer"] = new Dictionary<string, object?> { ["Tier"] = "gold" },
    ["Order"] = new Dictionary<string, object?> { ["Total"] = 120m },
});
Evaluate(engine, shipping, "bronze customer, $10 order", new()
{
    ["Customer"] = new Dictionary<string, object?> { ["Tier"] = "bronze" },
    ["Order"] = new Dictionary<string, object?> { ["Total"] = 10m },
});

Console.WriteLine();
Console.WriteLine("-- hitPolicy: collect --");
Evaluate(engine, points, "gold customer, $150 order (matches all three rows)", new()
{
    ["Customer"] = new Dictionary<string, object?> { ["Tier"] = "gold" },
    ["Order"] = new Dictionary<string, object?> { ["Total"] = 150m },
});
Evaluate(engine, points, "bronze customer, $60 order (matches one row)", new()
{
    ["Customer"] = new Dictionary<string, object?> { ["Tier"] = "bronze" },
    ["Order"] = new Dictionary<string, object?> { ["Total"] = 60m },
});

static void Evaluate(RuleWrightEngine engine, LoadedRuleSet ruleSet, string label, Dictionary<string, object?> fact)
{
    RuleEvaluationResult result = engine.Evaluate(ruleSet, fact);
    string outputs = string.Join(", ", result.Outputs.Select(kv => $"{kv.Key}={Format(kv.Value)}"));
    Console.WriteLine($"  {label}: {outputs}");
}

static string Format(object? value) => value switch
{
    IEnumerable<object?> list => "[" + string.Join(", ", list) + "]",
    _ => value?.ToString() ?? "null",
};
