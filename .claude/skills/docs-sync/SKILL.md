---
name: docs-sync
description: Keep the RuleWright documentation site (../docs, docs.albahadly.com/rulewright) in step with the library. Use after ANY RuleWright change that adds or alters an operator, action, expression operator, option, public API, error message or evaluation result, or that fixes a bug the site describes. Covers the sync / samples / build loop, which page documents what, and the traps the sample harness has.
---

# Keeping the docs site current

The site at `../docs` (its own repository, `albahadly/docs`, deployed to docs.albahadly.com by
Cloudflare) documents RuleWright's **local source**, not the last NuGet release. It sits beside
`README.md` and `usage.md` as a consumer surface, and it drifts the same way: silently. Every C#
snippet on it is a `#region` in `products/rulewright/samples/` that compiles against
`../rulewright/src` and runs, and every rule document a page shows is a file in
`samples/assets/` that a sample loaded. A renamed member breaks the site build. A changed
*behaviour* only shows if someone re-runs the samples and reads the diff.

**Rule: a RuleWright change that a consumer can see is not finished until the site shows it.**
Do it in the same sitting as the code, the way `usage.md` is done.

## The loop (run from `../docs`)

```powershell
./build.ps1 sync --product rulewright              # builds ../rulewright, refreshes products/rulewright/lib (API reference)
./build.ps1 samples --run --product rulewright     # compiles + runs every sample; refreshes console output and downloads
./build.ps1 --product rulewright                   # DocFX, warnings as errors: broken links, bookmarks and xrefs fail
git diff --stat                                    # read it
```

`--filter <text>` runs only the samples whose id contains the text. Use it while iterating, then
do one full run before committing. The samples are deterministic: a rerun of unchanged code gives
**zero** diff, so any diff in `console/` or `downloads/` is a change in RuleWright's behaviour and
has to be explained — either the change you meant, or a regression the site just caught.

## Where things are documented

| Change | Page(s) under `products/rulewright/` | Sample |
| --- | --- | --- |
| Condition operator, null semantics, field paths | `guides/authoring/conditions.md`, `reference/document-format.md` | `Guides/AuthoringSamples.cs` |
| Action type, output merging | `guides/authoring/actions.md` | same |
| Expression operator | `guides/authoring/computed-values.md`, `reference/document-format.md` | same |
| Quantifiers, `$`, `count` | `guides/authoring/collections.md` | same |
| Rule-set keys, priority, `else`, stop options | `guides/authoring/rule-sets.md` | same |
| Decision tables | `guides/authoring/decision-tables.md` | same |
| Built-in or custom functions | `guides/extending/custom-functions.md` | `Guides/ExtendingSamples.cs` |
| JSON adapters, fact helpers | `guides/extending/json-adapters.md` | same |
| Domain model / C# construction | `guides/extending/rules-in-code.md` | same |
| Validation messages and checks | `guides/operating/validation.md` | `Guides/OperatingSamples.cs` |
| Tracing | `guides/operating/tracing.md` | same |
| `RuleSchemaCatalog`, `FunctionCatalog` | `guides/operating/vocabulary.md` | same |
| Regex timeout, security properties | `guides/operating/security.md`, `reference/security-policy.md` | same |
| Exceptions and where they surface | `get-started/handling-errors.md` | `GetStarted/HandlingErrors.cs` |
| Number/string/date comparison | `concepts/values.md` | `Concepts/SemanticsSamples.cs` |
| Caching, hashing, threading, benchmarks | `concepts/architecture.md`, `concepts/performance.md` | `Concepts/CachingSamples.cs` |
| A new example in `examples/` | `reference/examples.md` (add a row) | `Reference/ExamplesSample.cs` picks it up |
| A release | `reference/release-notes.md`, `version` in `products.json` | — |

A **new capability** gets its own guide page, a sample, a row in `guides/toc.yml` and a card in
`guides/index.md`. If it changes what a document can say, update `reference/document-format.md`.

## The harness

- A sample is a static method with `[Sample("area/name", Inputs = ["rules/x.json"])]`. It runs in
  `obj/samples-output/rulewright/<id>/` with its inputs copied from `samples/assets/`.
- `ctx.Check(cond, what)` fails the run. `ctx.Publish(file, folder)` copies to `downloads/`.
  What the sample prints goes to `console/<id with / as ->.txt`.
- `#region name (usings: A, B)` becomes `snippets/<Dir>/<File>.name.cs`. Region names must be
  unique **per file**: two `#region snippet` blocks in one file overwrite each other.
- Pages include code with `[!code-csharp[](../../snippets/...)]`, rule documents with
  `[!code-json[](../../samples/assets/rules/x.json)]`, output with `[!code-text[](../../console/...)]`.
- The examples page and the JSON Schema download come straight from `../rulewright/examples/`
  and `docs/schema/` at build time: nothing to copy by hand.

## Traps met so far

- **A sample passing is not the page being right.** Read the output against what the page claims,
  and add a `ctx.Check` for the claim. Statements with no sample of their own, and hand-written
  JSON fragments in page prose, are pinned in `samples/Reference/PageClaims.cs`: add a line there
  whenever a page states a behaviour. Writing it caught four wrong statements before they shipped.
- **Dictionary facts match keys through their comparer, exact by default.** `PostAsJsonAsync`
  writes camelCase; the web API sample silently evaluated an empty order until its checks
  demanded the real outputs. The JSON fact helpers now take a key comparer, and the tutorial's
  server uses `StringComparer.OrdinalIgnoreCase`: keep a check on the real outputs anyway.
- **Compilation is lazy per rule.** A warm-up `Evaluate` compiles only the rules it reaches.
- **Keep JSON leaves on one line.** Rule documents in `samples/assets/` are formatted with
  containers expanded and leaves inline, so they read well at the site's code width.

## When it's done

- `samples --run`: all passing. The site build: `0 warning(s)`, `0 error(s)`.
- `git diff` read and explained. Commit in the docs repository and push when asked.
