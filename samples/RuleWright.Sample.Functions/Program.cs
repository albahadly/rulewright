// The 'custom' operator delegates a condition leaf to an IRuleFunction. There are three ways to
// get functions onto the builder, and this sample uses all three together: the curated built-in
// catalog (RegisterBuiltInFunctions), an inline delegate (RegisterFunction), and an assembly scan
// that discovers ordinary IRuleFunction classes (RegisterFunctionsFrom) so a project can keep its
// domain-specific predicates as regular, testable types instead of wiring each one up by hand.

using RuleWright.Core;
using RuleWright.Execution;
using RuleWright.Extensions.Functions;
using RuleWright.Json.SystemText;

RuleWrightEngine engine = new RuleWrightBuilder()
    .UseJsonReader(new SystemTextJsonReader())
    .RegisterBuiltInFunctions()
    .RegisterFunction("IsHighValue", (fieldValue, value) => fieldValue switch
    {
        decimal d => d >= 500m,
        long l => l >= 500,
        _ => false,
    })
    .RegisterFunctionsFrom(typeof(Program).Assembly)
    .Build();

Console.WriteLine("Registered functions: " + string.Join(", ", engine.RegisteredFunctions));
Console.WriteLine();

const string ruleSetJson = """
    {
      "name": "Function showcase",
      "rules": [
        {
          "id": "weekend-order",
          "description": "Built-in: RegisterBuiltInFunctions.",
          "condition": { "field": "Order.PlacedOn", "operator": "custom", "name": "IsWeekend" },
          "actions": [ { "type": "appendToOutput", "target": "Flags", "value": "weekend-order" } ]
        },
        {
          "id": "high-value-order",
          "description": "Inline delegate: RegisterFunction.",
          "condition": { "field": "Order.Total", "operator": "custom", "name": "IsHighValue" },
          "actions": [ { "type": "appendToOutput", "target": "Flags", "value": "high-value-order" } ]
        },
        {
          "id": "acme-employee",
          "description": "Discovered class: RegisterFunctionsFrom(assembly).",
          "condition": { "field": "Customer.Email", "operator": "custom", "name": "IsAcmeDomainEmail" },
          "actions": [ { "type": "appendToOutput", "target": "Flags", "value": "acme-employee" } ]
        }
      ]
    }
    """;

LoadedRuleSet ruleSet = engine.LoadRuleSet(ruleSetJson);

var fact = new Dictionary<string, object?>
{
    ["Customer"] = new Dictionary<string, object?> { ["Email"] = "alice@acme.com" },
    ["Order"] = new Dictionary<string, object?>
    {
        // A Saturday.
        ["PlacedOn"] = new DateTime(2026, 7, 18),
        ["Total"] = 750m,
    },
};

RuleEvaluationResult result = engine.Evaluate(ruleSet, fact);
Console.WriteLine("Fired: " + string.Join(", ", result.FiredRules.Select(fired => fired.RuleId)));
Console.WriteLine("Flags: " + string.Join(", ", (IEnumerable<object?>)result.Outputs["Flags"]!));

return result.FiredRules.Count == 3 ? 0 : 1;

/// <summary>
/// A domain-specific predicate kept as an ordinary class and picked up by
/// <c>RegisterFunctionsFrom</c> instead of being registered by hand — the point being that a
/// real project's own <c>IRuleFunction</c> types live next to its other code, not inline here.
/// </summary>
public sealed class IsAcmeDomainEmail : IRuleFunction
{
    public string Name => "IsAcmeDomainEmail";

    public bool Evaluate(object? fieldValue, object? value)
        => fieldValue is string email && email.EndsWith("@acme.com", StringComparison.OrdinalIgnoreCase);
}
