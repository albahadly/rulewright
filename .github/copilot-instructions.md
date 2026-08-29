# Copilot instructions for Rulewright

Rulewright is a **JSON-driven business rule engine for .NET**. The workflow is always:
**parse** a rule document → **validate** it against `docs/schema/rule-schema.json` →
**compile** it to expression-tree delegates (or **interpret** it for dictionary facts) →
**evaluate** facts against it, producing fired rules + merged outputs + an optional trace.

It multi-targets `net48;netstandard2.0;net8.0;net10.0` (set once in `src/Directory.Build.props`),
so the same packages run on .NET Framework 4.8 through modern .NET. Rules are **pure data** — a closed, validatable operator vocabulary,
never embedded code — so a UI can safely generate them and a reviewer can safely diff them.

When you touch evaluation semantics, the JSON schema, or the operator vocabulary, prefer reading
`docs/architecture.md`, `docs/schema/rule-schema.json`, and `README.md` over guessing — they are
the authoritative contract and spell out the subtle rules (null handling, hashing, parity).

---

## Repo layout

- **`src/Rulewright.Core`** — domain model, **zero dependencies**, netstandard2.0-only syntax
  (no `record`, no `init`, no `System.Index`/`Range`, no target-typed `new` that needs newer langver
  behavior). Key types: `Rule`, `RuleSet`, `ConditionNode` (`ConditionLeaf` / `ConditionGroup`),
  `ValueExpression` (`LiteralExpression` / `FieldExpression` / `OperatorExpression`), `RuleAction`,
  `IRuleFunction`, and the result/trace types (`RuleEvaluationResult`, `FiredRule`,
  `EvaluationTrace`, `RuleTrace`, `ConditionTraceNode`). Enums: `ConditionOperator`,
  `LogicalOperator`, `ExpressionOperator`, `RuleBranch`, `CompilationMode`.
- **`src/Rulewright.Serialization`** — the JSON-neutral DOM `RuleJsonValue` (+ `IRuleJsonReader`),
  `RuleSetParser` (DOM ↔ domain, incl. decision-table expansion), `RuleSetValidator` (structural
  validation with JSON-pointer error paths), `RuleHasher` (content hash), and `RuleSchemaCatalog`
  (the vocabulary as structured metadata for UIs/tooling). **No JSON-library dependency.**
- **`src/Rulewright.Execution`** — `RulewrightBuilder` → `RulewrightEngine`. Compiles each rule to
  expression-tree delegates **per fact type**, cached by the rule's **content hash**; falls back to
  an interpreter for dictionary facts. Tracing is a **separate compiled delegate** (opt-in, zero
  cost when off). Key files: `RuleExpressionCompiler`, `RuleInterpreter`, `RuntimeComparisons`,
  `ValueExpressionOps`, `OutputApplier`, `ConditionTraceBuilder`, `CompiledRule`, `LoadedRuleSet`.
- **`src/Rulewright.Json.SystemText`** — `SystemTextJsonReader : IRuleJsonReader` +
  `SystemTextJsonFacts.ToDictionary(JsonElement)`.
- **`src/Rulewright.Json.NewtonsoftJson`** — `NewtonsoftJsonReader : IRuleJsonReader` +
  `NewtonsoftJsonFacts.ToDictionary(JToken)`. Parity-tested against the STJ adapter.
- **`src/Rulewright.Extensions.Functions`** — built-in `custom`-operator predicates
  (`BuiltInFunctions`), `NamedRuleFunction`, and `RulewrightBuilder` extension methods
  (`RegisterBuiltInFunctions`, `RegisterFunctions`, `RegisterFunctionsFrom`).
- **`tests/`** — one xUnit **v3** project per `src/` library (run on both TFMs), plus
  `Rulewright.Benchmarks` (BenchmarkDotNet, with an NRules / MS-RulesEngine comparison).
- **`examples/`** — 19 canonical rule-schema JSON documents (`NN-name.json`), each with a
  golden-file fixture under `tests/Rulewright.Execution.Tests/Fixtures/example-NN.json`. These are
  also the demo content the Blazor samples load.
- **`samples/`** — `ConsoleApp`, `AspNetCore`, `NewtonsoftJson`, `Functions`, `DecisionTable`,
  `NetFramework48`, and two Blazor WASM **visual rule builders** (see the Blazor section below).
