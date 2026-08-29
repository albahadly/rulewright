# Rulewright

A high-performance, JSON-driven business rule engine for .NET. Rules are plain JSON
documents; evaluation is compiled expression trees — parse once, compile once, execute
millions of times.

[![CI](https://github.com/albahadly/Rulewright/actions/workflows/ci.yml/badge.svg)](https://github.com/albahadly/Rulewright/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

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
    { "type": "setOutput", "target": "Discount", "value": 10 },
    { "type": "setOutput", "target": "DiscountReason", "value": "VIP or high-value order" }
  ]
}
```

```csharp
var engine = new RulewrightBuilder()
    .UseJsonReader(new SystemTextJsonReader())
    .RegisterFunction("IsBusinessDay", (fieldValue, value) => /* ... */ true)
    .Build();

LoadedRuleSet ruleSet = engine.LoadRuleSet(json);   // parse + validate once

var result = engine.Evaluate(ruleSet, customerOrder, new EvaluationOptions
{
    EnableTrace = true,
    StopOnFirstMatch = false,
});

foreach (FiredRule fired in result.FiredRules)
    Console.WriteLine($"{fired.RuleId} -> {string.Join(", ", fired.Outputs)}");
```

## Why Rulewright?

- **Compiled, not interpreted.** Rules compile to delegates via expression trees:
  parse once, compile once per fact type, then evaluations are direct delegate calls —
  no reflection, no string parsing, no boxing of value-type comparisons. Compiled
  delegates are cached by a **content hash** of the rule (not its id), so editing a
  rule's body always invalidates the cache, while reformatting or moving canvas nodes
  never does.
- **One package, .NET Framework 4.8 through .NET 10.** Every library multi-targets
  `net48;netstandard2.0;net8.0;net10.0`, and the core has **zero dependencies** on all
  four. A .NET Framework 4.8 sample project builds and runs in CI — the compatibility
  claim is executed, not asserted.
- **Bring your own JSON library.** Parsing is abstracted behind `IRuleJsonReader`
  with adapter packages for **System.Text.Json** and **Newtonsoft.Json** (parity-tested
  against each other), so the core never forces a JSON dependency on your app.
- **Built for auditability.** Opt-in execution traces record which rules fired and
  which condition nodes passed, failed, or were short-circuited — with **zero
  overhead when disabled** (separate compiled fast path, not a runtime flag check).
- **Designed for a visual future.** The schema keeps logic and presentation apart:
  the `layout` key is reserved for drag-and-drop canvas tools and ignored by the
  engine. A formal JSON Schema (`docs/schema/rule-schema.json`), a standalone
  validator with JSON-pointer errors, and a runtime **discovery catalog**
  (`RuleSchemaCatalog` + `engine.RegisteredFunctions`) are the contract a future
  rule-builder UI plugs into.

### Why not NRules or Microsoft RulesEngine?

| | Rulewright | NRules | MS RulesEngine |
|---|---|---|---|
| Rule format | JSON documents | C# fluent DSL | JSON with lambda-expression strings |
| Evaluation | Compiled expression trees | Rete network | Parsed/compiled C# expression strings |
| Inference / chaining | No (v1 non-goal) | Yes | No |
| netstandard2.0 + .NET FX 4.8 | Yes, zero-dep core | Yes | Partial |
| Layout metadata for UI round-trip | First-class (`layout`) | n/a | n/a |
| Execution trace | Opt-in, per-node | Events | Partial |

NRules is the right tool for forward-chaining inference over a working memory.
Microsoft RulesEngine embeds C# expression *strings* in JSON, which means arbitrary
code in rule files and runtime string compilation. Rulewright's rules are pure data:
a closed, validatable operator vocabulary that a UI can safely generate and a
reviewer can safely diff. For the stateless "evaluate this fact against these rules"
case, Rulewright is also measurably faster and leaner than both — see
[`docs/benchmarks.md`](docs/benchmarks.md) for a like-for-like harness (with a
fairness check that all three flag the same matches).

## Install

```
dotnet add package Rulewright
```

That is the metapackage: the domain model, JSON parsing and schema validation, the
evaluation engine, the System.Text.Json adapter, and the built-in `custom` functions.

Prefer to pick pieces? Reference `Rulewright.Execution` plus one JSON adapter:

```
dotnet add package Rulewright.Execution
dotnet add package Rulewright.Json.NewtonsoftJson    # or Rulewright.Json.SystemText
```

| Package | What it is |
|---|---|
| `Rulewright` | Everything below except the Newtonsoft adapter. Start here. |
| `Rulewright.Execution` | The compiler, interpreter, delegate cache, and engine API. |
| `Rulewright.Serialization` | Parsing, validation, and canonical hashing — no evaluation, no JSON library. |
| `Rulewright.Core` | The domain model alone. Zero dependencies. |
| `Rulewright.Json.SystemText` | `IRuleJsonReader` over System.Text.Json, plus JSON fact helpers. |
| `Rulewright.Json.NewtonsoftJson` | The same, over Newtonsoft.Json. |
| `Rulewright.Extensions.Functions` | The built-in `custom`-operator predicate catalog. |

All packages target .NET Framework 4.8, .NET Standard 2.0, .NET 8.0 and .NET 10.0, are
strong-named, and ship symbols (`.snupkg`) with Source Link.

## Concepts

| Term | Meaning |
|---|---|
| **Rule** | `id` + condition tree + `actions` (+ optional `else` actions, `priority`, `enabled`, ignored `layout`). |
| **Condition** | A leaf (`field` / `operator` / `value`) or a group (`AND` / `OR` / `NOT` over children). |
| **Fact** | The object a rule set is evaluated against: a typed POCO (compiled path) or an `IDictionary<string, object>` (interpreted path). |
| **Action** | Changes the outputs at `target`. `setOutput` replaces, `addToOutput` sums, `appendToOutput` collects into a list, `removeOutput` deletes. Runs from `actions` when the condition matches, or `else` when it does not. |

### Operators

Comparison `Equals`, `NotEquals`, `GreaterThan`, `GreaterThanOrEqual`, `LessThan`,
`LessThanOrEqual` · String (ordinal) `Contains`, `StartsWith`, `EndsWith`,
`MatchesRegex` · Collection `In`, `NotIn` · Null `IsNull`, `IsNotNull` · Logical
`AND`, `OR`, `NOT` · Extensible `custom` + `name`, resolved against functions
registered on the builder **at compile time**.

For the `custom` operator, `Rulewright.Extensions.Functions` ships a curated catalog of
ready-made predicates and helpers to register them:

```csharp
var engine = new RulewrightBuilder()
    .UseJsonReader(new SystemTextJsonReader())
    .RegisterBuiltInFunctions()                       // IsEven, EqualsIgnoreCase, IsWeekend, …
    .RegisterFunctionsFrom(typeof(Program).Assembly)  // scan your own IRuleFunction classes
    .Build();
