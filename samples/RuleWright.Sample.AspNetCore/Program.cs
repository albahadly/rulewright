// A minimal API exposing a RuleWright rule set as an HTTP evaluation endpoint. Everything here
// mirrors the console sample's engine setup — the difference is that the engine and the loaded
// rule set are built once at startup and shared as singletons, and facts arrive as request JSON
// instead of C# objects. See the README's "Discovering the vocabulary" section for what /vocabulary
// is for: it is the runtime contract a future rule-builder UI would consume.

using System.Text.Json;
using RuleWright.Core;
using RuleWright.Execution;
using RuleWright.Json.SystemText;
using RuleWright.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(new RuleWrightBuilder()
    .UseJsonReader(new SystemTextJsonReader())
    .Build());

builder.Services.AddSingleton(provider =>
{
    var engine = provider.GetRequiredService<RuleWrightEngine>();
    string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "rules", "discount-rule.json"));
    return engine.LoadRuleSet(json);
});

var app = builder.Build();

app.MapGet("/", () => Results.Text(
    "RuleWright ASP.NET Core sample. POST a fact to /evaluate, or GET /rules and /vocabulary."));

app.MapGet("/rules", (LoadedRuleSet loaded) => Results.Ok(new
{
    name = loaded.RuleSet.Name,
    rules = loaded.RuleSet.Rules.Select(rule => new { rule.Id, rule.Description, rule.Priority }),
}));

app.MapGet("/vocabulary", () => Results.Ok(new
{
    conditionOperators = RuleSchemaCatalog.ConditionOperators
        .Select(info => new { name = info.JsonName, valueKind = info.ValueKind.ToString() }),
    logicalOperators = RuleSchemaCatalog.LogicalOperators
        .Select(info => new { name = info.JsonName, info.MinChildren, info.MaxChildren }),
    expressionOperators = RuleSchemaCatalog.ExpressionOperators
        .Select(info => new { name = info.JsonName, info.MinOperands, info.MaxOperands, category = info.Category.ToString() }),
    actionTypes = RuleSchemaCatalog.ActionTypes
        .Select(info => new { name = info.Name, info.RequiresValue, effect = info.Effect.ToString() }),
}));

app.MapPost("/evaluate", (EvaluateRequest request, RuleWrightEngine engine, LoadedRuleSet loaded) =>
{
    if (request.Fact.ValueKind != JsonValueKind.Object)
    {
        return Results.BadRequest("'fact' must be a JSON object.");
    }

    Dictionary<string, object?> fact = SystemTextJsonFacts.ToDictionary(request.Fact);
    RuleEvaluationResult result = engine.Evaluate(
        loaded, fact, new EvaluationOptions { EnableTrace = request.Trace });

    return Results.Ok(new EvaluateResponse(
        result.FiredRules.Select(fired => new FiredRuleResponse(fired.RuleId, fired.Branch.ToString(), fired.Outputs)).ToArray(),
        result.Outputs,
        result.CompilationMode.ToString(),
        request.Trace ? Describe(result.Trace!.Rules) : null));
})
.WithName("Evaluate");

app.Run();

static string[] Describe(IReadOnlyList<RuleTrace> rules)
    => rules.Select(rule => rule.Skipped
            ? $"{rule.RuleId}: skipped (disabled)"
            : $"{rule.RuleId}: {(rule.Fired ? "fired" : "did not fire")}")
        .ToArray();

/// <param name="Fact">The fact to evaluate, as a JSON object (nested objects/arrays are fine).</param>
/// <param name="Trace">When true, the response includes a per-rule trace summary.</param>
public sealed record EvaluateRequest(JsonElement Fact, bool Trace = false);

public sealed record FiredRuleResponse(string RuleId, string Branch, IReadOnlyDictionary<string, object?> Outputs);

public sealed record EvaluateResponse(
    IReadOnlyList<FiredRuleResponse> FiredRules,
    IReadOnlyDictionary<string, object?> Outputs,
    string CompilationMode,
    string[]? Trace);

// Exposed so WebApplicationFactory<Program>-style integration tests can reference the entry point.
public partial class Program;
