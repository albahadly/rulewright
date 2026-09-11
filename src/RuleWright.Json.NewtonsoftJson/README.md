# RuleWright.Json.NewtonsoftJson

The Newtonsoft.Json adapter for [RuleWright](https://www.nuget.org/packages/RuleWright):
implements `IRuleJsonReader` so the engine can read rule documents, plus helpers for using
JSON payloads as facts.

## Install

```
dotnet add package RuleWright.Json.NewtonsoftJson
```

Use this instead of
[`RuleWright.Json.SystemText`](https://www.nuget.org/packages/RuleWright.Json.SystemText) when
your application already standardises on Newtonsoft.Json. The two adapters are parity-tested
against each other — swapping them is the only change your code needs.

Note that the [`RuleWright`](https://www.nuget.org/packages/RuleWright) metapackage bundles the
System.Text.Json adapter. To use this one, reference
[`RuleWright.Execution`](https://www.nuget.org/packages/RuleWright.Execution) plus this package.

## Read rule documents

```csharp
using RuleWright.Json.NewtonsoftJson;

var engine = new RuleWrightBuilder()
    .UseJsonReader(new NewtonsoftJsonReader())
    .Build();
```

Comments and trailing commas are tolerated. Automatic date parsing is disabled so date-like
strings stay strings, matching the System.Text.Json adapter exactly, and trailing content after
the root document is rejected the same way. Malformed JSON surfaces as `RuleParseException`.

## Use a JSON payload as a fact

```csharp
JObject payload = JObject.Parse(requestBody);
Dictionary<string, object?> fact = NewtonsoftJsonFacts.ToDictionary(payload);

RuleEvaluationResult result = engine.Evaluate(ruleSet, fact);
// result.CompilationMode == CompilationMode.Interpreted
```

Numbers follow the same policy as the other adapter — `long` when integral, otherwise `decimal`
when exactly representable, otherwise `double` — so the two adapters produce identical facts
from identical JSON.

## Requirements

.NET Framework 4.8, .NET Standard 2.0, .NET 8.0 or .NET 10.0. Depends on Newtonsoft.Json 13.

## License

MIT.
