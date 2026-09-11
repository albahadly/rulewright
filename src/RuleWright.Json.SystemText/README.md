# RuleWright.Json.SystemText

The System.Text.Json adapter for [RuleWright](https://www.nuget.org/packages/RuleWright):
implements `IRuleJsonReader` so the engine can read rule documents, plus helpers for using
JSON payloads as facts.

## Install

```
dotnet add package RuleWright.Json.SystemText
```

**Already included in [`RuleWright`](https://www.nuget.org/packages/RuleWright)** — install
this directly only if you reference `RuleWright.Execution` piecewise. Prefer
[`RuleWright.Json.NewtonsoftJson`](https://www.nuget.org/packages/RuleWright.Json.NewtonsoftJson)
if your application already standardises on Newtonsoft.Json; the two are parity-tested against
each other, and swapping adapters is the only change.

## Read rule documents

```csharp
var engine = new RuleWrightBuilder()
    .UseJsonReader(new SystemTextJsonReader())
    .Build();
```

Comments and trailing commas are tolerated, so hand-written rule files stay pleasant to edit.
Malformed JSON surfaces as `RuleParseException`.

## Use a JSON payload as a fact

`SystemTextJsonFacts.ToDictionary` converts a `JsonElement` into the dictionary shape the
engine's interpreter evaluates — useful when facts arrive as JSON over HTTP and there is no
CLR type to bind them to.

```csharp
using JsonDocument payload = JsonDocument.Parse(requestBody);
Dictionary<string, object?> fact = SystemTextJsonFacts.ToDictionary(payload.RootElement);

RuleEvaluationResult result = engine.Evaluate(ruleSet, fact);
// result.CompilationMode == CompilationMode.Interpreted
```

Nested objects become nested dictionaries and arrays become `object?[]`. Numbers become `long`
when integral, otherwise `decimal` when exactly representable, otherwise `double` — the same
policy the rule parser applies to constants, so a fact and a rule constant of the same JSON
text compare as equal.

## Requirements

.NET Framework 4.8, .NET Standard 2.0, .NET 8.0 or .NET 10.0. System.Text.Json is in-box from
.NET 8 on; the down-level legs take a package reference for it.

## License

MIT.
