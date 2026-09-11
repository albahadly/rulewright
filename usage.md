# Using Rulewright

A task-oriented tour: every snippet below is a complete, runnable example. If you want the
conceptual reference instead — the full operator table, the schema contract, the architecture —
see [README.md](README.md) and [docs/architecture.md](docs/architecture.md).

- [Install](#install)
- [1. Your first rule](#1-your-first-rule)
- [2. Facts: typed or dynamic](#2-facts-typed-or-dynamic)
- [3. Conditions](#3-conditions)
- [4. Actions: writing outputs](#4-actions-writing-outputs)
- [5. Computed values](#5-computed-values)
- [5a. Collections: Any, All, None, count](#5a-collections-any-all-none-count)
- [6. Rule sets, priority, and else](#6-rule-sets-priority-and-else)
- [7. Decision tables](#7-decision-tables)
- [8. Custom functions](#8-custom-functions)
- [9. Validating before you run](#9-validating-before-you-run)
- [10. Tracing: why did this fire?](#10-tracing-why-did-this-fire)
- [11. Building rules in C# instead of JSON](#11-building-rules-in-c-instead-of-json)
- [12. Hosting: ASP.NET Core and long-lived engines](#12-hosting-aspnet-core-and-long-lived-engines)
- [13. Discovering the vocabulary for a UI](#13-discovering-the-vocabulary-for-a-ui)
- [14. Errors you may hit](#14-errors-you-may-hit)
- [15. Behaviour worth knowing](#15-behaviour-worth-knowing)

## Install

```
dotnet add package Rulewright
```

One metapackage: domain model, JSON parsing and validation, the engine, the System.Text.Json
adapter, and the built-in `custom` predicates. To pick pieces, see the package table in
[README.md](README.md#install).

```csharp
using Rulewright.Core;          // Rule, RuleSet, EvaluationOptions, RuleEvaluationResult
using Rulewright.Execution;     // RulewrightBuilder, RulewrightEngine, LoadedRuleSet
using Rulewright.Json.SystemText;
using Rulewright.Extensions.Functions;
```

## 1. Your first rule

A rule is JSON: a condition, and the actions to apply when it passes.

```json
{
  "id": "vip-discount",
  "description": "Adults spending over 100 get 10% off.",
  "condition": {
    "type": "group",
    "operator": "AND",
    "rules": [
      { "field": "Customer.Age",  "operator": "GreaterThanOrEqual", "value": 18 },
      { "field": "Order.Total",   "operator": "GreaterThan",        "value": 100 }
    ]
  },
  "actions": [
    { "type": "setOutput", "target": "DiscountPercent", "value": 10 }
  ]
}
```

Three steps to run it — build an engine, load the document, evaluate a fact:

```csharp
var engine = new RulewrightBuilder()
    .UseJsonReader(new SystemTextJsonReader())
    .Build();

LoadedRuleSet rules = engine.LoadRuleSet(json);

var fact = new Order { Customer = new Customer { Age = 30 }, Total = 150m };
RuleEvaluationResult result = engine.Evaluate(rules, fact);

Console.WriteLine(result.Outputs["DiscountPercent"]);   // 10
Console.WriteLine(result.FiredRules[0].RuleId);         // vip-discount
```

Each of the three has a distinct cost and lifetime, which matters for a server:

| Step | Cost | Do it |
|---|---|---|
| `Build()` | Cheap. Holds the function registry and the compiled-delegate cache. | **Once per process** |
| `LoadRuleSet` | Parses, validates, hashes. | **Once per rule document** |
| `Evaluate` | Runs compiled delegates. | Per request |

Compilation to delegates happens lazily on the first `Evaluate` for a given fact type, then is
cached on the engine — so keep the engine alive.

## 2. Facts: typed or dynamic

A **typed fact** (any POCO) takes the compiled path — field paths become null-guarded member
access, comparison constants are converted to the field's exact CLR type at compile time:

```csharp
public sealed class Order
{
    public Customer Customer { get; set; } = new();
    public decimal Total { get; set; }
    public string? Coupon { get; set; }
}

RuleEvaluationResult result = engine.Evaluate(rules, order);
Console.WriteLine(result.CompilationMode);   // Compiled
```

A **dictionary fact** takes the interpreter — use it when the shape is only known at runtime:

```csharp
var fact = new Dictionary<string, object?>
{
    ["Customer"] = new Dictionary<string, object?> { ["Age"] = 30L },
    ["Total"] = 150m,
};

RuleEvaluationResult result = engine.Evaluate(rules, fact);
Console.WriteLine(result.CompilationMode);   // Interpreted
```

Both paths give the same answers; the interpreter is just slower. `CompilationMode` always tells
you which ran, so the slower path is never silent.

Coming from JSON? Convert once and evaluate as a dictionary:

```csharp
using JsonDocument document = JsonDocument.Parse(requestBody);
Dictionary<string, object?> fact = SystemTextJsonFacts.ToDictionary(document.RootElement);
RuleEvaluationResult result = engine.Evaluate(rules, fact);
```

> **Type the variable, not just the object.** `Evaluate` compiles against the *static* type, so
> `object fact = new Order(...)` has no fields to bind and quietly falls back to the interpreter.
> Declare it as `Order` (or use `var`) to get the compiled path.

## 3. Conditions

A **leaf** compares one field. A **group** combines children with `AND`, `OR`, or `NOT`
(exactly one child), nested as deep as you like.

```json
{
  "type": "group", "operator": "OR",
  "rules": [
    { "field": "Customer.Tier", "operator": "In", "value": ["gold", "platinum"] },
    {
      "type": "group", "operator": "AND",
      "rules": [
        { "field": "Customer.LoyaltyYears", "operator": "GreaterThanOrEqual", "value": 5 },
        { "type": "group", "operator": "NOT",
          "rules": [ { "field": "Order.Category", "operator": "Equals", "value": "gift-card" } ] }
      ]
    }
  ]
}
```

Every operator, and the operand each one takes:

| Operator | `value` | Notes |
|---|---|---|
| `Equals`, `NotEquals` | scalar or `null` | Numbers compare across numeric types; `5` equals `5.0` |
| `GreaterThan`, `GreaterThanOrEqual`, `LessThan`, `LessThanOrEqual` | number or string | Strings compare **ordinally**, never by culture |
| `Contains`, `StartsWith`, `EndsWith` | string | Ordinal |
| `MatchesRegex` | string | Time-bounded — see [§15](#15-behaviour-worth-knowing) |
| `In`, `NotIn` | non-empty array of scalars | Membership of the field in a closed set |
| `Any`, `All`, `None` | *none* — takes `condition` | Quantify over a collection field; see [§5a](#5a-collections-any-all-none-count) |
| `IsNull`, `IsNotNull` | *omitted* | True if any segment of the path is null |
| `custom` + `name` | whatever the function expects | See [§8](#8-custom-functions) |

**Null semantics.** A null field — or a null anywhere along a dotted path — makes every operator
return `false`, with four exceptions: `IsNull` → `true`, `NotEquals` against a non-null value →
`true`, `NotIn` → `true`, and `Equals` against `null` → `true`. A missing dictionary key behaves
exactly like a null field, and nothing ever throws for a null.

**Field paths** are dotted (`Customer.Address.City`) and resolve case-insensitively against
properties and public fields on POCOs. Dictionary keys match exactly.

## 4. Actions: writing outputs

Actions write into `result.Outputs`, keyed by `target`. Four types:

```json
"actions": [
  { "type": "setOutput",    "target": "Tier",     "value": "gold" },
  { "type": "addToOutput",  "target": "Score",    "value": 25 },
  { "type": "appendToOutput", "target": "Reasons", "value": "loyal-customer" },
  { "type": "removeOutput", "target": "Provisional" }
]
```

| Type | Effect on `target` |
|---|---|
| `setOutput` | Replaces whatever is there |
| `addToOutput` | Adds numerically to a running total across all fired rules |
| `appendToOutput` | Appends to a `List<object?>` collected across all fired rules |
| `removeOutput` | Deletes the key (takes no `value`) |

`addToOutput` and `appendToOutput` are what make scoring rule sets work — several rules each
contribute, and the merged `Outputs` holds the total:

```csharp
Console.WriteLine(result.Outputs["Score"]);                    // 75 (25 + 30 + 20)
var reasons = (List<object?>)result.Outputs["Reasons"]!;       // ["loyal-customer", "large-order"]
```

Read a single rule's own contribution from `FiredRules`, and the merged view from `Outputs`:

```csharp
foreach (FiredRule fired in result.FiredRules)
{
    Console.WriteLine($"{fired.RuleId} ({fired.Branch}) wrote {string.Join(", ", fired.Outputs.Keys)}");
}
```

## 5. Computed values

An action's `value` is a **bare scalar** (a constant) or an **expression** computed from the fact.
Expressions are pure data — a closed operator vocabulary, never embedded code — so a UI can
generate them and a reviewer can diff them:

```json
"actions": [
  { "type": "setOutput", "target": "Discount",
    "value": { "op": "multiply", "operands": [ { "field": "Order.Total" }, 0.1 ] } },

  { "type": "setOutput", "target": "Message",
    "value": { "op": "concat", "operands": [ "Thanks, ", { "field": "Customer.Name" }, "!" ] } },

  { "type": "setOutput", "target": "Country",
    "value": { "op": "coalesce", "operands": [ { "field": "Customer.Country" }, "US" ] } }
]
```

| Operator | Operands | Result |
|---|---|---|
| `add`, `multiply` | 2 or more | Numeric |
| `count` | exactly 1 | Element count of a collection; null for a non-collection |
| `subtract`, `divide`, `modulo` | exactly 2 | Numeric; divide/modulo by zero yields `null` |
| `negate` | exactly 1 | Numeric |
| `concat` | 2 or more | String; **`null` if any operand is null** |
| `coalesce` | 2 or more | The first non-null operand |

An operand is a bare scalar, `{ "field": "<dotted path>" }`, `{ "literal": <scalar> }`, or another
`{ "op": ... }` node — nest them freely. Evaluation is **total**: a non-numeric operand to an
arithmetic operator yields `null` rather than throwing.

The same expressions work on the **left-hand side of a condition**:

```json
{
  "expression": { "op": "divide", "operands": [ { "field": "Order.Total" }, { "field": "Order.ItemCount" } ] },
  "operator": "GreaterThan",
  "value": 25
}
```

That reads *average item price > 25*. A leaf uses `field` **or** `expression`, never both.

## 5a. Collections: Any, All, None, count

`In`/`NotIn` compare a single scalar against a set. To reason about a *collection* field — order
lines, tags, payments — use a quantifier. `field` names the collection and `condition` is applied
to each element:

```json
{
  "field": "Order.Lines",
  "operator": "Any",
  "condition": {
    "type": "group",
    "operator": "AND",
    "rules": [
      { "field": "Category", "operator": "In", "value": ["alcohol", "tobacco"] },
      { "field": "Quantity", "operator": "GreaterThan", "value": 1 }
    ]
  }
}
```

That reads *any order line is a restricted category with quantity above one*.

| Operator | True when | Empty collection | Null field |
|---|---|---|---|
| `Any` | at least one element matches | `false` | `false` |
| `All` | every element matches | `true` (vacuously) | `false` |
| `None` | no element matches | `true` (vacuously) | `true` |

A null collection is the field's *absence*, so it follows the same null semantics as everything
else — `None` is true for it exactly as `NotIn` is. An **empty** collection is a collection, so
`All` and `None` are vacuously true over it. That distinction is deliberate: "there is no basket"
and "the basket is empty" are different facts.

The `condition` is an ordinary condition tree — groups, nested quantifiers, computed expressions,
`custom` functions all work inside it. Its field paths resolve against the **element**, not the
root fact.

### `"$"` — the element itself

For a collection of scalars there is no member to name, so `"$"` means the element being tested:

```json
{ "field": "Order.Tags", "operator": "Any",
  "condition": { "field": "$", "operator": "Equals", "value": "priority" } }
```

`$` is only valid inside a quantifier's `condition`, and the whole `$` prefix is reserved, so
`$root.Total` is rejected rather than read as a member called `$root`. That leaves room to add
correlated conditions later without breaking documents.

> **Not in this version:** the element condition cannot reach back to the root fact, so "any line
> whose price exceeds the order's average" is not yet expressible.

### Counting

`count` measures a collection, so a size test goes through the ordinary computed left-hand side:

```json
{
  "expression": { "op": "count", "operands": [ { "field": "Order.Lines" } ] },
  "operator": "GreaterThan",
  "value": 3
}
```

It works in an action's value too (`"target": "LineCount"`). Like every expression operator it is
total: a null, a non-collection, or a string yields `null` rather than an error — a string is
text, not a collection of characters, for both `count` and the quantifiers.

Counting only the *matching* elements ("more than three alcohol lines") is a follow-up; today you
would add a `custom` function for it.

### C# equivalent

Quantifiers are a factory rather than a constructor, so existing `ConditionLeaf` calls are
untouched:

```csharp
ConditionLeaf leaf = ConditionLeaf.Quantifier(
    "Order.Lines",
    ConditionOperator.Any,
    new ConditionLeaf("Category", ConditionOperator.Equal, "alcohol"));
```

## 6. Rule sets, priority, and else

Several rules in one document:

```json
{
  "name": "Checkout policy",
  "rules": [
    { "id": "free-shipping", "priority": 10,
      "condition": { "field": "Order.Total", "operator": "GreaterThanOrEqual", "value": 50 },
      "actions": [ { "type": "setOutput", "target": "Shipping", "value": 0 } ],
      "else":    [ { "type": "setOutput", "target": "Shipping", "value": 4.95 } ] },

    { "id": "retired", "enabled": false,
      "condition": { "field": "Order.Coupon", "operator": "IsNotNull" },
      "actions": [ { "type": "setOutput", "target": "Legacy", "value": true } ] }
  ]
}
```

- **`priority`** — higher runs first; ties keep document order. When two rules write the same
  target, the one that runs *last* wins the merge.
- **`else`** — actions applied when the condition does **not** pass. One rule, both branches;
  `FiredRule.Branch` says which ran.
- **`enabled: false`** — retires a rule without deleting it. It is skipped entirely.
- **`description`** and **`layout`** — free-text and UI metadata; the engine ignores both.

Stop after the first match — the caller's choice, for one evaluation:

```csharp
var result = engine.Evaluate(rules, fact, new EvaluationOptions { StopOnFirstMatch = true });
```

`EvaluationOptions` is immutable — set it with an object initializer. (Don't try to mutate
`EvaluationOptions.Default`; it is shared and read-only by design.)

- **`stopAfterFirstMatch`** — the same thing as the *set's own* semantics, stated in the document:

  ```json
  { "name": "Shipping", "stopAfterFirstMatch": true, "rules": [ ... ] }
  ```

  Evaluation stops after the first rule whose condition passes, whatever the caller asks for. This
  is what a `first`-hit-policy decision table (§7) expands into, so a tool that rewrites a table as
  its equivalent rules can say so rather than silently producing a collecting set. The two combine
  with OR: a caller can stop a collecting set early, but cannot turn this one into a collecting
  one. Omitted means `false`, so documents written before this property keep their behaviour.

## 7. Decision tables

For logic that reads as a grid. Each input column maps a cell to a condition; each output column
maps a cell to an action:

```json
{
  "decisionTable": {
    "id": "shipping",
    "hitPolicy": "first",
    "inputs": [
      { "field": "Customer.Tier", "operator": "Equals" },
      { "field": "Order.Total",   "operator": "GreaterThanOrEqual" }
    ],
    "outputs": [ { "target": "Discount" }, { "target": "Label" } ],
    "rows": [
      { "when": ["VIP", 100],  "then": [20, "vip-big-order"] },
      { "when": ["VIP", null], "then": [10, "vip"] },
      { "when": [null, null],  "then": [0,  "standard"] }
    ]
  }
}
```

- A `null` **input** cell is a wildcard; an all-wildcard row is a catch-all.
- A `null` **output** cell means that row does not write that output.
- Input columns default to `Equals` and accept any comparison operator (`In` cells are arrays).
- Output columns default to `setOutput` and may use `addToOutput` / `appendToOutput`.
- A `then` cell can be a full value expression, not just a constant.

Two hit policies:

- **`collect`** (default) — every matching row applies, in row order. Pairs with `addToOutput`
  for scoring tables.
- **`first`** — only the first matching row applies.

A table **expands into ordinary rules at load time**, one per row, so tracing, hashing, and both
execution paths work exactly as they do for hand-written rules:

```csharp
LoadedRuleSet rules = engine.LoadRuleSet(tableJson);
foreach (Rule rule in rules.RuleSet.Rules)
{
    Console.WriteLine(rule.Id);          // shipping-0, shipping-1, shipping-2
}
Console.WriteLine(rules.RuleSet.StopAfterFirstMatch);   // True for hitPolicy "first"
```

## 8. Custom functions

When the closed operator set doesn't cover something, register a predicate and call it with the
`custom` operator. The delegate receives the resolved field value and the leaf's `value`:

```csharp
var engine = new RulewrightBuilder()
    .UseJsonReader(new SystemTextJsonReader())
    .RegisterFunction("IsBusinessDay", (field, value) =>
        field is DateTime d && d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday)
    .Build();
```

```json
{ "field": "Order.PlacedOn", "operator": "custom", "name": "IsBusinessDay" }
```

Omit `field` and the function receives the whole fact. Functions are bound at **compile time**, so
an unregistered name fails at `LoadRuleSet` — not mid-evaluation. They must be thread-safe.

`Rulewright.Extensions.Functions` ships a curated catalog:

```csharp
var engine = new RulewrightBuilder()
    .UseJsonReader(new SystemTextJsonReader())
    .RegisterBuiltInFunctions()                        // IsEmail, IsEven, EqualsIgnoreCase, …
    .RegisterFunctionsFrom(typeof(Program).Assembly)   // scan your own IRuleFunction types
    .Build();
```

`IsNullOrEmpty` · `IsNullOrWhiteSpace` · `EqualsIgnoreCase` · `IsEmail` · `IsEven` / `IsOdd` ·
`IsPositive` / `IsNegative` · `DivisibleBy` · `IsBetweenInclusive` · `IsWeekend` / `IsWeekday` ·
`IsInPast` / `IsInFuture`. Every one is **total** — an unexpected value type yields `false`, never
an exception.

A function's `value` is whatever it expects, including an array:

```json
{ "field": "Customer.Age", "operator": "custom", "name": "IsBetweenInclusive", "value": [18, 65] }
```

For a testable clock, build the catalog yourself:

```csharp
var functions = BuiltInFunctions.Create(() => new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
```

## 9. Validating before you run

`Validate` checks a document against the schema contract and returns **structured errors with
JSON pointer paths** — no exceptions, so you can bind it straight to an editor:

```csharp
RuleSetValidationResult validation = engine.Validate(json);
if (!validation.IsValid)
{
    foreach (RuleValidationError error in validation.Errors)
    {
        Console.WriteLine($"{error.Path}: {error.Message}");
        // /rules/0/condition/value: 'value' must be a non-empty array for operator 'In'.
        // /rules/1/actons: Unknown property 'actons'. Expected one of: id, description, …
    }
    return;
}

LoadedRuleSet rules = engine.LoadRuleSet(json);
```

Malformed JSON comes back as a single root-level error rather than throwing, so one code path
handles both. The vocabulary is closed, so a misspelled key is reported rather than ignored —
that's what catches `"actons"` before it becomes a rule that fires and writes nothing.

`docs/schema/rule-schema.json` is the same contract as a JSON Schema, for editor completion.

## 10. Tracing: why did this fire?

Tracing records which rules fired and which condition nodes passed, failed, or were never reached:

```csharp
var result = engine.Evaluate(rules, fact, new EvaluationOptions { EnableTrace = true });

foreach (RuleTrace rule in result.Trace!.Rules)
{
    if (rule.Skipped)
    {
        Console.WriteLine($"{rule.RuleId}: skipped ({rule.SkipReason})");
        continue;
    }

    Console.WriteLine($"{rule.RuleId}: {(rule.Fired ? "FIRED" : "no match")}");
    Print(rule.Condition!, indent: 1);
}

static void Print(ConditionTraceNode node, int indent)
{
    string mark = node.Passed switch { true => "PASS", false => "FAIL", null => "  - " };
    Console.WriteLine($"{new string(' ', indent * 2)}{mark} {node.Description}");
    foreach (ConditionTraceNode child in node.Children)   // empty for leaves
    {
        Print(child, indent + 1);
    }
}
```

```
vip-discount: FIRED
  PASS AND
    PASS Customer.Age GreaterThanOrEqual 18
    PASS Order.Total GreaterThan 100
retired: skipped (Disabled)
later-rule: skipped (StoppedAfterMatch)
```

`Passed == null` means short-circuited — the node was never evaluated. `SkipReason` separates a
rule the author turned off (`Disabled`) from one evaluation never reached (`StoppedAfterMatch`).
Tracing is off by default and the untraced path pays nothing for it.

## 11. Building rules in C# instead of JSON

The domain model is public and immutable, so you can skip JSON entirely — useful for tests,
codegen, or translating from your own storage format:

```csharp
var rule = new Rule(
    id: "vip",
    condition: new ConditionGroup(LogicalOperator.And, new ConditionNode[]
    {
        new ConditionLeaf("Customer.Age", ConditionOperator.GreaterThanOrEqual, 18L),
        new ConditionLeaf("Order.Total", ConditionOperator.GreaterThan, 100m),
    }),
    actions: new[]
    {
        new RuleAction(RuleAction.SetOutputType, "DiscountPercent", 10L),
        new RuleAction(RuleAction.AddToOutputType, "Score",
            new OperatorExpression(ExpressionOperator.Multiply, new ValueExpression[]
            {
                new FieldExpression("Order.Total"),
                new LiteralExpression(0.1m),
            })),
    },
    priority: 10);

LoadedRuleSet rules = engine.LoadRuleSet(new RuleSet(new[] { rule }, "Checkout policy"));
```

An engine built without `UseJsonReader` still handles this — the reader is only needed for the
`string` overloads.

Note the enum spellings: the domain uses `ConditionOperator.Equal` / `NotEqual` where the JSON
says `"Equals"` / `"NotEquals"`, to avoid colliding with `object.Equals`.

## 12. Hosting: ASP.NET Core and long-lived engines

The engine is thread-safe, and both it and a `LoadedRuleSet` are meant to outlive a request:

```csharp
builder.Services.AddSingleton(new RulewrightBuilder()
    .UseJsonReader(new SystemTextJsonReader())
    .RegisterBuiltInFunctions()
    .Build());

builder.Services.AddSingleton(provider =>
{
    RulewrightEngine engine = provider.GetRequiredService<RulewrightEngine>();
    return engine.LoadRuleSet(File.ReadAllText("rules/checkout.json"));
});

app.MapPost("/evaluate", (EvaluateRequest request, RulewrightEngine engine, LoadedRuleSet rules) =>
{
    Dictionary<string, object?> fact = SystemTextJsonFacts.ToDictionary(request.Fact);
    RuleEvaluationResult result = engine.Evaluate(rules, fact);
    return Results.Ok(new { result.Outputs, fired = result.FiredRules.Select(r => r.RuleId) });
});
```

**Reloading rules at runtime.** Call `LoadRuleSet` again and swap the reference — loading is cheap
and compiled delegates are cached by rule *content hash*, so rules that didn't change keep their
existing delegate. Rules that did change compile once on next use.

**One caveat for hot-reload at scale:** that cache is unbounded and lives on the engine. If you
load thousands of *distinct* rule documents over a long-lived process (a multi-tenant service
reloading tenant rules, say), memory grows with the number of distinct rules ever seen. Rebuild
the engine periodically if that's your shape.

**Newtonsoft.Json instead:**

```csharp
var engine = new RulewrightBuilder()
    .UseJsonReader(new NewtonsoftJsonReader())    // Rulewright.Json.NewtonsoftJson
    .Build();
```

Both adapters produce identical results from identical JSON.

## 13. Discovering the vocabulary for a UI

Building a rule editor? Enumerate what the engine accepts instead of hard-coding it:

```csharp
foreach (ConditionOperatorInfo op in RuleSchemaCatalog.ConditionOperators)
{
    Console.WriteLine($"{op.JsonName}: value={op.ValueKind}, expressionLeft={op.AllowsExpressionLeft}");
}

RuleSchemaCatalog.ExpressionOperators;   // op name, operand arity, category
RuleSchemaCatalog.ActionTypes;           // + RequiresValue
RuleSchemaCatalog.LogicalOperators;      // AND / OR / NOT + child arity

engine.FunctionCatalog;                  // custom functions: name, description, expected value kind
```

The catalog is derived from the same maps the parser and validator use, so it cannot drift from
what the engine actually accepts. `samples/Rulewright.Sample.BlazorBuilder` is a full
WebAssembly editor built on exactly this.

## 14. Errors you may hit

| Exception | Means | Fix |
|---|---|---|
| `RuleParseException` | The text isn't well-formed JSON, or a number is outside the representable range | Check the JSON; numbers must be finite |
| `RuleValidationException` | The document breaks the schema contract; carries pointer-addressed `Errors` | Call `Validate` first and show the errors |
| `RuleCompilationException` | A rule can't bind to the fact type: unknown field path, wrong operand shape, unregistered `custom` name, bad regex | Check the message — it names the rule id and the exact member or operand |
| `RegexMatchTimeoutException` | A `MatchesRegex` match exceeded the timeout | Simplify the pattern, or raise `UseRegexTimeout` |

`LoadRuleSet` is where nearly everything fails — deliberately. Field paths, operand shapes,
function names, and regex patterns are all checked before the first evaluation, so a bad rule
surfaces at load rather than on a random request.

Evaluation itself is **total**: it never throws on data. A null field, a missing key, a
non-numeric operand — each has defined semantics rather than an exception.

## 15. Behaviour worth knowing

**Numbers.** JSON numbers become `long` when integral, otherwise `decimal` when exactly
representable, otherwise `double`. Comparisons work across numeric types (`5` equals `5.0`), but
there is **no string-to-number coercion** — `"18"` never equals `18`.

**Strings** compare ordinally everywhere — `Contains`, `StartsWith`, `EndsWith`, and the ordering
operators. The same rule and fact answer the same way on every machine, whatever the ambient
culture.

**Dates.** A `DateTime` field compares against an ISO-8601 string constant. `IsInPast` /
`IsInFuture` compare *instants*: a `DateTimeOffset` and a `Local` `DateTime` are resolved to UTC
first, and a `DateTimeKind.Unspecified` value is read as UTC so results don't depend on the
server's time zone. `IsWeekend` / `IsWeekday` stay wall-clock — the Saturday where it happened.

**Regex is time-bounded.** Patterns come from rule authors and run against your data, so matching
is capped at one second by default, raising `RegexMatchTimeoutException` rather than pinning a
thread:

```csharp
.UseRegexTimeout(TimeSpan.FromMilliseconds(250))
```

**A quantifier is one node in a trace.** Per-element results have no single slot to live in, so
the element condition is rendered into the node's description
(`Lines Any (AND(Category Equals "alcohol", Quantity GreaterThan 0))`) rather than traced
separately.

**Evaluation is stateless and single-pass.** One fact in, one result out. Rule outputs never feed
other rules' conditions — there is no forward chaining, by design.

**Native AOT.** Compiled delegates use `System.Linq.Expressions`, which AOT cannot support. Under
NativeAOT use dictionary facts; `CompilationMode.Interpreted` confirms which path ran.

---

Runnable versions of most of the above live in [`examples/`](examples/) (19 documents, each
validated by the test suite) and [`samples/`](samples/) (console, ASP.NET Core, decision tables,
custom functions, .NET Framework 4.8, and the Blazor editor).
