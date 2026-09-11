using System;
using System.Collections.Generic;
using System.Linq;
using RuleWright.Core;
using RuleWright.Extensions.Functions;
using RuleWright.Json.SystemText;
using Xunit;

namespace RuleWright.Execution.Tests;

/// <summary>A public rule function for the assembly-scanning discovery test.</summary>
public sealed class DiscoverableTestFunction : IRuleFunction
{
    /// <inheritdoc />
    public string Name => "DiscoveredIsFortyTwo";

    /// <inheritdoc />
    public bool Evaluate(object? fieldValue, object? value)
        => fieldValue is long l ? l == 42 : fieldValue is int i && i == 42;
}

/// <summary>
/// The registration and discovery helpers wire functions into the engine: the built-in catalog,
/// batches, and assembly scanning all end up usable by the <c>custom</c> operator and visible via
/// <see cref="RuleWrightEngine.RegisteredFunctions"/>.
/// </summary>
public class FunctionRegistrationTests
{
    private static RuleWrightBuilder Builder() => new RuleWrightBuilder().UseJsonReader(new SystemTextJsonReader());

    private static Dictionary<string, object?> OrderFact(long itemCount)
        => new() { ["Order"] = new Dictionary<string, object?> { ["ItemCount"] = itemCount } };

    [Fact]
    public void RegisterBuiltInFunctions_ExposesThemAll()
    {
        RuleWrightEngine engine = Builder().RegisterBuiltInFunctions().Build();

        var expected = BuiltInFunctions.All.Select(f => f.Name).OrderBy(n => n, StringComparer.Ordinal);
        Assert.Equal(expected, engine.RegisteredFunctions);
    }

    [Fact]
    public void BuiltInFunction_WorksEndToEndThroughTheCustomOperator()
    {
        const string rule = "{\"id\":\"r\",\"condition\":{\"field\":\"Order.ItemCount\",\"operator\":\"custom\",\"name\":\"IsEven\"}}";
        RuleWrightEngine engine = Builder().RegisterBuiltInFunctions().Build();
        LoadedRuleSet loaded = engine.LoadRuleSet(rule);

        Assert.Single(engine.Evaluate(loaded, OrderFact(4)).FiredRules);
        Assert.Empty(engine.Evaluate(loaded, OrderFact(3)).FiredRules);
    }

    [Fact]
    public void RegisterFunctions_RegistersABatch()
    {
        RuleWrightEngine engine = Builder()
            .RegisterFunctions(new[]
            {
                new NamedRuleFunction("AlwaysYes", (f, v) => true),
                new NamedRuleFunction("AlwaysNo", (f, v) => false),
            })
            .Build();

        Assert.Equal(new[] { "AlwaysNo", "AlwaysYes" }, engine.RegisteredFunctions);
    }

    [Fact]
    public void RegisterFunctionsFrom_DiscoversAndRegistersByScanning()
    {
        RuleWrightEngine engine = Builder()
            .RegisterFunctionsFrom(typeof(DiscoverableTestFunction).Assembly)
            .Build();

        Assert.Contains("DiscoveredIsFortyTwo", engine.RegisteredFunctions);

        const string rule = "{\"id\":\"r\",\"condition\":{\"field\":\"Answer\",\"operator\":\"custom\",\"name\":\"DiscoveredIsFortyTwo\"}}";
        LoadedRuleSet loaded = engine.LoadRuleSet(rule);
        Assert.Single(engine.Evaluate(loaded, new Dictionary<string, object?> { ["Answer"] = 42L }).FiredRules);
        Assert.Empty(engine.Evaluate(loaded, new Dictionary<string, object?> { ["Answer"] = 7L }).FiredRules);
    }

    [Fact]
    public void RegisterBuiltInFunctions_HonorsACustomClock()
    {
        var clockNow = new DateTime(2021, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        const string rule = "{\"id\":\"r\",\"condition\":{\"field\":\"When\",\"operator\":\"custom\",\"name\":\"IsInPast\"}}";
        RuleWrightEngine engine = Builder().RegisterBuiltInFunctions(() => clockNow).Build();
        LoadedRuleSet loaded = engine.LoadRuleSet(rule);

        Assert.Single(engine.Evaluate(loaded, new Dictionary<string, object?> { ["When"] = new DateTime(2020, 1, 1) }).FiredRules);
        Assert.Empty(engine.Evaluate(loaded, new Dictionary<string, object?> { ["When"] = new DateTime(2099, 1, 1) }).FiredRules);
    }

    [Fact]
    public void NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => Builder().RegisterFunctions(null!));
        Assert.Throws<ArgumentNullException>(() => Builder().RegisterFunctionsFrom(null!));
    }
}
