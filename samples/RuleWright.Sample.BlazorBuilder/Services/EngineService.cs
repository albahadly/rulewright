using RuleWright.Execution;
using RuleWright.Extensions.Functions;
using RuleWright.Json.SystemText;
using RuleWright.Serialization;

namespace RuleWright.Sample.BlazorBuilder.Services;

/// <summary>
/// Owns the single <see cref="RuleWrightEngine"/> the whole app evaluates against (built once,
/// with the built-in <c>custom</c> functions registered) — everything runs in-browser, no server
/// round trip. Also exposes the <see cref="RuleSchemaCatalog"/> and <see cref="IRuleJsonReader"/>
/// so components don't each have to know how the engine was constructed.
/// </summary>
public sealed class EngineService
{
    public EngineService()
    {
        JsonReader = new SystemTextJsonReader();
        Engine = new RuleWrightBuilder()
            .UseJsonReader(JsonReader)
            .RegisterBuiltInFunctions()
            .Build();
    }

    public IRuleJsonReader JsonReader { get; }

    public RuleWrightEngine Engine { get; }
}
