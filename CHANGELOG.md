# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and Rulewright uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). Before 1.0, breaking changes may land
in a minor version; each one is called out below.

## [0.3.0]

Additive: nothing that loaded under 0.2.0 changes meaning. Rules can now reason about collection
fields, which `In`/`NotIn` never could.

### Added

- **Collection quantifiers `Any`, `All`, `None`.** `In`/`NotIn` compare one scalar against a
  closed set; a quantifier reasons about a collection field, applying a nested `condition` to each
  element. The element condition is an ordinary condition tree, so groups, nested quantifiers,
  computed expressions and `custom` functions all work inside it. The compiled path reads the
  element type off the collection's `IEnumerable<T>` and emits a typed `Enumerable.Any`/`All`, so
  member access inside the loop stays reflection-free.
- **`"$"` names the element** a quantifier is currently testing, which is how a collection of
  scalars is tested. It is valid only inside a quantifier's `condition`, and the whole `$` prefix
  is now **reserved** — a path like `$root.Total` is rejected rather than read as a member named
  `$root`. That leaves room for correlated conditions later without a breaking change.
- **`count` expression operator**, measuring a collection through the existing computed
  left-hand side (`count(Order.Lines) > 3`) or in an action's value. Total, like every other
  expression operator: a null, a non-collection, or a string yields null.
- `ConditionLeaf.Quantifier(...)` factory and `ConditionLeaf.ElementCondition`;
  `OperatorValueKind.Condition` and `ConditionOperatorInfo.RequiresElementCondition` so an
  authoring UI knows to offer a condition editor; `ExpressionOperatorCategory.Collection`.
- [examples/20-collection-operators.json](examples/20-collection-operators.json).

### Known limits

- A quantifier's element condition sees the element, not the root fact, so correlated conditions
  ("any line whose price exceeds the order average") are not yet expressible.
- Counting only the matching elements is a follow-up; `count` measures the whole collection.
- A quantifier is one node in a trace — per-element results have no single slot — so its element
  condition is rendered into the node's description rather than traced separately.

## [0.2.0]

### Fixed

- **`In`/`NotIn` answered differently on the two execution paths when the value array contained a
  `null`.** The compiled path drops nulls when building its typed set; the interpreter compared
  them and treated a null field as a member. Both now agree that a null contributes nothing —
  matching the documented null semantics (`In` false, `NotIn` true). Reachable from JSON through a
  decision-table cell, which the validator did not screen.
- **`IsInPast` / `IsInFuture` ignored `DateTimeKind` and discarded a `DateTimeOffset`'s offset.**
  In a UTC+12 zone a timestamp five hours in the past reported as being in the future. Both the
  field and the clock now resolve to UTC first. A `DateTimeKind.Unspecified` value is read as UTC
  so results no longer depend on the server's time zone. `IsWeekend` / `IsWeekday` stay wall-clock
  by design.
- **A `ConditionNode` instance reused within one condition tree crashed the load** with
  `ArgumentException: An item with the same key has already been added`. Node bookkeeping is now
  positional rather than keyed on node identity, so an immutable node may appear at several
  positions. As a side effect, traced evaluation got faster (~6% compiled, ~11% interpreted) —
  the per-node dictionary lookup is gone.
- **`addToOutput` onto a target already holding a non-numeric value replaced it with `null`.**
  Such a target is now left untouched.
- **A document carrying both `decisionTable` and `rules` silently dropped `rules`.** It is now a
  validation error.
- **An out-of-range JSON number (`1e400`) behaved differently per target framework** — accepted on
  .NET 8/10, rejected on .NET Framework 4.8 — and escaped as a raw `ArgumentException`. It is now
  rejected identically everywhere, as the documented `RuleParseException`.
- **A malformed operand on a hand-built rule surfaced as an `InvalidCastException`** from inside
  the compiler, or mid-evaluation for dictionary facts. Operand shapes, regex patterns, and custom
  function names are now all checked at `LoadRuleSet`, raising `RuleCompilationException` with the
  rule id.
- **An `object`-typed property compared differently on the two paths** (`5` vs `5L` was unequal
  compiled, equal interpreted). The compiled path now defers to the same runtime comparison.
- **A fact whose static type is `object`, and facts implementing only
  `IReadOnlyDictionary<string, object>`, failed to evaluate.** Both now run the interpreter and
  report `CompilationMode.Interpreted`.
- **A decision-table `MatchesRegex` cell with an unparsable pattern passed validation** and then
  failed at load. It is now a validation error with a JSON pointer.

### Changed

- **The `first` decision-table hit policy is linear instead of quadratic.** It was encoded by
  ANDing each row with the negation of every earlier row — a 500-row table reached roughly a
  quarter of a million condition nodes and about seven seconds of compile time. The policy now
  rides on `RuleSet.StopAfterFirstMatch`, so a table expands to one plain rule per row.
  **Behaviour is unchanged**; only the expansion shape is. Code inspecting a `first` table's
  expanded conditions will see the row's own condition rather than a negation chain.
- **Unknown properties are now reported instead of ignored.** The vocabulary is closed, so a
  misspelled `"actons"` used to produce a rule that fired and wrote nothing. The C# validator and
  `docs/schema/rule-schema.json` both reject undefined keys now. **This can reject documents that
  previously loaded** if they carry extra keys.
- **`EvaluationOptions` is immutable.** Its properties are `init`-only, so the shared
  `EvaluationOptions.Default` can no longer be reconfigured process-wide by any caller. Object
  initializers are unaffected; **post-construction assignment no longer compiles.**
- `description` is now formally part of the schema on a rule set and a decision table. The shipped
  examples already used it.

### Added

- `RuleSkipReason` and `RuleTrace.SkipReason`, separating a rule the author disabled from one that
  evaluation never reached after a match. `RuleTrace.Skipped` is unchanged.
- `RuleSet.StopAfterFirstMatch` — the set's own first-match semantics, ORed with the caller's
  `EvaluationOptions.StopOnFirstMatch`.
- Traces describe a computed left-hand side as the expression itself
  (`divide(Order.Total, Order.ItemCount) GreaterThan 25`) rather than as `(fact)`.
- [usage.md](usage.md) — a task-oriented guide; every snippet in it is verified against the engine.
- This changelog, and [SECURITY.md](SECURITY.md).

### Testing

- The **`net10.0` leg is now tested.** The library ships four target frameworks; the suite ran only
  `net8.0` and `net48`, so a shipped leg was never executed. CI runs it too.
- Interpreter parity tests are **differential** — the same rule runs against a typed fact and an
  equivalent dictionary fact and the two answers must agree, rather than each being compared to a
  hand-written expectation.
- `ExampleFilesTests` now evaluates every `examples/` document on **both** paths; previously it
  claimed to exercise compiled delegates but only ever ran the interpreter.
- The Blazor editor's example documents are linked from `examples/` rather than duplicated, so the
  editor cannot drift from the documents the suite validates.

## [0.1.1]

Initial public packages: the core domain model, JSON parsing and schema validation, the
expression-tree evaluation engine, System.Text.Json and Newtonsoft.Json adapters, the built-in
`custom` predicate catalog, and the `Rulewright` metapackage. See the
[releases page](https://github.com/albahadly/rulewright/releases).