```

The catalog covers what the closed operator set deliberately doesn't: `IsNullOrEmpty`,
`IsNullOrWhiteSpace`, `EqualsIgnoreCase`, `IsEmail`, `IsEven`/`IsOdd`, `IsPositive`/`IsNegative`,
`DivisibleBy`, `IsBetweenInclusive`, `IsWeekend`/`IsWeekday`, and `IsInPast`/`IsInFuture` (with an
injectable clock). Each is **total** — an unexpected value type yields `false`, never an
exception. Write your own with `new NamedRuleFunction("MyCheck", (field, value) => …)`.

A `custom` leaf's `value` is whatever its function expects — a scalar, a string, or an array
such as `IsBetweenInclusive`'s `[min, max]`:

```json
{ "field": "Customer.Age", "operator": "custom", "name": "IsBetweenInclusive", "value": [18, 65] }
```

`MatchesRegex` patterns run against consumer-supplied fact data, so matching is time-bounded:
one second by default, raising `RegexMatchTimeoutException` rather than letting a pattern with
catastrophic backtracking pin a thread. Change the bound with
`.UseRegexTimeout(TimeSpan.FromMilliseconds(250))`.

### Field resolution

`"field": "Customer.Age"` resolves a dotted path against the fact:

- **Typed facts**: compile-time member access (`Expression.PropertyOrField` chains).
  A missing member is a load/compile-time `RuleCompilationException`, not a runtime
  surprise. Navigation is null-safe: a null intermediate applies the operator's null
  semantics instead of throwing.
- **Dictionary facts**: nested dictionaries, with cached-reflection fallback for POCOs
  stored inside dictionaries. Missing keys resolve to null. The result reports
  `CompilationMode.Interpreted` so the slower path is visible, never silent.

### Computed left-hand sides

A condition leaf can compare a **computed expression** to a value, not only a field — use
`expression` in place of `field`, with the same value-expression vocabulary as actions:

```json
{ "expression": { "op": "divide", "operands": [ { "field": "Order.Total" }, { "field": "Order.ItemCount" } ] },
  "operator": "GreaterThan", "value": 25 }
