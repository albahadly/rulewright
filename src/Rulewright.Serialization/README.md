# Rulewright.Serialization

JSON-to-domain mapping, structural schema validation, and canonical rule hashing for
[Rulewright](https://www.nuget.org/packages/Rulewright).

**No JSON library dependency** — parsing is abstracted behind `IRuleJsonReader`, which the
adapter packages implement.

## Install

```
dotnet add package Rulewright.Serialization
```

**You almost certainly want [`Rulewright`](https://www.nuget.org/packages/Rulewright)
instead** — it includes this package. Install `Rulewright.Serialization` directly when you
want to validate, hash, or inspect rule documents **without evaluating them**: a CI check on a
rules repository, a rule-authoring UI, or a schema-driven editor.

## Validate a document

`RuleSetValidator` returns every error with a JSON pointer, so an editor can put a squiggle in
the right place rather than reporting "invalid".

```csharp
using Rulewright.Serialization;
using Rulewright.Json.SystemText;

RuleJsonValue document = new SystemTextJsonReader().Read(json);
RuleSetValidationResult result = RuleSetValidator.Validate(document);

foreach (RuleValidationError error in result.Errors)
    Console.WriteLine($"{error.Path}: {error.Message}");
    // /rules/0/condition/value: 'value' must be a string for operator 'Contains'.
```

## Enumerate the authoring vocabulary

`RuleSchemaCatalog` exposes the closed vocabulary as structured metadata — condition operators
with their value kinds, logical combinators with child arity, expression operators with operand
arity, and action types — derived from the same sources the parser and validator use, so a
builder UI cannot drift from what the engine accepts.

```csharp
foreach (ConditionOperatorInfo op in RuleSchemaCatalog.ConditionOperators)
    Console.WriteLine($"{op.JsonName} takes {op.ValueKind}");
```

## Hash a rule

`RuleHasher` renders a rule's compilation-relevant content — the condition tree and actions —
in canonical form and SHA-256s it. It is insensitive to whitespace, key order, and the
`layout`, `id`, `description`, `priority`, and `enabled` members, so canvas edits and metadata
changes never invalidate a cache while any semantic change always does. This is the engine's
compiled-delegate cache key, and it works equally well as an "did this rule really change?"
check in review tooling.

```csharp
string hash = RuleHasher.ComputeHash(rule);
string canonical = RuleHasher.GetCanonicalForm(rule);   // for diagnostics
```

## Parse

`RuleSetParser.Parse` maps a validated document to the immutable `RuleSet` model, expanding
`decisionTable` documents into ordinary rules on the way.

## Requirements

.NET Framework 4.8, .NET Standard 2.0, .NET 8.0 or .NET 10.0.

## License

MIT.
