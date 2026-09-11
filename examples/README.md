# RuleWright examples

A tour of the JSON rule format, from a single rule to decision tables. Every file here is a
valid RuleWright document — a single rule, a rule set (`rules`), or a decision table
(`decisionTable`) — and is validated against the engine in CI.

## Running an example

```csharp
using RuleWright.Execution;
using RuleWright.Extensions.Functions;
using RuleWright.Json.SystemText;

var engine = new RuleWrightBuilder()
    .UseJsonReader(new SystemTextJsonReader())
    // Only 12-custom-function.json and 18-builtin-functions.json need this:
    .RegisterBuiltInFunctions()
    .Build();

string json = File.ReadAllText("examples/07-computed-values.json");
LoadedRuleSet ruleSet = engine.LoadRuleSet(json);          // parse + validate + prepare, once

var fact = new
{
    Customer = new { Name = "Alice", IsVip = true },
    Order = new { Total = 120.50m, ItemCount = 3 }
};

RuleEvaluationResult result = engine.Evaluate(ruleSet, fact, new EvaluationOptions { EnableTrace = true });

foreach (var output in result.Outputs)
    Console.WriteLine($"{output.Key} = {output.Value}");
```

Facts can be **typed POCOs** (compiled path, fields checked at compile time) or an
**`IDictionary<string, object>`** (interpreted path — handy for JSON payloads via
`SystemTextJsonFacts.ToDictionary`). The examples below assume a fact shaped roughly like:

```
Customer { Name, Age, Tier ("bronze"|"silver"|"gold"|"vip"), IsVip, LoyaltyYears,
           Country, Email, PostCode }
Order    { Total, ItemCount, Weight, Coupon, Category, ShippingCost, PlacedOn,
           DiscountApplied }
```

Any field a rule references but the fact omits simply resolves to `null` (dictionary facts)
or must exist on the type (typed facts) — see `05-null-and-coalesce.json`.

## The examples

| File | Shows |
|---|---|
| [01-quickstart-single-rule.json](01-quickstart-single-rule.json) | The smallest document: one rule, nested `AND`/`OR`, two `setOutput` actions. |
| [02-rule-set-priority.json](02-rule-set-priority.json) | A `rules` set; `priority` controls evaluation order and which output wins the merge. |
| [03-string-operators.json](03-string-operators.json) | `Contains`, `StartsWith`, `EndsWith`, `MatchesRegex` (all ordinal). |
| [04-collection-operators.json](04-collection-operators.json) | `In` / `NotIn` against a closed set (array values). |
| [05-null-and-coalesce.json](05-null-and-coalesce.json) | `IsNull` / `IsNotNull`, and `coalesce` to supply defaults for null fields. |
| [06-nested-logic.json](06-nested-logic.json) | `AND` / `OR` / `NOT` nested to arbitrary depth. |
| [07-computed-values.json](07-computed-values.json) | Output values computed from fact fields (`multiply`, `subtract`, `concat`). |
| [08-arithmetic-operators.json](08-arithmetic-operators.json) | Every arithmetic operator: `add`, `subtract`, `multiply`, `divide`, `modulo`, `negate`. |
| [09-accumulators-risk-scoring.json](09-accumulators-risk-scoring.json) | `addToOutput` (running total) and `appendToOutput` (collected list) across rules. |
| [10-decision-table-first.json](10-decision-table-first.json) | A decision table with `hitPolicy: "first"` — only the first matching row applies. |
| [11-decision-table-collect-scoring.json](11-decision-table-collect-scoring.json) | A decision table with `hitPolicy: "collect"` feeding accumulator outputs. |
| [12-custom-function.json](12-custom-function.json) | The `custom` operator, delegating a leaf to a function registered on the builder. |
| [13-if-else-append.json](13-if-else-append.json) | The pre-`else` way to write if / else: a rule and its negation (`NOT`) append different values (compare 16). |
| [14-if-else-decision-table.json](14-if-else-decision-table.json) | The same if / else as 13, expressed DRY as a `first` decision table (the condition is written once). |
| [15-condition-side-expression.json](15-condition-side-expression.json) | A condition whose left-hand side is a computed `expression` (e.g. `Order.Total / Order.ItemCount > 25`). |
| [16-else-and-remove.json](16-else-and-remove.json) | First-class `else` actions (one rule, both branches) and `removeOutput` to retract an earlier rule's output. |
| [17-disabled-rule.json](17-disabled-rule.json) | `enabled: false` retires a rule without deleting it — it is skipped entirely. |
| [18-builtin-functions.json](18-builtin-functions.json) | `RuleWright.Extensions.Functions`' curated built-in `custom` predicates: `IsEmail`, `EqualsIgnoreCase`, `DivisibleBy`, `IsPositive`. |
| [19-decision-table-computed-cell.json](19-decision-table-computed-cell.json) | A decision table `then` cell is an expression, not just a constant — a pricing table computes its output from the fact. |
| [20-collection-operators.json](20-collection-operators.json) | Collection quantifiers: `Any` / `All` / `None` over a collection field, `"$"` for scalar elements, and `count`. |
| [21-stop-after-first-match.json](21-stop-after-first-match.json) | `stopAfterFirstMatch` — the *set’s own* semantics: evaluation stops at the first rule that passes, with no option from the caller (compare 02). |

## Key ideas the examples lean on

- **One value key.** An action's `value` is a bare scalar (a constant) or an expression
  object — `{ "field": "…" }`, `{ "op": "…", "operands": [ … ] }`, or `{ "literal": … }`.
  A constant is just the simplest expression.
- **Action types.** `setOutput` replaces, `addToOutput` sums, `appendToOutput` collects into
  a list, `removeOutput` deletes. The accumulators combine across every rule that fires, in
  priority order; `removeOutput` can undo an earlier rule's write.
- **Else actions.** A rule may carry an `else` array that runs when the condition is false —
  an if/else in one rule. Each fired rule reports which `Branch` (`Then`/`Else`) ran.
- **Total evaluation.** Operators never throw on data: nulls propagate (except `coalesce`),
  non-numeric arithmetic yields null, and divide/modulo by zero yields null.
- **Pure data.** There is no embedded code — a closed, validatable vocabulary that a UI can
  generate and a reviewer can diff. Validation errors carry JSON-pointer paths.

The authoritative contract is [`docs/schema/rule-schema.json`](../docs/schema/rule-schema.json);
the design notes are in [`docs/architecture.md`](../docs/architecture.md).
