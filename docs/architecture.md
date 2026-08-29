# Rulewright Architecture

## Layering

```
Rulewright.Json.SystemText   ──┐
Rulewright.Json.NewtonsoftJson ┤ (adapters: JSON text → neutral DOM)
                             ▼
                 Rulewright.Serialization      Rulewright.Core
                 (DOM, parser, validator,  ──► (domain model, results,
                  canonical hash)               IRuleFunction)
                             ▲
                             │
                 Rulewright.Execution
                 (compiler, interpreter, cache,
                  RulewrightBuilder / RulewrightEngine)
```

- **Core** is pure domain model — no I/O, no JSON, zero dependencies, immutable after
  construction.
- **Serialization** defines a minimal neutral JSON tree (`RuleJsonValue`) plus
  `IRuleJsonReader`. Adapter packages translate their library's DOM into it; that is
  the *only* thing an adapter does. This keeps `netstandard2.0` consumers free to pick
  System.Text.Json, Newtonsoft, or anything else.
- **Execution** owns everything that runs: the expression-tree compiler, the
  dictionary-fact interpreter, the compiled-delegate cache, and the public
  builder/engine API.

## The evaluation pipeline

1. **Parse once** — `LoadRuleSet(json)`: adapter → neutral DOM → structural
   validation (JSON-pointer errors) → immutable `RuleSet`.
2. **Prepare once** — per rule: content hash, pre-sorted evaluation order
   (priority desc, document order for ties), prematerialized action outputs,
   pre-order condition-node index (for tracing).
3. **Compile once per fact type** — first `Evaluate<TFact>` compiles the rule into
   two delegates (see *Tracing*) and caches them.
4. **Execute many** — subsequent evaluations are direct delegate invocations.

## Compiled-delegate cache

- Key: `(fact CLR type, rule content hash)` in a `ConcurrentDictionary` — safe for
  concurrent readers, lock-free on the hot path.
- The content hash (`RuleHasher`) is a SHA-256 over a canonical rendering of exactly
  the compilation-relevant content: the **condition tree and actions**, with sorted
  keys and invariant number formatting. It is insensitive to whitespace, key order,
  `layout`, `id`, `description`, `priority`, and `enabled` — none of which change the
  compiled delegate — so canvas edits and metadata changes never trigger recompiles,
  while any semantic change always does. Two rules with identical bodies share one
  compiled delegate.
- Numeric literals are rendered by **numeric behaviour, not just by text**: a `double`/`float`
  literal carries a `d` suffix in the canonical form, because binary floating point makes
  arithmetic run in `double` where integral and `decimal` literals make it run in `decimal`
  (`1/3` differs between the two). Without that, `1.0d` and `1L` would render alike and the
  second rule would silently execute the first rule's compiled delegate. JSON numbers parse to
  `long`/`decimal` whenever representable, so the suffix only ever tags values that genuinely
  behave differently.

## Compilation strategy

`RuleExpressionCompiler` builds one `Expression<Func<TFact, bool>>` per rule:

- **Field paths** become chained member accesses with block-scoped locals, so each
  segment is evaluated exactly once. Reference-type and `Nullable<T>` links get null
  guards that short-circuit into the operator's null semantics (below) instead of
  throwing `NullReferenceException`.
- **Constants are converted at compile time** to the field's exact CLR type
  (`ValueConverter`): JSON `18` (long) becomes `int 18` for an `int` field; ISO
  strings become `DateTime`/`DateTimeOffset`/`TimeSpan`/`Guid`; strings become enum
  members. Value-type comparisons therefore run unboxed. Conversion failures surface
  as `RuleCompilationException` with the rule id and field path.
- **Numeric comparisons**: if the constant converts exactly to the field type, the
  comparison runs in that type (fast path). Otherwise (`int` field vs `10.5`) both
  sides widen once to `decimal` (or `double` when binary floating point is involved).
  Exact equality against a non-representable constant folds to a compile-time
  `false`/`true`.
- **Strings** are ordinal: `Contains`/`StartsWith`/`EndsWith` with
  `StringComparison.Ordinal`, ordering via `string.CompareOrdinal` (**not**
  `Comparer<string>.Default`, which orders by the ambient culture and would make the same
  rule and fact answer differently per machine), equality via `==`/`EqualityComparer<string>`,
  and `In`/`NotIn` via an ordinal `HashSet<string>`. `MatchesRegex` embeds a
  `RegexOptions.Compiled` regex constructed at rule-compile time, with a bounded match
  timeout (`RulewrightBuilder.UseRegexTimeout`, one second by default) so a pattern with
  catastrophic backtracking raises `RegexMatchTimeoutException` instead of pinning the
  thread on consumer-supplied data.
- **`In`/`NotIn`** build a typed `HashSet<T>` once at compile time and emit a
  `Contains` call.
- **Custom functions** are looked up in the registry at compile time and embedded as
  constants — the compiled delegate calls `IRuleFunction.Evaluate` directly, no
  per-evaluation name resolution.
