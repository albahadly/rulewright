# RuleWright.Extensions.Functions

A curated catalog of ready-made predicates for [RuleWright](https://www.nuget.org/packages/RuleWright)'s
`custom` operator, plus the helpers that register them.

## Install

```
dotnet add package RuleWright.Extensions.Functions
```

**Already included in [`RuleWright`](https://www.nuget.org/packages/RuleWright)** — install
this directly only if you reference `RuleWright.Execution` piecewise.

## Register them

```csharp
var engine = new RuleWrightBuilder()
    .UseJsonReader(new SystemTextJsonReader())
    .RegisterBuiltInFunctions()                       // the catalog below
    .RegisterFunctionsFrom(typeof(Program).Assembly)  // scan your own IRuleFunction classes
    .Build();
```

Then name one in a rule leaf:

```json
{ "field": "Customer.Email", "operator": "custom", "name": "IsEmail" }
{ "field": "Customer.Age",   "operator": "custom", "name": "IsBetweenInclusive", "value": [18, 65] }
```

## The catalog

Deliberately, these are the things RuleWright's closed operator vocabulary leaves out.

| Function | Checks |
|---|---|
| `IsNullOrEmpty` / `IsNullOrWhiteSpace` | field is null, empty, or whitespace |
| `EqualsIgnoreCase` | field equals the value, ignoring case (ordinal) |
| `IsEmail` | field looks like an email address |
| `IsEven` / `IsOdd` | field is an even / odd integer |
| `IsPositive` / `IsNegative` | field is a number greater / less than zero |
| `DivisibleBy` | field is an integer divisible by the value |
| `IsBetweenInclusive` | field is within the `[min, max]` value array |
| `IsWeekend` / `IsWeekday` | field date falls on a weekend / weekday |
| `IsInPast` / `IsInFuture` | field date is before / after now |

Every predicate is **total**: given a value of an unexpected shape it returns `false` rather
than throwing, so a `custom` leaf can never crash an evaluation.

`BuiltInFunctions.Create(clock)` takes an injectable clock, so `IsInPast`/`IsInFuture` are
deterministic under test.

## Write your own

```csharp
var isBusinessDay = new NamedRuleFunction(
    "IsBusinessDay",
    (field, value) => field is DateTime d && d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday),
    description: "Field date is a weekday.",
    valueKind: RuleFunctionValueKind.None);
```

`NamedRuleFunction` implements `IRuleFunctionMetadata`, so the description and value kind show
up in `engine.FunctionCatalog` for a rule-builder UI to render.

## Requirements

.NET Framework 4.8, .NET Standard 2.0, .NET 8.0 or .NET 10.0.

## License

MIT.
