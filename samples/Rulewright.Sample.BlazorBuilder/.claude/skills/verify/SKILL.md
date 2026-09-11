---
name: verify
description: How to build, launch, and drive the Rulewright drag-and-drop flow builder (custom canvas, dark theme) in a real browser.
---

# Verifying Rulewright.Sample.BlazorBuilder

Blazor WebAssembly. As of the current design, the canvas is a **hand-rolled** node-graph editor
(no third-party library — earlier Drawflow-based iteration was fully replaced), styled to match
a supplied dark-theme mockup (copper/teal accents, JetBrains Mono + IBM Plex Sans). Being a
static WASM site, curl only proves the shell serves, not that anything rendered — always drive
it in a real browser.

## Launch

```bash
cd samples/Rulewright.Sample.BlazorBuilder
dotnet run --urls http://127.0.0.1:5312
```

Same Windows file-lock gotcha as the other samples: kill any leftover `dotnet run` via
`Get-NetTCPConnection -LocalPort 5312` / `Stop-Process -Force` before rebuilding.

## Architecture (matters for debugging)

The canvas/palette/wires/inspector/drawer/modal are **entirely JS-owned state**
(`wwwroot/js/rule-canvas.js`, a single IIFE exposing `window.rulewrightFlowBuilder.init(dotNetRef)`)
— there is no C# node/draft model (the retired form/tree builder had one; this does not).
`Pages/Canvas.razor` is just the page
shell (static HTML matching the mockup) plus two `[JSInvokable]` bridge methods:
`ValidateRule(ruleJson)` and `EvaluateRule(ruleJson, factJson)`, both taking/returning JSON
strings, backed by the real `RulewrightEngine` (`Services/EngineService.cs`, unchanged from
before). JS builds the rule JSON client-side from its own node/connection graph
(`buildConditionTree`/`buildRuleJson` in rule-canvas.js) and calls these via
`dotNetRef.invokeMethodAsync(...)`.

**Trace-highlight mechanism**: `buildConditionTree` returns both the JSON condition tree AND a
parallel "id tree" of the same shape (node id in place of the JSON body). Since
`Rulewright.Execution.ConditionTraceBuilder` produces a `ConditionTraceNode` tree that mirrors
the parsed condition 1:1 (same order, same nesting), `applyTraceHighlight` zips the id tree and
the trace tree positionally — no string-matching heuristics. If node/wire pass-fail highlighting
ever looks wrong after a change to condition-building order, check that `buildConditionTree`'s
recursion order still exactly matches what gets sent to the engine.

## Known-good smoke flow (verified working, zero console errors)

1. Load `/`, wait for `#canvasWrap`. A seeded demo graph renders: Fact Input → Compare
   (`Customer.Age > 18`) → Set Output (`Discount = 10`), connected with orthogonal wires.
2. `#btnExport` → opens the bottom drawer's JSON tab; `#jsonOut` shows real rule-schema JSON
   (`{id, description, priority, enabled, condition, actions}`) — matches Rulewright's actual
   schema, not a mockup approximation.
3. `#btnValidate` → calls the real `RuleSetValidator` via the JSInvokable bridge; the Validation
   tab and a toast both reflect the real result.
4. Click a node (`.node`) → right-side Node Inspector shows its fields (Compare: field/operator/
   value; Set Output: target/value); editing them (`input`/`change` events) live-updates the
   node's on-canvas summary and the JSON tab.
5. Double-click a palette item (`.palette-item[data-type="..."]`) → drops a new node near canvas
   center; drag-and-drop from the palette onto the canvas works too (`dragstart`/`drop`).
6. `#btnTest` → modal pre-filled with the current sample fact JSON; `#testRun` calls the real
   `EvaluateRule` bridge. Verified both outcomes: a fact that satisfies `Customer.Age > 18`
   shows a green "✓ Rule fired — Discount=10" banner, green node/wire highlighting, and a Trace
   tab entry `Customer.Age GreaterThan 18 — passed`; a failing fact shows the red "✗ Rule did not
   fire" banner instead.