- **Computed left-hand sides** (`ConditionLeaf.Left`) compile the same `ValueExpression`
  used by actions (so field access stays reflection-free), then compare through the shared
  `RuleInterpreter.ApplyOperator` — the exact boxed operator logic the interpreter uses.
  A field leaf keeps its fast typed comparison; only computed-left-hand-side leaves take the
  boxed path, and both agree by construction. Field references are type-checked against the
  fact at compile time, like any other field.

## Action values (constant and computed)

Most actions write a single `value` to a `target`. That value is always a `ValueExpression`
— a closed, pure-data AST (`LiteralExpression`, `FieldExpression`, `OperatorExpression`)
mirroring the condition tree, with no embedded code strings. A JSON scalar parses to a
`LiteralExpression`; a JSON object to a field, operator, or explicit-literal node. There is
one value key, not two: a constant is simply the simplest expression. The action `type`
(`setOutput` / `addToOutput` / `appendToOutput` / `removeOutput`) decides how it changes the
running result.

- **Application, not merge.** Actions apply to a single running outputs dictionary owned by
  the evaluation, in priority order across all fired rules — so accumulators (`addToOutput`,
  `appendToOutput`) build totals and collections over the whole run, and `removeOutput` can
  delete a key an earlier rule wrote. `OutputApplier` centralizes the set/add/append/remove
  semantics so the compiled and interpreted paths behave identically. Each fired rule also
  returns its own snapshot of what it wrote (the value at each touched target right after the
  rule); `appendToOutput` copies the list on write, so a rule's snapshot stays frozen as later
  rules extend the collection, and `removeOutput` deletes the key from the snapshot too so a
  rule never reports a value it just removed.
- **Else branch.** A rule may carry `else` actions that run when its condition is *false*
  (the ordinary `actions` run when it is true). The engine applies exactly one branch per
  non-skipped rule; an else-branch firing is not a match, so it never triggers
  `stopOnFirstMatch`. Each branch is prepared independently — its own pre-materialized
  dictionary or `OutputStep<TFact>[]` — and a rule appears in `FiredRules` with `Branch` set
  to `Then` or `Else`. `removeOutput` is a natural else action (grant in `actions`, retract in
  `else`).
- **Compilation.** A branch with any action that is not a constant `setOutput` gets, alongside
  the rule's two predicates, an array of `OutputStep<TFact>` (action type, target, and a
  compiled `Func<TFact, object?>` value delegate), cached under the same `(fact type, content
  hash)` key — one array for `actions`, one for `else`. Field reads compile to the same
  null-guarded member access as conditions and are **type-checked against the fact at compile
  time**; a missing member is a `RuleCompilationException`, not a runtime surprise. A branch
  made purely of constant `setOutput` actions carries **no** steps — the engine
  pre-materializes its outputs into one shared dictionary and allocates nothing per firing.
- **Shared semantics.** Both paths funnel operator evaluation through one place —
  `ValueExpressionOps`, operating on boxed `object?`. The compiled path emits calls to
  those methods (field access stays compiled/reflection-free); the interpreter
  (`ActionExpressionInterpreter`) calls them directly for dictionary facts. Typed and
  dictionary facts therefore produce identical outputs by construction, exercised by
  parity tests. Actions run only for *fired* rules, so boxing here never touches the
  condition hot path the performance claims are about.
- **Total evaluation.** Operators never throw on data: any null operand propagates to a
  null result (except `coalesce`, which returns the first non-null operand), a non-numeric
  operand to an arithmetic operator yields null, and division or modulo by zero yields
  null. Arithmetic runs in `decimal` unless a binary floating-point operand forces
  `double`, so `divide` is never silently integer-truncated. The accumulators are equally
  tolerant: `addToOutput` ignores a null or non-numeric contribution (initializing from a
  decimal zero) and `appendToOutput` ignores a null, so one stray rule cannot wipe a total
  or a collection.
- **Hashing.** The content hash covers action values (canonical
  `{"target":…,"type":…,"value":…}`, the value rendered as a bare scalar for a literal or as
  the node object otherwise). `5` and `{ "literal": 5 }` parse to the same
  `LiteralExpression` and therefore hash identically. `removeOutput` omits the `value` key
  (it has none), and `else` actions add a canonical `"else":[…]` section only when present —
  so a rule with no else keeps its former hash.

## Decision tables

A `decisionTable` document is **expanded into an ordinary `RuleSet` at parse time**
(`RuleSetParser.ExpandDecisionTable`), so the engine, compiler, interpreter, and Core model
need no awareness of it — tables inherit tracing, hashing, and the compiled/interpreted split
for free. Each row becomes one rule (priority descending by row order):

- **Conditions.** Each input column pairs a `field` + `operator` (default `Equals`) with the
  row's `when` cell. A null cell is a wildcard (that column contributes no leaf); the active
  leaves become a single leaf, an `AND` group, or — when a row is all wildcards — a
  synthesized always-true catch-all (`field IsNotNull OR field IsNull`).
