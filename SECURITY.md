# Security Policy

## Supported versions

Rulewright is pre-1.0. Fixes land on the latest released minor version; there are no long-term
support branches yet.

| Version | Supported |
|---|---|
| Latest release | Yes |
| Anything older | No — please upgrade |

## Reporting a vulnerability

Report privately, not in a public issue: open a
[security advisory](https://github.com/albahadly/rulewright/security/advisories/new) on the
repository.

Please include the affected version and target framework, a rule document and fact that reproduce
the problem, and what you observed versus what you expected. You can expect an acknowledgement
within a week, and an assessment with a fix or a rejection with reasons after that.

## Threat model

Rulewright's central assumption is that **a rule document is code-adjacent input**. Rules decide
business outcomes, so treat authorship as a privileged operation: review rule changes the way you
review code, and do not load documents from untrusted parties without review.

Within that assumption, these properties are deliberate and a break in any of them is a
vulnerability worth reporting:

- **Rules are pure data.** The schema is a closed vocabulary — operators, field paths, and
  literals. Rulewright never compiles, evaluates, or otherwise executes a string from a document.
  A rule cannot call arbitrary code; the only extension point is a `custom` function that the
  *host application* registered on the builder.
- **Evaluation is total.** It does not throw on data. A null field, a missing key, a non-numeric
  operand, or division by zero each have defined semantics, so a hostile *fact* cannot turn an
  evaluation into an exception path.
- **Regular expressions are time-bounded.** `MatchesRegex` patterns come from the rule author but
  run against consumer data, so matching is capped (one second by default, configurable with
  `RulewrightBuilder.UseRegexTimeout`) and raises `RegexMatchTimeoutException` rather than letting
  catastrophic backtracking pin a thread.
- **Document nesting is bounded** by the JSON reader's depth limit (64 by default in both the
  System.Text.Json and Newtonsoft.Json adapters), so a deeply nested document cannot exhaust the
  stack during parsing.
- **Numbers must be finite.** An out-of-range literal is rejected at parse time, identically on
  every target framework.
- **Evaluation is stateless and thread-safe.** One engine serves concurrent evaluations; a fact
  never leaks into another evaluation.

## Known limits, by design

These are documented behaviour rather than defects — but know them before you widen who may author
rules:

- **The compiled-delegate cache is unbounded.** It is keyed by fact type and rule content hash and
  lives for the engine's lifetime. A process that loads an unbounded number of *distinct* rule
  documents grows with them. Rebuild the engine periodically if rules are hot-reloaded at scale.
- **Compilation cost scales with document size.** A very large rule set costs time and memory on
  first evaluation per fact type. Bound the size of documents you accept.
- **A `custom` function is host code.** Rulewright calls whatever you registered; its safety,
  thread-safety, and running time are yours.
- **Field paths read your fact object.** A rule can read any public property or field reachable
  from the fact you pass, including ones you did not intend to expose. Pass a purpose-built
  projection rather than a domain object with sensitive members hanging off it.