7. Selecting a node and pressing `Delete` removes it (and its connections); dragging from a
   node's output port (`.port-out`) to another node's input port (`.port-in`) wires them;
   clicking a wire selects it (delete-affordance `×` glyph at the wire's midpoint, or `Delete`
   key) removes it.

All of the above passed clean (zero console errors) in the current design.

## Examples picker + full schema coverage (added later, same design)

The toolbar's `#exampleSelect` dropdown (populated from `wwwroot/examples/manifest.json`, the
same 19 files as the repo's `examples/`) fetches an example, then `importRuleJson(rule)` in rule-canvas.js clears the
canvas and rebuilds it as nodes: condition tree (Compare/Custom Function/AND/OR/NOT), all four
action types with a Then/Else branch each, and — when present — computed value-expressions
(`literal`/`field`/`op`) as chained `Literal`/`Field Ref`/`Expression` nodes wired into a
Compare's **Field (expr)** pin (the ONLY place a condition allows a computed value — its
right-hand comparison `value` must stay a constant, `RuleSetValidator` rejects an object there)
or an Action's **Value (expr)** pin. `rules`-set docs import only rule #1 with a toast noting
`N` total; `decisionTable` docs show a toast and leave the canvas untouched (not supported).
**Both clauses are superseded** — see the multi-rule section (every rule of a set is imported)
and the net10.0 section (decision tables are expanded through the engine and rendered).

