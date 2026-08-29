# Rulewright

A high-performance, JSON-driven business rule engine for .NET. Rules are plain JSON
documents; evaluation is compiled expression trees — parse once, compile once, execute
millions of times.

This is the main package: install it and you have the whole engine.

## Install

```
dotnet add package Rulewright
```

That covers everything on this page — the domain model, the JSON parser and schema
validator, the evaluation engine, the System.Text.Json adapter, and the built-in `custom`
functions all come with it.

## A rule

```json
{
  "id": "discount-rule-01",
  "description": "VIP or high-value customers get 10% off",
  "priority": 10,
  "condition": {
    "type": "group",
    "operator": "AND",
    "rules": [
      { "field": "Customer.Age", "operator": "GreaterThan", "value": 18 },
      {
        "type": "group",
        "operator": "OR",
        "rules": [
          { "field": "Order.Total", "operator": "GreaterThanOrEqual", "value": 100 },
          { "field": "Customer.IsVip", "operator": "Equals", "value": true }
        ]
      }
    ]
  },
  "actions": [
    { "type": "setOutput", "target": "Discount", "value": 10 }
  ]
}
```

## Evaluate it

```csharp
using Rulewright.Core;
using Rulewright.Execution;
using Rulewright.Json.SystemText;

var engine = new RulewrightBuilder()
    .UseJsonReader(new SystemTextJsonReader())
    .Build();

LoadedRuleSet ruleSet = engine.LoadRuleSet(json);   // parse + validate once

RuleEvaluationResult result = engine.Evaluate(ruleSet, customerOrder, new EvaluationOptions
{
    EnableTrace = true,
});

foreach (FiredRule fired in result.FiredRules)
    Console.WriteLine($"{fired.RuleId} -> {string.Join(", ", fired.Outputs)}");
```

Facts can be typed POCOs (compiled path — expression-tree delegates, no reflection at
evaluation time) or `IDictionary<string, object>` (interpreted path). The result reports
which path ran via `CompilationMode`, so a fallback is never silent.

## What you get

- **Compiled, not interpreted.** Rules compile to delegates and are cached by a **content
  hash** of the rule, not its id — editing a rule's body invalidates the cache, reformatting
  it does not.
- **A closed, validatable operator vocabulary.** No embedded code or expression strings in
  rule files: comparison, string (ordinal), collection, and null operators, `AND`/`OR`/`NOT`
  groups, plus an extensible `custom` operator bound to functions you register.
- **Computed values and accumulators.** Action values can be arithmetic/`concat`/`coalesce`
  expressions over fact fields; `addToOutput` and `appendToOutput` accumulate across fired
  rules, `removeOutput` retracts, and `else` branches run when a condition fails.
- **Decision tables.** `decisionTable` documents expand into ordinary rules with
  `hitPolicy: "first"` or `"collect"`.
- **Opt-in execution traces** recording which rules fired and which condition nodes passed,
  failed, or were short-circuited — with **zero overhead when disabled** (a separate compiled
  fast path, not a runtime flag check).
- **Structured validation.** `RuleSetValidator` returns JSON-pointer-addressed errors, and
  `RuleSchemaCatalog` enumerates the whole authoring vocabulary at runtime for a builder UI.

## Picking pieces instead

| Package | When |
|---|---|
| [`Rulewright.Execution`](https://www.nuget.org/packages/Rulewright.Execution) | The engine, without a JSON adapter or the built-in functions. |
| [`Rulewright.Json.NewtonsoftJson`](https://www.nuget.org/packages/Rulewright.Json.NewtonsoftJson) | Use Newtonsoft.Json instead of System.Text.Json. |
| [`Rulewright.Serialization`](https://www.nuget.org/packages/Rulewright.Serialization) | Validate or hash rule documents without evaluating them. |
| [`Rulewright.Core`](https://www.nuget.org/packages/Rulewright.Core) | Reference the domain model alone. Zero dependencies. |

## Requirements

.NET Framework 4.8, .NET Standard 2.0, .NET 8.0 or .NET 10.0.

NativeAOT: the compiled path uses `Expression.Compile`, which NativeAOT does not support.
Use dictionary facts there — the engine reports `CompilationMode.Interpreted`.

## License

MIT.