- **`docs/schema/rule-schema.json`** (the JSON Schema contract), **`docs/architecture.md`**
  (design writeup: null semantics, hashing, parity), **`docs/benchmarks.md`**.

---

## Hard invariants — do not violate (from CONTRIBUTING.md)

1. **`Rulewright.Core` stays zero-dependency**, and every `src/` library keeps compiling for
   `netstandard2.0`. Don't add a runtime dependency to Core; don't use APIs/syntax unavailable on
   netstandard2.0.
2. **The JSON schema is a contract.** Any change to `docs/schema/rule-schema.json`, `RuleSetValidator`,
   or observable evaluation semantics requires a deliberate, reviewable update to the golden-file
   fixtures — **never regenerate fixtures silently to make a test pass**.
3. **Compiled and interpreted paths stay in parity.** Any change to operator/expression semantics
   touches **both** `RuleExpressionCompiler` and `RuleInterpreter`/`RuntimeComparisons`/`ValueExpressionOps`,
   with tests exercising a typed/compiled fact **and** a dictionary/interpreted fact producing
   identical results.
4. **Public members need XML doc-comments.** The build uses `TreatWarningsAsErrors` (incl.
   missing-docs warnings) — an undocumented public member **fails the build**. Write the doc; never
   `#pragma warning disable` it away.
5. **`RuleHasher` covers only condition + actions** (not id/description/priority/enabled/layout) —
   it is the compiled-delegate cache key. Don't fold metadata into it.
6. **`layout` is opaque and engine-ignored** — reserved for a visual builder's canvas positions.
   Never give it evaluation meaning.

**Adding a new operator or action type is a multi-file change** — schema + validator rule + parser
JSON↔enum mapping + `RuleSchemaCatalog` metadata + compiler impl + interpreter impl + tests for
**both** paths + README/docs + (usually) an `examples/` doc with its golden fixture. Missing any
one makes the PR incomplete.

---

## The rule document (JSON) — exact shapes

A document is **one rule**, a **rule set** (`{ "name", "rules": [...] }`), or a **decision table**
(`{ "decisionTable": {...} }`, expanded to rules at load time). A single rule:

```json
{
  "id": "discount-rule-01",          // required, unique within a set
  "description": "…",                // optional
  "priority": 10,                    // optional, default 0; higher evaluates first, wins output merges
  "enabled": true,                   // optional, default true; false = skipped entirely
  "condition": { … },                // required: a leaf or a group
  "actions": [ … ],                  // run when condition is TRUE
  "else":    [ … ],                  // optional: run when condition is FALSE (same shape as actions)
  "layout":  { … }                   // optional, opaque, engine-ignored (UI canvas positions)
}
```

**Condition — leaf:**
```json
{ "field": "Customer.Age", "operator": "GreaterThan", "value": 18 }
{ "field": "Customer.Email", "operator": "IsNull" }                 // null ops take no value
{ "field": "Customer.Tier", "operator": "In", "value": ["gold","vip"] }   // In/NotIn value is an array
{ "operator": "custom", "name": "IsBusinessDay", "field": "Order.Date" }   // custom uses field (+ optional value)
{ "expression": { "op":"divide", "operands":[{"field":"Order.Total"},{"field":"Order.ItemCount"}] },
  "operator": "GreaterThan", "value": 25 }                          // computed LHS: `expression` instead of `field`
```
A leaf uses **`field` OR `expression`** (never both). A leaf's comparison `value` must stay a
**constant** — only the LHS (`expression`) may be computed. `custom` uses `field` only.

**Condition — group:**
```json
{ "type": "group", "operator": "AND", "rules": [ <leaf-or-group>, … ] }   // AND/OR: 1+ children; NOT: exactly 1
```

**Operators** (JSON spelling → note): `Equals`, `NotEquals`, `GreaterThan`, `GreaterThanOrEqual`,
`LessThan`, `LessThanOrEqual`, `Contains`, `StartsWith`, `EndsWith`, `MatchesRegex` (all ordinal;
anchor your own regex), `In`, `NotIn` (array `value`), `IsNull`, `IsNotNull` (no `value`), and
`custom` (+ `name`). Logical group operators: `AND`, `OR`, `NOT`.
> **Enum-vs-JSON gotcha:** the C# `ConditionOperator` enum spells equality `Equal` / `NotEqual`
> (to avoid colliding with `object.Equals`), but the JSON is `"Equals"` / `"NotEquals"`. All other
> names match. `Custom` ↔ `"custom"`.