**Two real bugs found via this import path, both fixed** — worth re-checking after any change
to `importRuleJson`/`importValueExpr`/`importCondition`:
1. **Double-quoted string literals**: import populated a text field with `JSON.stringify(value)`,
   but `parseValue()` (the field's own reader) treats bare unquoted text as a string — so a
   string got wrapped in quotes that then round-tripped as `""gold""`. Fixed via a
   `valueToFieldText()` helper that only JSON-stringifies non-string values.
2. **Extra unwired operand/input slots** (e.g. 3 real operands rendering as 6 slots): import
   pre-grew `node.inputs` to the target length before calling `addConnection()` N times, but
   `addConnection()` *also* auto-appends a fresh empty slot on every call for a dynamic-input
   node (that's how manual click-to-wire keeps exactly one trailing empty slot) — the two growth
   mechanisms compounded. Fixed by removing the pre-growth entirely; sequential
   `addConnection(child, node, i)` calls for `i = 0, 1, 2, ...` naturally reproduce the correct
   "N filled + 1 trailing empty" invariant on their own. (After the fix, a node with 3 wired
   operands correctly shows 4 port rows — 3 filled + 1 empty invitation slot — not 3; that's
   correct, not a regression.)

Verified via Playwright: all 19 examples import and validate clean (zero console errors);
`07-computed-values.json`'s two `concat`/`multiply` expressions round-trip and evaluate for
real (`DiscountAmount=20`, `Message="Thanks Ada, you saved 10%!"` — confirms both the operand-
count fix and a related whitespace-preservation fix in `parseValue`, which previously trimmed
meaningful trailing spaces like `"Thanks "` out of string constants); `12-custom-function.json`
round-trips its `addToOutput`/`appendToOutput` actions and AND-grouped custom-function leaf
correctly; `08-arithmetic-operators.json` (26 nodes) loads without crashing; changing an
existing action's type via the Inspector's new Type dropdown (e.g. `setOutput` →
`addToOutput`) updates the exported JSON immediately.

## Review pass: bug fixes + New/Import/Download/Fit + connection typing (later session)

A review-and-fix pass on top of the above. All in `rule-canvas.js` + `Canvas.razor` (no engine
change). Two real bugs, four UI features; a 20-check Playwright suite (`scratchpad/verify.js`)
confirmed everything at 20/20 with zero console errors.

**Bugs fixed:**
1. **Test-rule error path crashed the page.** `runTest`'s `!response.ok` branch called
   `openDrawer('validation')`, but the drawer tab is named `warnings` (`panelWarnings`) — so
   `switchDrawerTab` did `getElementById('panelValidation').classList.add(...)` on `null` and
   threw. Now opens `'warnings'` and also renders the engine's error message into `#warnList`.
   Repro: import a rule with an invalid regex (`MatchesRegex` value `"("`) and Test it — the
   engine throws, the fail banner + Validation tab now render cleanly instead of a page error.
2. **String leaf comparison values double-quoted on import.** `importCondition` set the leaf's
   value field via `JSON.stringify(cond.value)` (unlike actions/literals, which use
   `valueToFieldText`), so a string like `"@acme.com"` went into the field WITH quotes and
   re-exported as `"\"@acme.com\""`. Same class as the earlier action/literal bug (see above);
   fixed by switching that line to `valueToFieldText(cond.value)`. Re-check example 03 round-trip
   if `importCondition` is touched again.

**Features added:**
- **Connection type validation** (`nodeOutputKind`/`inputPortKind` + a guard in `addConnection`):
  wiring a value node (Literal/Field Ref/Expression) into a condition pin — or a condition into a
  value/expr pin — is now rejected up front with a toast instead of silently dropped at build
  time. The **Fact Input (trigger) node lost its output port** (`noOutput:true`) since it's
  reference-only and every wire from it was invalid anyway.
- **Fit-to-view** (`fitToView`): the toolbar's ⤢ button (was a fixed 100%/60,60 reset) now frames
  every node; import calls it automatically so imports of deep trees (which lay children out to
  negative X) are fully on-screen.
- **New** (`#btnNew` → `newCanvas`), **Import JSON** (`#btnImport` → `#importModal`),
  **Download** (`#btnDownloadJson`).

## Multi-rule authoring + professional pass (later session — CURRENT design)

`rule-canvas.js` was substantially rewritten to build a whole **rule set** on one canvas, plus a
UI polish pass. Model change: a first-class **Rule anchor node** (`type:'rule'`, gold accent).

**How a rule is expressed now** (was: one implicit rule = all actions + their shared condition):
- The condition tree's root output wires into the Rule node's **Condition** input pin.
- The Rule node's **output** fans out to one or more **Action** nodes — so an action's input[0]
  pin is now **"Rule"** (kind `'rule'`), NOT the condition root. Each action still carries its own
  Then/Else branch dropdown.
- `buildRuleSet()` iterates every Rule node: condition = `buildConditionTree(condInput.from)`;
  actions = `actionsOf(ruleNode)` split by branch. **1 rule → bare rule object** (back-compat,
  single-rule examples round-trip); **2+ rules → `{ name, rules:[...] }`** (name from
  `#ruleSetName`, rules sorted priority-desc). Warnings for: no condition wired, no actions
  attached, actions not attached to any rule, duplicate rule ids.
- Connection kinds extended with `'rule'`: Rule.Condition expects `'condition'`; Action.input0
  expects `'rule'`; Rule output emits `'rule'`. So a value→Rule-pin or rule→group-pin wire is
  rejected with a toast (verified).

**Multi-rule Test/trace** — the C# `EvaluateRule` bridge (`Pages/Canvas.razor`) now returns a
per-rule array: `{ ruleId, conditionPassed, skipped, firedBranch, trace }` keyed by rule id
(`RuleTrace.RuleId` + `FiredRule.RuleId`/`.Branch` make this robust — no positional matching
across rules). JS `runTest` zips each rule's `idTree` against its `trace` by id, highlights that
rule's condition nodes + Then/Else action nodes, sets `ruleNode._fired`, and the Trace panel shows
one section per rule (`.trace-rule`). Banner reads "N of M rules fired — <merged outputs>".

**Import/seed/tidy** — `importDocument(doc)` rebuilds ALL rules of a set (`importRule` per rule:
Rule node + condition subtree + action nodes, all wired). Seed graph is a **2-rule** set. **Tidy**
(`#btnTidy` → `layoutAll`) repositions the live graph into tidy per-rule bands by walking
connections (`placeUpstream` follows inputs but stops at rule-kind sources); import calls it too.
Snap-to-grid (12px) on node drop/drag-end. Wires are now **rounded orthogonal** (`orthPoints` +
`roundedPath`).

**Right panel** — the old fixed "Rule settings" (`#ruleId`/`#ruleDescription`/… — REMOVED) became
a **Rule set** section: `#ruleSetName` + **+ Add rule** (`#btnAddRule` → `addRuleNode`) + a
`#rulesList` of chips (one per rule: enabled dot, id, priority, fired/skip badge; click focuses
the Rule node, × runs `removeRuleCluster` which deletes the rule + its exclusive upstream/action
nodes via `ruleClusterIds` fixpoint). Per-rule id/description/priority/enabled now live in the
**Rule node's** inspector.

Verified via `scratchpad/verify2.js` — **23/23 checks, zero console errors**: 2-rule seed exports
`{name,rules[]}`; Test fires 2/2 then 1/2 with correct merged outputs + per-rule trace + fired
chips; Add rule; import `02-rule-set-priority.json` rebuilds all 3 rules; `03` string values
round-trip un-doubled; Tidy; all connection-kind rejections; single-rule doc still exports bare.
Full solution builds 0-warning Release; 376 tests/TFM unaffected (no library code changed).

## net10.0, local examples, and a height-aware Tidy (later session — CURRENT)

Three changes, all verified in a real browser (Playwright, all 20 examples, zero console errors).

**The project targets `net10.0`** (was net8.0; the libraries already shipped a net10.0 leg).
`Directory.Packages.props` moves the two `Microsoft.AspNetCore.Components.WebAssembly*` pins to
`10.0.10` to match, and `blazor-builder-pages.yml` now installs the 10.0.x SDK — it installed only
8.0.x, which cannot build this project.

**The examples are real files in `wwwroot/examples/` again.** A previous pass replaced them with
`<Content Include="..\..\examples\*.json" Link="wwwroot\examples\..." />` to keep one copy in the
repo. That silently broke the picker: the SDK's discovery pass (`DefineStaticWebAssets`, in
`Microsoft.NET.Sdk.StaticWebAssets.targets`) matches candidates by their `Link`/`TargetPath` but
stamps **every** asset it finds in `@(Content)` with `$(MSBuildProjectDirectory)\wwwroot\` as the
ContentRoot. So `staticwebassets.runtime.json` mapped `examples/<file>.json` to
`wwwroot\examples\<file>.json`, which did not exist, and every example 404'd — while
`manifest.json` (a genuine file in wwwroot) still resolved, so the dropdown filled normally and
only the *loading* failed. **A Blazor WASM app can only serve what physically lives under its own
wwwroot; do not try to link files in from elsewhere.**

The copy is kept honest by `tests/Rulewright.Execution.Tests/BlazorBuilderExamplesTests.cs`:
every `examples/*.json` must have a byte-identical (newline-normalised) counterpart here, the two
folders must hold exactly the same file names, and `manifest.json` must list exactly them. **Adding
an example means copying it here and adding a manifest entry, or the suite fails.**

**Tidy (`layoutAll`/`placeUpstream`) is height- and depth-aware.** It used a fixed `ROW_H = 140`
slot per node and fixed columns, so (a) any node taller than one slot — a group or Expression with
several operand rows — was overlapped by its next sibling, and (b) chained value expressions
(`Expression <- Field Ref`), laid out leftward from `RULE_X + COL_W`, landed exactly on the Rule
column. Now siblings stack by `nodeHeight()` + `ROW_GAP`, a parent is centred against the block its
children occupy, and `RULE_X`/`ACTION_X` are derived from the deepest condition/value tree on the
canvas (`upstreamDepth`) so trees can never grow back into the Fact Input or Rule columns. Nothing
lays out to negative X any more.

Regression check worth repeating after any layout change — measure overlap in world coordinates,
reading each node's `translate(x, y)` (nodes are positioned by `transform`, **not** `left`/`top` —
reading `style.left` silently yields `NaN` and a meaningless all-clear):

```js
const m = /translate\(([-\d.]+)px,\s*([-\d.]+)px\)/.exec(el.style.transform);
```

Across the seed graph and all 20 examples: **21 overlapping node pairs before, 0 after.**
