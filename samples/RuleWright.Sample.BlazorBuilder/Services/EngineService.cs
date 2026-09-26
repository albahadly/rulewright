using RuleWright.Execution;
using RuleWright.Extensions.Functions;
using RuleWright.Json.SystemText;
using RuleWright.Serialization;

namespace RuleWright.Sample.BlazorBuilder.Services;

/// <summary>
/// Owns the single <see cref="RuleWrightEngine"/> the whole app evaluates against (built once,
/// with the built-in <c>custom</c> functions plus a sample value function and custom action
/// registered) — everything runs in-browser, no server round trip. Also exposes the
/// <see cref="RuleSchemaCatalog"/>, the <see cref="IRuleJsonReader"/>, and the engine's
/// <see cref="RuleDocumentOptions"/> so components don't each have to know how the engine was
/// constructed.
/// </summary>
public sealed class EngineService
{
    public EngineService()
    {
        JsonReader = new SystemTextJsonReader();
        Engine = new RuleWrightBuilder()
            .UseJsonReader(JsonReader)
            .RegisterBuiltInFunctions()
            // The same sample registrations examples/README.md documents, so the bundled
            // examples that use them (23-value-functions, 25-custom-action) validate, test,
            // and trace inside the builder.
            .RegisterValueFunction("RoundTo", args =>
                args.Length == 2 && args[0] is decimal value && args[1] is long digits
                    ? decimal.Round(value, (int)digits)
                    : null)
            .RegisterAction("setIfHigher", context =>
            {
                context.TryGetOutput(context.Target, out object? current);
                if (AsDecimal(context.Value) is decimal incoming
                    && (AsDecimal(current) is not decimal held || incoming > held))
                {
                    context.SetOutput(context.Target, incoming);
                }
            })
            .Build();

        // The engine's registered custom action types, folded into structural validation and
        // parsing exactly as Engine.Validate / Engine.LoadRuleSet fold them.
        DocumentOptions = new RuleDocumentOptions(Engine.RegisteredActions);
    }

    public IRuleJsonReader JsonReader { get; }

    public RuleWrightEngine Engine { get; }

    public RuleDocumentOptions DocumentOptions { get; }

    private static decimal? AsDecimal(object? value) => value switch
    {
        decimal d => d,
        long l => l,
        double d => (decimal)d,
        _ => null,
    };
}
