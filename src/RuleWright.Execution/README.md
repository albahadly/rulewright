# RuleWright.Execution

The [RuleWright](https://www.nuget.org/packages/RuleWright) evaluation engine: an
expression-tree rule compiler, a dynamic-fact interpreter fallback, a thread-safe compiled
delegate cache, and the `RuleWrightBuilder`/`RuleWrightEngine` public API.

## Install

```
dotnet add package RuleWright.Execution
dotnet add package RuleWright.Json.SystemText   # or RuleWright.Json.NewtonsoftJson
```

**Most users want [`RuleWright`](https://www.nuget.org/packages/RuleWright) instead** — it
includes this package, a JSON adapter, and the built-in functions in one reference. Install
`RuleWright.Execution` directly to choose your own JSON adapter and skip the function catalog.

## Use it

```csharp
using RuleWright.Core;
using RuleWright.Execution;
using RuleWright.Json.SystemText;

var engine = new RuleWrightBuilder()
    .UseJsonReader(new SystemTextJsonReader())
    .RegisterFunction("IsBusinessDay", (fieldValue, value) => /* ... */ true)
    .Build();

LoadedRuleSet ruleSet = engine.LoadRuleSet(json);   // parse + validate once

var result = engine.Evaluate(ruleSet, customerOrder, new EvaluationOptions
{
    EnableTrace = true,
    StopOnFirstMatch = false,
});
```

One `RuleWrightEngine` is thread-safe and serves concurrent evaluations. Load a rule set once
and keep it: parsing, validation, priority ordering, and content hashing all happen there, and
compilation happens lazily per fact type on first evaluation.

## Two execution paths

**Typed facts** compile to delegates via expression trees. Field paths become chained,
null-guarded member accesses; comparison constants convert to the field's exact CLR type at
compile time, so evaluations run with no reflection, no string parsing, and no boxing of
value-type comparisons. A missing member is a load-time `RuleCompilationException`, not a
runtime surprise.

**Dictionary facts** (`IDictionary<string, object>`, nested, with cached-reflection fallback
for POCOs inside) run the interpreter. Both paths share the same operator, null, and
arithmetic semantics, and the result's `CompilationMode` says which one ran — the slower path
is never silent.

## Tracing costs nothing when off

Each rule compiles two delegates from the same builder: a plain `Func<TFact, bool>` with no
tracing artifacts, and a traced variant that records each condition node's outcome. Untraced
evaluation calls the first one, so tracing is not a runtime flag check on the hot path.

```csharp
foreach (RuleTrace rule in result.Trace!.Rules)
    Console.WriteLine($"{rule.RuleId}: fired={rule.Fired} skipped={rule.Skipped}");
```

## Regex safety

`MatchesRegex` patterns run against consumer-supplied fact data, so matching is time-bounded —
one second by default, raising `RegexMatchTimeoutException` rather than letting a pattern with
catastrophic backtracking pin a thread. Change the bound with
`.UseRegexTimeout(TimeSpan.FromMilliseconds(250))`.

## Requirements

.NET Framework 4.8, .NET Standard 2.0, .NET 8.0 or .NET 10.0.

NativeAOT: the compiled path uses `Expression.Compile`, which NativeAOT does not support. Use
dictionary facts there — the engine reports `CompilationMode.Interpreted`.

## License

MIT.
