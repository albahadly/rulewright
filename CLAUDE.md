# RuleWright — working context

## Code navigation

Two indexes cover this tree, and they are **not interchangeable**: **CodeGraph** (`.codegraph/`, a
name-based symbol graph) and **Serena** (`.serena/`, a Roslyn/LSP language server). Use each for
what it is good at, and never grep or open files to do a job either one does.

**CodeGraph first, for exploration.** "How does X work", where a symbol lives, what calls what, the
shape of an area. One `codegraph_explore` call (or `codegraph explore "<names or question>"` in a
shell) returns the relevant symbols' source plus the call paths between them, far cheaper than a
grep-and-read loop, and it follows dynamic-dispatch hops grep cannot.

**Serena for precision, and it is the source of truth for references.** Before you **rename,
delete, move, or change any public *or internal* signature**, get the full reference list from
`find_referencing_symbols` and work from that, not from CodeGraph and not from grep.
`find_implementations` and `find_declaration` likewise beat guessing from a name.

**When the two disagree, trust Serena.** CodeGraph resolves by name: its `callers` is blind to a
*type* (nothing "calls" an interface), its `impact` is a neighbourhood (callees included), not a
rename checklist, and it conflates two types that share a name. Serena resolves them, and it skips
names that only appear in doc comments, which grep reports.

**Two Serena gotchas:**

- A bare name that collides with its own constructors errors out. Use the absolute name path,
  e.g. `/RuleWright.Execution/RuleWrightEngine`.
- For a **partial class** Serena anchors the symbol to one file and does not list the other half as
  a reference; that is correct, it is a declaration.

**Serena can be stale or incomplete; say so rather than trusting silence.** It indexes at startup,
so after new files are added its view can lag and a session restart may be needed. And Roslyn
resolves against **one** target framework, while this tree builds `netstandard2.0`, `net48`,
`net8.0` and `net10.0`: a symbol live only under another TFM can be invisible. Serena's silence is
evidence, not proof.

**Read only the files these tools point to.** `codegraph sync` picks up changes since the last
index; `codegraph status` reports staleness. **Neither index is committed**: `.codegraph/` and
`.serena/` are in `.gitignore`. Serena finds this project from the working folder
(`--project-from-cwd` in the user-level MCP config), so start the session in this repository.

## Git and CI

Work happens on **`dev`**; `main` takes changes only through a pull request (a ruleset enforces
it, and `.git/hooks/pre-push` refuses a push to `main`). A push to `dev` runs no CI: open a pull
request, or `gh workflow run ci.yml --ref dev`. `.github/workflows/RUNNERS.md` covers the
cloud/local runner switch and why a fork's pull request never runs on the local runners.