```

That is, *average item price > 25*. The compiled path evaluates the left side with the same
reflection-free field access as a plain leaf, then both paths compare through one shared
operator routine, so a `field` leaf and an equivalent `expression` leaf agree. A leaf uses
`field` **or** `expression` (not both); `custom` uses `field`. See
[examples/15-condition-side-expression.json](examples/15-condition-side-expression.json).

### Null semantics (both paths, by design)

A null field value (or null anywhere along the path) makes every operator return
`false`, except: `IsNull` → `true`, `NotEquals` (non-null comparand) → `true`,
`NotIn` → `true`, and `Equals` with a `null` comparand → `true`.

### Actions — constant and computed values

A `setOutput` action writes a `value` into the result's outputs. That value is a **bare
scalar** (a constant) or an **expression** computed from the fact at evaluation time — one
key, and a constant is simply the simplest expression:

```json
"actions": [
  { "type": "setOutput", "target": "Tier", "value": "gold" },
  { "type": "setOutput", "target": "Discount",
    "value": { "op": "multiply", "operands": [ { "field": "Order.Total" }, 0.1 ] } },
  { "type": "setOutput", "target": "Message",
    "value": { "op": "concat", "operands": [ "Thanks, ", { "field": "Customer.Name" }, "!" ] } }
]
```

Values are **pure data** — a closed operator vocabulary, never embedded code — so a UI can
generate them and a reviewer can diff them, exactly like conditions. A value is a bare scalar
(a literal), `{ "field": "<dotted path>" }`, `{ "literal": <scalar> }`, or
`{ "op": "<operator>", "operands": [ … ] }`:

| Category | Operators |
|---|---|
| Arithmetic | `add`, `subtract`, `multiply`, `divide`, `modulo`, `negate` |
| String | `concat` |
| Null | `coalesce` (first non-null operand) |

`add`/`multiply`/`concat`/`coalesce` take two or more operands; `subtract`/`divide`/`modulo`
take exactly two (order significant); `negate` takes one. Evaluation is **total** — it never
throws on data: any null operand propagates to a null result (except `coalesce`), a
non-numeric operand to an arithmetic operator yields null, and division or modulo by zero
yields null. Arithmetic runs in `decimal` unless a floating-point operand forces `double`, so
`divide` is never silently integer-truncated. Computed outputs are compiled to delegates and
cached per fact type just like conditions (constant-only rules keep their pre-materialized
outputs and allocate nothing per firing); field references in expressions are checked against
typed facts at compile time.

**Action types.** The action `type` decides how the value combines with what fired rules have
already written to that `target`, applied in priority order across the whole evaluation:

| Type | Effect |
|---|---|
| `setOutput` | Replaces the value at `target`. |
| `addToOutput` | Adds numerically — a running total across fired rules. |
| `appendToOutput` | Appends to a list at `target` — collected across fired rules. |
| `removeOutput` | Deletes `target`, undoing what an earlier rule wrote (takes no `value`). |

```json
"actions": [
  { "type": "addToOutput", "target": "RiskScore",
    "value": { "op": "multiply", "operands": [ { "field": "Order.Total" }, 0.1 ] } },
  { "type": "appendToOutput", "target": "Reasons", "value": "high-value order" }
]
```

The accumulators are **null-tolerant**: a value that resolves to null (and, for `addToOutput`,
any non-numeric value) contributes nothing rather than wiping the running result — so one stray
rule can't destroy a total. Each fired rule's own `Outputs` snapshot reflects the result *at
that rule's firing* (`appendToOutput` copies the list, so earlier snapshots stay frozen).

**Else actions.** A rule may carry an `else` array alongside `actions`: it runs the `actions`
when the condition matches and the `else` actions when it does not — an if/else in one rule,
rather than a rule plus a negated twin. An else-branch firing is not a match, so it never
triggers `stopOnFirstMatch`. Each such rule appears in `FiredRules` with a `Branch` of `Then`
or `Else` telling you which side ran.

```json
{
  "id": "tier",
  "condition": { "field": "Customer.IsVip", "operator": "Equals", "value": true },
  "actions": [ { "type": "setOutput", "target": "Badge", "value": "gold" } ],
  "else":    [ { "type": "setOutput", "target": "Badge", "value": "standard" } ]
}
```

### Decision tables

For logic that reads naturally as a grid, a **decision table** is a compact authoring form.
Each input column maps a cell to a condition; each output column maps a cell to an action:

```json
{
  "decisionTable": {
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

A table **expands into ordinary rules at load time** — one rule per row — so it runs through
the exact same compiled/interpreted path with no special casing, and the same schema, tracing,
and hashing apply. A `null` input cell is a **wildcard**; an all-wildcard row is a **catch-all**.
Input columns default to `Equals` and take any comparison operator (`In` cells are arrays);
output columns default to `setOutput` and may use `addToOutput`/`appendToOutput`. A `null`
output cell skips that output for the row. Two hit policies:

- **`collect`** (default) — every matching row applies its actions in row order (pairs with
  `addToOutput` for scoring tables).
- **`first`** — only the first matching row applies. Encoded by ANDing each row with the
  negation of every earlier row's condition, so exactly one row fires under normal evaluation.

### Discovering the vocabulary

The rule schema is a **closed, pure-data vocabulary** — and it is introspectable at runtime, so
a rule-builder UI (or codegen, or docs) can enumerate exactly what it may author instead of
hard-coding operator lists. `RuleSchemaCatalog` exposes the built-in vocabulary as structured
metadata, and `engine.RegisteredFunctions` exposes the `custom` functions registered on a given
engine — together, the complete set of authoring choices.

```csharp
foreach (var op in RuleSchemaCatalog.ConditionOperators)
    Console.WriteLine($"{op.JsonName}: value={op.ValueKind}, expressionLeft={op.AllowsExpressionLeft}");
// Equals: value=Scalar, expressionLeft=True   ·   IsNull: value=None …   ·   custom: value=Custom …

RuleSchemaCatalog.ExpressionOperators;  // op, operand arity (min/max), category
RuleSchemaCatalog.ActionTypes;          // setOutput/addToOutput/appendToOutput/removeOutput + RequiresValue
RuleSchemaCatalog.LogicalOperators;     // AND/OR/NOT + child arity

engine.RegisteredFunctions;             // e.g. ["IsBusinessDay", "IsWeekend"] — for the custom operator
```

The catalog is **derived from the same maps and enums the parser and validator use**, so it can
never drift from what the engine actually accepts.

## Samples

| Project | Shows |
|---|---|
| [`Rulewright.Sample.ConsoleApp`](samples/Rulewright.Sample.ConsoleApp) | The quickstart: load a rule set, evaluate a typed fact (compiled path) and a dictionary fact (interpreted path), print a trace. |
| [`Rulewright.Sample.AspNetCore`](samples/Rulewright.Sample.AspNetCore) | The engine and a loaded rule set as ASP.NET Core singletons behind a minimal API: `POST /evaluate` a fact, `GET /rules`, `GET /vocabulary` (the schema discovery catalog over HTTP). |
| [`Rulewright.Sample.NewtonsoftJson`](samples/Rulewright.Sample.NewtonsoftJson) | The same quickstart with `NewtonsoftJsonReader` in place of `SystemTextJsonReader` — swapping the JSON adapter is the only change. |
| [`Rulewright.Sample.Functions`](samples/Rulewright.Sample.Functions) | The three ways to register `custom`-operator functions together: `RegisterBuiltInFunctions`, an inline `RegisterFunction` delegate, and `RegisterFunctionsFrom` assembly discovery. |
| [`Rulewright.Sample.DecisionTable`](samples/Rulewright.Sample.DecisionTable) | Loading `decisionTable` documents end to end, contrasting `hitPolicy: "first"` (one row wins) against `"collect"` (every matching row's actions apply). |
| [`Rulewright.Sample.NetFramework48`](samples/Rulewright.Sample.NetFramework48) | A .NET Framework 4.8 smoke test proving the netstandard2.0 packages work end to end outside .NET (Core). |
| [`Rulewright.Sample.BlazorBuilder`](samples/Rulewright.Sample.BlazorBuilder) | The v3 rule builder: a Blazor WebAssembly app with a freeform, drag-and-drop dark-themed node canvas (palette → drag nodes onto a canvas → wire them together) in the style of n8n/Logic Apps/Node-RED. Covers the full authoring surface — all condition operators, computed value-expressions, all four action types, else branches, custom functions, and multi-rule sets — and can load any of the `examples/` rule files onto the canvas. Backed by the real engine for Validate/Test, in-browser: no server, no round trip, no simulation. |

Build rule JSON files in the hosted editor: https://albahadly.github.io/rulewright

Run any of them with `dotnet run --project samples/<ProjectName>` (the ASP.NET Core sample also
needs `dotnet run` — it listens on the URL printed at startup; try `curl -X POST .../evaluate -d
'{"fact":{"Customer":{"Age":30,"IsVip":true},"Order":{"Total":10}}}' -H content-type:application/json`).

## Performance

The benchmark suite (`tests/Rulewright.Benchmarks`, BenchmarkDotNet) measures
compiled vs interpreted throughput at 1 / 100 / 10,000 rules, cold-load vs
warm-cache cost, and a like-for-like comparison against NRules and Microsoft
RulesEngine. Run it with:

```
dotnet run -c Release --project tests/Rulewright.Benchmarks -- --filter '*'
```

A warm compiled evaluation of a small rule set is on the order of **~100 ns/rule**
with minimal allocation; the dictionary interpreter is ~1.8× slower by design, and
tracing (a separate compiled delegate) costs ~3× **only when enabled**. Against the
same rule set and fact, Rulewright evaluates the stateless one-shot case faster and
far leaner than both NRules and Microsoft RulesEngine — full tables, methodology, and
a fairness check are in [`docs/benchmarks.md`](docs/benchmarks.md). *(Numbers are
illustrative and machine-specific; re-run the suite on your hardware.)*

## Building from source

```
git clone https://github.com/albahadly/Rulewright.git
cd Rulewright
dotnet build Rulewright.slnx
dotnet test  Rulewright.slnx            # runs on net8.0 and net48 (Windows)
dotnet run --project samples/Rulewright.Sample.ConsoleApp
```

Requires the .NET 8+ SDK. On Windows, the test suite and the
`Rulewright.Sample.NetFramework48` smoke test also exercise .NET Framework 4.8.

## Packages

| Package | Contents |
|---|---|
| `Rulewright.Core` | Domain model: `Rule`, conditions, results, `IRuleFunction`. Zero dependencies. |
| `Rulewright.Serialization` | JSON ↔ domain mapping, structural validator (JSON-pointer errors), content hashing. |
| `Rulewright.Execution` | Expression-tree compiler, interpreter fallback, delegate cache, `RulewrightBuilder` / `RulewrightEngine`. |
| `Rulewright.Json.SystemText` | `IRuleJsonReader` adapter for System.Text.Json + `JsonElement` fact helpers. |
| `Rulewright.Json.NewtonsoftJson` | `IRuleJsonReader` adapter for Newtonsoft.Json + `JToken` fact helpers. |
| `Rulewright.Extensions.Functions` | Built-in `custom`-operator predicates + registration/assembly-scan discovery helpers. |

## Non-goals for v1

Called out explicitly so expectations are clear:

- **No forward-chaining / RETE-style inference.** One fact set in, one result out —
  stateless, single-pass evaluation. Rule outputs never feed other rules' inputs.
- **No persistence layer.** Storing rule JSON is your application's concern; this
  library parses, validates, compiles, and executes.
- **No UI in v1.** The Blazor rule builder (`Rulewright.Sample.BlazorBuilder`, v3) is a
  sample, not part of the library, and it treats the JSON Schema and the validator built
  here as its contract. Its canvas positions nodes by auto-layout rather than persisting
  the schema's `layout` key — round-tripping `layout` is still a follow-up.

## Roadmap

- **v1** — core engine: compiled evaluation, interpreter fallback, tracing,
  schema + validator, benchmarks, net48 proof.
- **v2 (current)** — *"rules do more."* Computed action expressions (arithmetic,
  `concat`, `coalesce` over fact fields), accumulating action types
  (`addToOutput`/`appendToOutput`), a `removeOutput` retract action, first-class `else`
  actions, decision-table authoring, a schema discovery catalog (`RuleSchemaCatalog` +
  `engine.RegisteredFunctions`), System.Text.Json **and** Newtonsoft.Json adapters, and a
  published NRules/RulesEngine benchmark comparison (`docs/benchmarks.md`) have all shipped.
- **v3 (current)** — Blazor WebAssembly rule builder (`samples/Rulewright.Sample.BlazorBuilder`)
  emitting/consuming this exact schema, running fully client-side. Shipped: a freeform
  drag-and-drop node canvas covering the whole authoring surface — every condition operator,
  computed value-expressions, all four action types, `else` branches, and `custom` functions —
  authoring **multi-rule sets** (one Rule anchor node per rule; two or more export
  `{ name, rules[] }`), with import of any `examples/` document and per-rule evaluate/trace
  against the real engine. Not yet: `decisionTable` documents on the canvas, and persisting
  node positions into the schema's `layout` key (both follow-ups). From v1 on, changes to the
  `layout` contract or the JSON Schema are treated as breaking changes.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). By participating you agree to the
[Code of Conduct](CODE_OF_CONDUCT.md).

## License

[MIT](LICENSE)
