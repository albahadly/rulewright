# Rulewright.Core

Foundation package for [Rulewright](https://www.nuget.org/packages/Rulewright): the immutable
domain model a rule is made of, plus the result and trace types an evaluation produces.

**Zero dependencies.**

## Install

```
dotnet add package Rulewright.Core
```

**You almost certainly want [`Rulewright`](https://www.nuget.org/packages/Rulewright)
instead** — it includes this package. Install `Rulewright.Core` directly only when you are
writing something that plugs into Rulewright and does not need the rest of it: a rule
generator, an analyzer, or a `custom` function library.

## What is in it

**The rule model** — `RuleSet`, `Rule`, and the condition tree: `ConditionNode` with its two
shapes, `ConditionGroup` (`AND`/`OR`/`NOT` over children) and `ConditionLeaf` (a field path or
computed expression, a `ConditionOperator`, and a constant). Everything is immutable after
construction and validated in the constructor.

```csharp
using Rulewright.Core;

var rule = new Rule(
    "adults-only",
    new ConditionLeaf("Customer.Age", ConditionOperator.GreaterThanOrEqual, 18),
    new[] { new RuleAction(RuleAction.SetOutputType, "Eligible", true) });
```

**Actions** — `RuleAction` and its four types: `setOutput` replaces, `addToOutput` sums,
`appendToOutput` collects into a list, `removeOutput` deletes the key.

**Value expressions** — `ValueExpression` and its three shapes, `LiteralExpression`,
`FieldExpression`, and `OperatorExpression` (arithmetic, `concat`, `coalesce`). Pure data: a
closed operator vocabulary, never embedded code.

**Results and traces** — `RuleEvaluationResult`, `FiredRule` (with the `RuleBranch` that ran),
`EvaluationTrace`, `RuleTrace`, and `ConditionTraceNode`, whose `Passed` is null for a node
short-circuiting skipped. `CompilationMode` reports whether an evaluation ran compiled
delegates or the interpreter, so a fallback is never silent.

**Custom function contracts** — `IRuleFunction`, `IRuleFunctionMetadata`,
`RuleFunctionDescriptor`, and `RuleFunctionValueKind`: implement `IRuleFunction` to back the
`custom` operator, and add the metadata interface so a builder UI can discover it.

## Requirements

.NET Framework 4.8, .NET Standard 2.0, .NET 8.0 or .NET 10.0.

## License

MIT.
