using RuleWright.Json.SystemText;
using Xunit;

namespace RuleWright.Execution.Tests;

/// <summary>
/// An engine exposes the custom functions registered on it — the per-configuration half of
/// the authoring vocabulary a rule-builder UI needs (the static half is RuleSchemaCatalog).
/// </summary>
public class EngineDiscoveryTests
{
    [Fact]
    public void RegisteredFunctions_ListsNamesSortedOrdinally()
    {
        RuleWrightEngine engine = new RuleWrightBuilder()
            .UseJsonReader(new SystemTextJsonReader())
            .RegisterFunction("IsWeekend", (f, v) => false)
            .RegisterFunction("AlwaysTrue", (f, v) => true)
            .Build();

        Assert.Equal(new[] { "AlwaysTrue", "IsWeekend" }, engine.RegisteredFunctions.ToArray());
    }

    [Fact]
    public void RegisteredFunctions_EmptyWhenNoneRegistered()
    {
        RuleWrightEngine engine = new RuleWrightBuilder()
            .UseJsonReader(new SystemTextJsonReader())
            .Build();

        Assert.Empty(engine.RegisteredFunctions);
    }
}