**Actions:** `type` is one of `setOutput` (replace), `addToOutput` (numeric running total),
`appendToOutput` (collect into a list), `removeOutput` (delete `target`, no `value`). Applied in
priority order across all fired rules. Accumulators are null-tolerant (a null / non-numeric
contribution is ignored, never wipes the running value). C# constants:
`RuleAction.SetOutputType` / `AddToOutputType` / `AppendToOutputType` / `RemoveOutputType`.

**Action `value` = a value-expression** (constant is the simplest one):
```json
"value": "gold"                                             // bare scalar literal
"value": { "literal": 10 }                                  // explicit literal (hashes same as bare 10)
"value": { "field": "Order.Total" }                         // field reference
"value": { "op": "multiply", "operands": [ { "field": "Order.Total" }, 0.1 ] }
```
Expression ops (JSON, lowercase): `add`, `multiply`, `concat`, `coalesce` (**2+** operands);
`subtract`, `divide`, `modulo` (**exactly 2**, order significant); `negate` (**exactly 1**).
Evaluation is **total** (never throws on data): any null operand → null result (except `coalesce`);
non-numeric to an arithmetic op → null; divide/modulo by zero → null. Arithmetic runs in `decimal`
unless a float operand forces `double`.

**Decision table** (`hitPolicy`: `"collect"` default = every matching row applies; `"first"` = only
the first). `inputs` default to `Equals`, `outputs` default to `setOutput`; a `null` cell is a
wildcard (input) or skip (output). It **expands to ordinary rules at parse time** — no engine
special-casing.

---

## Public API surface

```csharp
// Build an engine (immutable, thread-safe, reusable across evaluations)
RulewrightEngine engine = new RulewrightBuilder()
    .UseJsonReader(new SystemTextJsonReader())               // or NewtonsoftJsonReader
    .RegisterFunction("IsBusinessDay", (fieldValue, value) => /* bool */ true)  // inline custom fn
    .RegisterFunction(new MyRuleFunction())                  // IRuleFunction instance
    .RegisterBuiltInFunctions()                              // Extensions.Functions catalog
    .RegisterFunctionsFrom(typeof(Program).Assembly)         // scan public IRuleFunction types
    .Build();

LoadedRuleSet ruleSet = engine.LoadRuleSet(json);            // parse + validate + compile once (throws on invalid)

RuleEvaluationResult result = engine.Evaluate(ruleSet, fact, new EvaluationOptions
{
    EnableTrace = true,        // opt-in; separate compiled delegate
    StopOnFirstMatch = false,  // stop after the first THEN match (else-firings don't count as matches)
});

result.FiredRules;         // IReadOnlyList<FiredRule>  — each: RuleId, Outputs, Branch (Then/Else)
result.Outputs;            // IReadOnlyDictionary<string, object?> merged across fired rules (priority order)
result.CompilationMode;    // Compiled (typed fact) or Interpreted (dictionary fact)
result.Trace;              // EvaluationTrace? — .Rules[i]: RuleId, Fired, Skipped, Condition (ConditionTraceNode tree)

engine.RegisteredFunctions;// IReadOnlyList<string> — custom-function names on this engine
engine.FunctionCatalog;    // IReadOnlyList<RuleFunctionDescriptor> — name + description + value kind
```

`fact` is a **typed POCO** (compiled path — dotted `field` paths are compile-time member access;
a missing member throws `RuleCompilationException` at load) or an
`IDictionary<string, object?>` (interpreted path — missing keys resolve to null). Build a dictionary
fact from JSON with `SystemTextJsonFacts.ToDictionary(jsonElement)` /
`NewtonsoftJsonFacts.ToDictionary(jToken)`.

Custom functions: implement `IRuleFunction` (`string Name`; `bool Evaluate(object? fieldValue, object? value)`)
— **must be thread-safe** (one shared instance). Make functions **total** (bad type → `false`, never
throw). For inline functions use `new NamedRuleFunction("Name", (field, value) => …)`.

