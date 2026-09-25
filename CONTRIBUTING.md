# Contributing to RuleWright

Thanks for your interest! Issues, discussions, and pull requests are all welcome.

## Development setup

- Install the [.NET 10 SDK](https://dotnet.microsoft.com/download) — `global.json` pins
  `10.0.100` (rolling forward to the latest feature band). The .NET 8 runtime lets you run the
  net8.0 leg; on Windows, .NET Framework 4.8 (in-box on Windows 10/11) completes the matrix.
- Build & test:

  ```
  dotnet build RuleWright.slnx
  dotnet test  RuleWright.slnx              # net8.0 + net10.0 + net48 on Windows
  dotnet test  RuleWright.slnx -f net8.0    # Linux/macOS: run each leg — no net48 runtime there
  dotnet test  RuleWright.slnx -f net10.0
  ```

- Benchmarks (Release only):

  ```
  dotnet run -c Release --project tests/RuleWright.Benchmarks -- --filter '*Evaluation*'
  ```

## Ground rules

- **`RuleWright.Core` stays zero-dependency** and every library keeps compiling for
  `netstandard2.0`. Use only syntax-level C# features there (no `init`, no records,
  no `System.Index/Range`).
- **The JSON schema is a contract.** Changes to `docs/schema/rule-schema.json`, the
  validator, or observable evaluation semantics must update the golden-file fixtures
  (`tests/RuleWright.Execution.Tests/Fixtures`) deliberately — a fixture change is a
  reviewable, intentional contract change.
- **Compiled and interpreted paths stay in parity.** If you change operator
  semantics, change both `RuleExpressionCompiler` and `RuleInterpreter`/
  `RuntimeComparisons`, and cover both in tests.
- **Public members carry XML docs.** The build treats warnings as errors, including
  missing docs.
- New operators or actions need: schema update, validator rules, parser mapping,
  compiler + interpreter implementations, tests for both paths, README and `usage.md`
  docs, and the documentation site (below).

## The documentation site

[docs.albahadly.com/rulewright](https://docs.albahadly.com/rulewright/) is built from the
[albahadly/docs](https://github.com/albahadly/docs) repository, checked out next to this one
(`../docs`). It documents this **source**, not the last NuGet release: every C# snippet on it is
a sample compiled against `../rulewright/src` and run, and every rule document it shows is a file
a sample loaded. So a renamed member breaks its build, but a changed *behaviour* only shows when
someone re-runs the samples and reads the diff.

A change a consumer can see — a new operator, action or option, a changed API, different
results, a new error message, or a bug fix the site describes — isn't finished until the site
shows it. From `../docs`:

```
./build.ps1 sync --product rulewright                  # refresh the API reference from src/
./build.ps1 samples --run --product rulewright         # compile and run every sample
./build.ps1 --product rulewright                       # DocFX, warnings as errors
git diff                                               # read it: any diff is a behaviour change
```

The `docs-sync` skill in `.claude/skills/` has the page map and the harness's traps.

## Pull requests

1. Fork, branch from `main`, keep changes focused.
2. Add or update tests — bug fixes need a failing-before test.
3. `dotnet build` and `dotnet test` must pass with zero warnings.
4. Describe *why* in the PR body, not just what.

## Reporting bugs

Please include: the rule JSON, the fact (shape and values), expected vs actual
result, and whether the run was `Compiled` or `Interpreted`
(`result.CompilationMode`). A failing golden-file fixture makes a perfect repro.