- **Actions.** Each output column pairs a `target` + `type` (default `setOutput`) with the
  row's `then` cell, parsed as a value expression (so cells can be computed). A null cell
  writes nothing for that output.
- **Hit policy.** `collect` (default) leaves rows independent — all matches apply in order.
  `first` bakes exclusivity into the conditions: row *n* is `AND(ownₙ, ¬own₀, …, ¬ownₙ₋₁)`, so
  the rows are mutually exclusive and exactly the first match fires under normal evaluation —
  no new engine flag. Structural validation (cell counts, operator/type vocabularies, cell
  shapes) happens in `RuleSetValidator` with the same JSON-pointer error surface as rules.

## Schema discovery

The authoring vocabulary is closed and finite, so it can be **enumerated at runtime** for a
rule-builder UI (or codegen/docs) rather than hard-coded twice. `RuleSchemaCatalog`
(Serialization) exposes it as structured metadata — condition operators (with their
`OperatorValueKind` and whether they allow a computed left-hand side), logical combinators
(with child arity), expression operators (with operand arity and category), and action types
(with `RequiresValue`/`Effect`). The catalog is **derived from the same sources the parser and
validator use** — `OperatorMap`, `ExpressionOperatorMap`, `RequiredArity`, and the domain enums
— iterating the enums so a newly added operator appears automatically and cannot drift from what
the engine accepts. The classification (value kinds, categories) is a coarser authoring hint;
`RuleSetValidator` remains the authority on what actually validates. The one per-engine variable
— the registered `custom` functions — is exposed separately as `RulewrightEngine.RegisteredFunctions`,
since it is instance state rather than fixed vocabulary.

## Null semantics

Identical across compiled and interpreted paths, exercised by shared tests:

| Situation | Result |
|---|---|
| `IsNull` on null field/path | `true` |
| `IsNotNull` on null field/path | `false` |
| `Equals` with `null` comparand, null field | `true` |
| `NotEquals` with non-null comparand, null field | `true` |
| `NotIn` with null field | `true` |
| Any other operator with a null field/path | `false` |
| `custom` with a null path | function is called with `null` fieldValue |

A leaf's `value` is converted **by its JSON shape, not by its operator**: an array becomes
`object?[]` (recursively), any other node its scalar CLR value. That is what `In`/`NotIn` need,
and equally what a `custom` function declaring `RuleFunctionValueKind.Array` needs — the
built-in `IsBetweenInclusive` takes `[min, max]`. Object nodes have no CLR mapping and
`RuleSetValidator` rejects them at the pointer of the offending node, so a document that
validates can always be parsed.

Missing members on **typed** facts are compile-time errors; missing keys on
**dictionary** facts resolve to null (there is no compile-time shape to check).

## Tracing with zero disabled-cost

Each rule compiles **two** delegates from the same expression builder:

- the fast path: `Func<TFact, bool>` — no tracing artifacts at all;
- the traced path: `Func<TFact, bool?[], bool>` — every condition node's boolean
  outcome is additionally written into a slot array (`results[i] = (bool?)node`).

Nodes are indexed by a pre-order walk (`ConditionNodeIndexer`) shared by the compiler,
the interpreter, and `ConditionTraceBuilder`, which re-hydrates the `bool?[]` into a
`ConditionTraceNode` tree after evaluation. Slots left `null` mean "short-circuited,
never evaluated" and surface as `Passed == null`. `EvaluationOptions.EnableTrace`
selects which delegate runs — tracing off costs nothing beyond an untraced call.

## Interpreter fallback

Dictionary facts (`IDictionary<string, object>`, nested dictionaries, POCOs inside
dictionaries via cached reflection) cannot be typed at compile time, so
`RuleInterpreter` walks the condition tree directly with `RuntimeComparisons`
mirroring the compiled semantics. Results report
`CompilationMode.Interpreted` — the throughput difference is documented and measured
in the benchmark suite rather than hidden.

## Thread safety

- `RulewrightEngine` is immutable after `Build()`; the delegate cache is a
  `ConcurrentDictionary`.
- `LoadedRuleSet`, `RuleSet`, and every result type are immutable.
- Compiled delegates and interpreter state are stateless per call; per-evaluation
  buffers (`bool?[]`, output dictionaries) are method-local.
- `IRuleFunction` implementations are shared across concurrent evaluations and must
  be thread-safe (documented on the interface).

## Multi-targeting

Libraries target `netstandard2.0;net8.0`. Everything is written against the
netstandard2.0 API surface; C# language features used are syntax-only (no
`IsExternalInit`, no `System.Index`). The net48 leg is enforced three ways: the test
projects run on net48, the `Rulewright.Sample.NetFramework48` smoke test runs in CI,
and the adapter's netstandard2.0 build takes the System.Text.Json 8.x package only
for that target (net8.0 uses the in-box copy).
