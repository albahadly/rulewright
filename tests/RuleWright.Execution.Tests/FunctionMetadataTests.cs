using System.Collections.Generic;
using System.Linq;
using RuleWright.Core;
using RuleWright.Extensions.Functions;
using RuleWright.Json.SystemText;
using Xunit;

namespace RuleWright.Execution.Tests;

/// <summary>
/// <see cref="RuleFunctionDescriptor"/>/<see cref="IRuleFunctionMetadata"/> discovery: the
/// built-in functions expose real metadata, functions that don't opt in still register and
/// evaluate normally, and <see cref="RuleWrightEngine.RegisteredFunctions"/>'s existing shape
/// is unaffected by the new <see cref="RuleWrightEngine.FunctionCatalog"/> member.
/// </summary>
public class FunctionMetadataTests
{
    private static RuleWrightBuilder Builder() => new RuleWrightBuilder().UseJsonReader(new SystemTextJsonReader());

    public static IEnumerable<object[]> BuiltInFunctionValueKinds()
    {
        yield return new object[] { "IsNullOrEmpty", RuleFunctionValueKind.None };
        yield return new object[] { "IsNullOrWhiteSpace", RuleFunctionValueKind.None };
        yield return new object[] { "EqualsIgnoreCase", RuleFunctionValueKind.Text };
        yield return new object[] { "IsEmail", RuleFunctionValueKind.None };
        yield return new object[] { "IsEven", RuleFunctionValueKind.None };
        yield return new object[] { "IsOdd", RuleFunctionValueKind.None };
        yield return new object[] { "IsPositive", RuleFunctionValueKind.None };
        yield return new object[] { "IsNegative", RuleFunctionValueKind.None };
        yield return new object[] { "DivisibleBy", RuleFunctionValueKind.Scalar };
        yield return new object[] { "IsBetweenInclusive", RuleFunctionValueKind.Array };
        yield return new object[] { "IsWeekend", RuleFunctionValueKind.None };
        yield return new object[] { "IsWeekday", RuleFunctionValueKind.None };
        yield return new object[] { "IsInPast", RuleFunctionValueKind.None };
        yield return new object[] { "IsInFuture", RuleFunctionValueKind.None };
    }

    [Fact]
    public void AllBuiltInFunctions_HaveMetadataCoverage()
    {
        // Locks in that every built-in has a covering row above — a future 15th predicate
        // added without a matching entry here (and without metadata) fails this count check.
        Assert.Equal(BuiltInFunctionValueKinds().Count(), BuiltInFunctions.All.Count);
    }

    [Theory]
    [MemberData(nameof(BuiltInFunctionValueKinds))]
    public void BuiltInFunction_HasDescriptionAndExpectedValueKind(string name, RuleFunctionValueKind expectedKind)
    {
        var function = Assert.Single(BuiltInFunctions.All, f => f.Name == name);
        var metadata = Assert.IsAssignableFrom<IRuleFunctionMetadata>(function);

        Assert.False(string.IsNullOrWhiteSpace(metadata.Description));
        Assert.Equal(expectedKind, metadata.ValueKind);
    }

    [Fact]
    public void NamedRuleFunction_TwoArgCtor_YieldsUnspecifiedMetadata()
    {
        var function = new NamedRuleFunction("AlwaysYes", (f, v) => true);

        Assert.Null(function.Description);
        Assert.Equal(RuleFunctionValueKind.Unspecified, function.ValueKind);
    }

    [Fact]
    public void FunctionCatalog_ReflectsBuiltInMetadata()
    {
        RuleWrightEngine engine = Builder().RegisterBuiltInFunctions().Build();

        RuleFunctionDescriptor descriptor = engine.FunctionCatalog.Single(d => d.Name == "IsBetweenInclusive");

        Assert.Equal(RuleFunctionValueKind.Array, descriptor.ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(descriptor.Description));
    }

    [Fact]
    public void FunctionCatalog_FunctionWithoutMetadata_AppearsAsUnspecified()
    {
        RuleWrightEngine engine = Builder()
            .RegisterFunctions(new[] { new NamedRuleFunction("AlwaysYes", (f, v) => true) })
            .Build();

        RuleFunctionDescriptor descriptor = Assert.Single(engine.FunctionCatalog);

        Assert.Equal("AlwaysYes", descriptor.Name);
        Assert.Null(descriptor.Description);
        Assert.Equal(RuleFunctionValueKind.Unspecified, descriptor.ValueKind);
    }

    [Fact]
    public void RegisteredFunctions_ShapeIsUnchangedByFunctionCatalog()
    {
        RuleWrightEngine engine = Builder().RegisterBuiltInFunctions().Build();

        var expectedNames = BuiltInFunctions.All.Select(f => f.Name).OrderBy(n => n, System.StringComparer.Ordinal);
        Assert.Equal(expectedNames, engine.RegisteredFunctions);
        Assert.Equal(engine.RegisteredFunctions, engine.FunctionCatalog.Select(d => d.Name));
    }
}