**Validation / discovery / serialization** (in `Rulewright.Serialization`):
```csharp
RuleJsonValue doc = jsonReader.Read(json);
RuleSetValidationResult vr = RuleSetValidator.Validate(doc);   // vr.IsValid, vr.Errors[i].Path (JSON-pointer) + .Message
RuleSchemaCatalog.ConditionOperators / .LogicalOperators / .ExpressionOperators / .ActionTypes;  // structured metadata
```
`RuleSchemaCatalog` is **derived from the same maps/enums the parser and validator use**, so it can
never drift — enumerate it instead of hard-coding operator lists in tooling/UI.

---

## Null semantics (identical in both paths, by design)

A null field value (or null anywhere along a dotted path) makes **every operator return `false`**,
except: `IsNull` → `true`; `NotEquals` (non-null comparand) → `true`; `NotIn` → `true`; and
`Equals` with a `null` comparand → `true`. Typed-fact navigation is null-safe (a null intermediate
applies these semantics rather than throwing). Preserve this exactly when changing comparison code.

---

## Build & test

```
dotnet build Rulewright.slnx
dotnet test  Rulewright.slnx              # net8.0 + net48 (Windows)
dotnet test  Rulewright.slnx -f net8.0    # net8.0 only (Linux/macOS — no net48 runtime there)
```

Both must be **clean: 0 warnings, 0 failures** before a change is done (warnings are errors). The
full suite is currently **376 tests per TFM**. For a Blazor sample change, a clean build is **not**
enough — these are WASM apps with no browser-automation suite: run it
(`dotnet run --project samples/<project>`) and drive the change in a real browser. Check
`samples/<project>/.claude/skills/verify/SKILL.md` for the launch command, key selectors, and known
gotchas before inventing a verification flow.

---

## Blazor rule builder (`samples/Rulewright.Sample.BlazorBuilder`)

One WASM sample that authors **this exact rule schema**, fully client-side (it references
Core/Serialization/Execution/Json.SystemText/Extensions.Functions and runs the real engine in the
browser — no backend). A freeform **drag-and-drop node canvas** (n8n / Logic-Apps style,
hand-rolled, dark theme).

**All interactive state lives in `wwwroot/js/rule-canvas.js`** (a single IIFE,
`window.rulewrightFlowBuilder`); `Pages/Canvas.razor` is just the page shell plus two
`[JSInvokable]` bridge methods to the real engine — `ValidateRule(ruleJson)` (→
`RuleSetValidator`) and `EvaluateRule(ruleJson, factJson)` (→ `LoadRuleSet` + `Evaluate` with
tracing, returning a **per-rule** breakdown keyed by rule id). It builds a **whole rule set at
once**: each rule is a **Rule anchor node** (condition tree → its Condition pin; its output →
Action nodes); 1 Rule node exports a bare rule, 2+ export `{ name, rules[] }`. Trace highlighting
works by zipping a JS "id tree" (same shape as the built condition) against the engine's
`ConditionTraceNode` tree positionally. **When editing this file, re-derive the invariants
documented in its verify SKILL.md** (e.g. import uses `valueToFieldText`, not `JSON.stringify`, for
string fields; `addConnection` auto-grows dynamic-input slots — don't pre-grow). `decisionTable`
documents are not supported by the canvas (they show a toast).

The Blazor sample is not a test project; changes there don't affect the test count, but must be
browser-verified. It is deployed to GitHub Pages by
`.github/workflows/blazor-builder-pages.yml`.

---

## Conventions & what not to do

- **Commits:** Conventional Commits (`feat:`, `fix:`, `chore:`, …), concise subject, `-` bulleted
  body. **No AI-attribution trailers.** Prefer small, focused PRs mirroring one roadmap item
  (README "Roadmap") over broad refactors.
- **Match surrounding style:** file-scoped namespaces, explicit types in library code, XML docs on
  everything public, comment density that matches the neighbouring file.
- **Don't** special-case behavior between the compiled and interpreted paths — they must agree by
  construction (shared routines like `RuntimeComparisons` / `ValueExpressionOps`).
- **Don't** change `docs/schema/rule-schema.json` or a golden fixture without updating the other side
  of that contract in the same change.
- **Don't** suppress `TreatWarningsAsErrors` or add `#pragma warning disable` to dodge a missing XML
  doc — write the doc.
- **Don't** add a runtime dependency to `Rulewright.Core`, or use non-netstandard2.0 syntax in any
  `src/` library.
- **Don't** give `layout` evaluation meaning or fold rule metadata into `RuleHasher`.
