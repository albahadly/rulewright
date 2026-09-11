# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and RuleWright uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). Before 1.0, breaking changes may land
in a minor version; each one is called out below.

## [0.3.0]

The first release since 0.1.1 — it carries everything in 0.2.0 below as well, which was never
published. **The project is now spelled `RuleWright`**, which renames every package, assembly and
namespace; beyond that it is additive relative to 0.2.0, in that rules can now reason about
collection fields, which `In`/`NotIn` never could. Coming from 0.1.1, read 0.2.0's **Changed**
section too.

### Changed

- **`Rulewright` is now `RuleWright` everywhere** — package ids (`RuleWright`, `RuleWright.Core`,
  `RuleWright.Serialization`, `RuleWright.Execution`, `RuleWright.Json.SystemText`,
  `RuleWright.Json.NewtonsoftJson`, `RuleWright.Extensions.Functions`), assembly names, and every
  namespace. **This is a source-breaking change**: `using Rulewright.Core;` becomes
  `using RuleWright.Core;`, and because the assembly names changed too, a consumer must recompile
  rather than drop the new assemblies in place. Package ids are case-insensitive on nuget.org, so
  an existing `<PackageReference Include="Rulewright.Core" />` still resolves; the code inside it
  does not. Rule documents are untouched — no JSON written for an earlier version needs editing.

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
- **`stopAfterFirstMatch` on a rule-set document.** `RuleSet.StopAfterFirstMatch` arrived in 0.2.0,
  but only a `first` decision table or a C# caller could set it; a JSON rule set can now state it
  itself (`{ "name": "Shipping", "stopAfterFirstMatch": true, "rules": [ ... ] }`). It is ORed with
  the caller's `EvaluationOptions.StopOnFirstMatch`, the Blazor builder offers it as a toggle, and
  [examples/21-stop-after-first-match.json](examples/21-stop-after-first-match.json) shows it.

### Fixed

- **`count` was traced as `coalesce`.** `ConditionDescriber`'s expression-operator switch ended in
  a catch-all arm returning `"coalesce"`, so the operator added after it described itself under the
  previous one's name (`coalesce(Order.Lines) GreaterThan 3`). Evaluation was never affected, only
  traces and rule descriptions. Every operator is now named explicitly, and an unrecognised one
  falls back to its own name rather than to its predecessor's.
- **The published JSON Schema rejected `stopAfterFirstMatch`.**
  [docs/schema/rule-schema.json](docs/schema/rule-schema.json) closes the rule-set vocabulary with
  `additionalProperties: false` and had not learned the property, so a document the engine accepted
  — the shipped example included — failed schema-based validation in an editor or a CI job.

### Testing

- The JSON Schema and `RuleSetValidator` are held against each other now: every schema definition
  that closes its vocabulary must name exactly the properties of the validator's matching list, and
  a list or definition added to either side fails the suite until it is mapped to its counterpart.
  They are two hand-maintained copies of one vocabulary and nothing compared them, which is how the
  schema came to miss `stopAfterFirstMatch` while every example still loaded.

### Build

- Dropped the `Microsoft.SourceLink.GitHub` package reference. Source Link ships inside the .NET
  SDK from .NET 8 on, so the reference was redundant — and its transitive
  `Microsoft.Build.Tasks.Git` 8.0.0 picked up advisory GHSA-23fw-v26w-5fgq, which failed CI
  restore under `TreatWarningsAsErrors`. The vulnerable dependency is gone rather than suppressed,
  and the shipped symbols are unchanged: the packaged PDBs still carry a Source Link blob pointing
  at the repository and commit, with every document path deterministically mapped.

### Known limits

- A quantifier's element condition sees the element, not the root fact, so correlated conditions
  ("any line whose price exceeds the order average") are not yet expressible.
- Counting only the matching elements is a follow-up; `count` measures the whole collection.
- A quantifier is one node in a trace — per-element results have no single slot — so its element
  condition is rendered into the node's description rather than traced separately.
- Expression objects are the one place the schema and the engine still disagree, in the opposite
  direction: the schema closes them with `additionalProperties: false`, while the C# validator
  discriminates on `op`/`field`/`literal` and ignores an undefined sibling key. Closing it in C#
  would reject documents that load today, so it is left for a deliberate change.

## [0.2.0]

**Never published to nuget.org.** It exists as a tagged state in this repository only; everything
below shipped in 0.3.0, which went straight from 0.1.1. If you are looking for one of these fixes
on the feed, take 0.3.0.

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
`custom` predicate catalog, and the `RuleWright` metapackage. See the
[releases page](https://github.com/albahadly/rulewright/releases).
