# Benchmarks

The benchmark suite lives in `tests/RuleWright.Benchmarks` (BenchmarkDotNet) and covers three
things: RuleWright's own warm-path throughput, its cold-start cost, and a like-for-like
comparison against **NRules** and **Microsoft RulesEngine**.

```
# RuleWright throughput and cold start
dotnet run -c Release --project tests/RuleWright.Benchmarks -- --filter '*EvaluationBenchmarks*'
dotnet run -c Release --project tests/RuleWright.Benchmarks -- --filter '*ColdStartBenchmarks*'

# Comparison against NRules and Microsoft RulesEngine
dotnet run -c Release --project tests/RuleWright.Benchmarks -- --filter '*EngineComparison*'

# Confirm the three engines agree on which rules match (fairness check)
dotnet run -c Release --project tests/RuleWright.Benchmarks -- verify
```

> The numbers below were captured with `--job short` (3 warmup + 3 measured iterations) on the
> machine noted under each table. They are **illustrative, not a spec** — absolute times depend
> on hardware, and short jobs have wider error bars than a full run. Re-run the suite on your own
> hardware for numbers you can hold RuleWright to; the **ratios** are the portable part.

## RuleWright throughput (warm)

Compiled delegates (typed facts) vs the interpreter (dictionary facts), with and without
tracing, evaluated once per call against a pre-loaded, pre-warmed rule set.

*BenchmarkDotNet v0.15.8 · .NET 8.0.29 · 13th Gen Intel Core i9-13980HX · Windows 11*

| Method | Rules | Mean | Allocated |
|---|--:|--:|--:|
| Compiled (typed) | 1 | 123 ns | 680 B |
| Compiled (typed), traced | 1 | 376 ns | 1,384 B |
| Interpreted (dictionary) | 1 | 238 ns | 960 B |
| Compiled (typed) | 100 | 9.8 µs | 14.2 KB |
| Compiled (typed), traced | 100 | 34.0 µs | 75.2 KB |
| Interpreted (dictionary) | 100 | 18.0 µs | 37.1 KB |
| Compiled (typed) | 10,000 | 813 µs | 863 KB |
| Compiled (typed), traced | 10,000 | 4.77 ms | 6.79 MB |
| Interpreted (dictionary) | 10,000 | 1.50 ms | 2.49 MB |

Compiled evaluation scales roughly linearly (~100 ns/rule). The dictionary interpreter runs
about **1.8–1.9× slower** than compiled — the deliberate, *visible* cost of a fact with no
compile-time shape. Tracing is a separate compiled delegate, so enabling it costs **~3× and only
when you ask for it** — an untraced evaluation pays nothing (see the throughput table's untraced
rows).

## Cold start

The one-time cost of a fresh engine, against the warm cached call as baseline.

| Method | Rules | Mean | Allocated |
|---|--:|--:|--:|
| `LoadRuleSet` (parse + validate + hash) | 1 | 7.1 µs | 18 KB |
| Load + compile + first evaluate | 1 | 956 µs | 65 KB |
| Warm cached evaluate | 1 | 123 ns | 680 B |
| `LoadRuleSet` (parse + validate + hash) | 100 | 637 µs | 1.43 MB |
| Load + compile + first evaluate | 100 | 91 ms | 6.1 MB |
| Warm cached evaluate | 100 | 9.7 µs | 14.2 KB |

Compilation to expression-tree delegates is the dominant first-call cost (and its measurement is
noisy — it is JIT-bound). It happens **once per rule content hash** and is then cached, so steady
state is the warm row. Load a rule set at startup, or accept a slow first request per rule.

## Comparison: RuleWright vs NRules vs Microsoft RulesEngine

The same logical rule set — `Customer.Age > threshold AND (Order.Total >= 100 OR Customer.IsVip)`,
one rule per threshold — is built in all three engines and evaluated against the **same typed
fact**. Every engine is fully built/compiled once during setup; only per-fact evaluation is timed.
A `verify` mode asserts the three engines flag the **same rules as matching** (10 of 10, then 90
of 100), so this measures the cost of the *same decision*, not different amounts of work.

*BenchmarkDotNet v0.15.8 · .NET 8.0.29 · 13th Gen Intel Core i9-13980HX · Windows 11*

| Engine | Rules | Mean | vs RuleWright | Allocated | vs RuleWright |
|---|--:|--:|--:|--:|--:|
| **RuleWright** (compiled) | 10 | **890 ns** | 1.0× | **2.0 KB** | 1.0× |
| Microsoft RulesEngine | 10 | 1.95 µs | 2.2× | 7.3 KB | 3.6× |
| NRules (Rete) | 10 | 7.02 µs | 7.9× | 32.4 KB | 16× |
| **RuleWright** (compiled) | 100 | **9.5 µs** | 1.0× | **14.2 KB** | 1.0× |
| Microsoft RulesEngine | 100 | 15.1 µs | 1.6× | 63.1 KB | 4.5× |
| NRules (Rete) | 100 | 66.9 µs | 7.0× | 289 KB | 20× |

For this **stateless, one-shot** pattern — "evaluate this fact against these rules, right now" —
RuleWright is the fastest and by far the leanest allocator. Why the others differ, stated fairly:

- **NRules** is a **Rete inference engine**, built for a *long-lived working memory* where facts
  are inserted and updated incrementally and rules chain off each other's conclusions. That is a
  genuinely different problem, and NRules is the right tool for it. The cost here is the Rete
  session lifecycle (create session → insert fact → fire) paid per evaluation, which one-shot
  request handling does not amortize. If your workload is incremental inference, benchmark that
  shape — these numbers do not describe NRules' strength.
- **Microsoft RulesEngine** compiles **C# expression *strings*** embedded in the rule JSON. It is
  competitive on speed but allocates several times more, and the model means arbitrary code in
  rule files plus runtime string compilation — the trade-off RuleWright's closed, pure-data
  vocabulary deliberately avoids (see the README's comparison table).

The methodology is in `ComparisonBenchmarks.cs`; the `verify` command lets you confirm the
fairness claim yourself before trusting a single timing.

## A note on regression gating

A CI regression gate — failing the build when a benchmark regresses beyond a threshold — is a
tracked roadmap item, not yet wired up. Until then, treat these as a reproducible baseline you
can re-run per release rather than an automatically enforced budget.
