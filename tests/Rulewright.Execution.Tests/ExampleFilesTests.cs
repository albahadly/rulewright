using Rulewright.Core;
using Rulewright.Extensions.Functions;
using Rulewright.Json.SystemText;
using Rulewright.Serialization;
using Xunit;

namespace Rulewright.Execution.Tests;

/// <summary>
/// Every document under the repository's <c>examples/</c> folder must be a valid Rulewright
/// document that loads cleanly, so the examples cannot drift out of sync with the engine.
/// </summary>
public class ExampleFilesTests
{
    private static readonly RulewrightEngine Engine = new RulewrightBuilder()
        .UseJsonReader(new SystemTextJsonReader())
        // Covers 12-custom-function.json (IsWeekend) and 18-builtin-functions.json.
        .RegisterBuiltInFunctions()
        .Build();

    private static readonly string ExamplesDirectory = FindExamplesDirectory();

    public static IEnumerable<object[]> ExampleFiles()
        => Directory.GetFiles(ExamplesDirectory, "*.json")
            .Select(file => new object[] { Path.GetFileName(file) });

    [Theory]
    [MemberData(nameof(ExampleFiles))]
    public void Example_ValidatesLoadsAndEvaluates(string fileName)
    {
        string json = File.ReadAllText(Path.Combine(ExamplesDirectory, fileName));

        RuleSetValidationResult validation = Engine.Validate(json);
        Assert.True(
            validation.IsValid,
            $"{fileName}: {string.Join("; ", validation.Errors.Select(e => $"{e.Path} {e.Message}"))}");

        // LoadRuleSet parses, validates, resolves custom functions, and prepares the rule set
        // (a decision table expands to rules here).
        LoadedRuleSet loaded = Engine.LoadRuleSet(json);
        Assert.NotEmpty(loaded.RuleSet.Rules);

        // Evaluation is total — it never throws on data — so every example must run cleanly
        // against a representative fact. Both execution paths are exercised: the dictionary fact
        // runs the interpreter, the typed fact compiles every field path in the document against
        // a real CLR type (which a dictionary fact can never check), and with tracing on so the
        // traced delegate is built too.
        var options = new EvaluationOptions { EnableTrace = true };

        RuleEvaluationResult interpreted = Engine.Evaluate(loaded, RepresentativeFact(), options);
        Assert.Equal(CompilationMode.Interpreted, interpreted.CompilationMode);
        Assert.NotNull(interpreted.Outputs);
        Assert.NotNull(interpreted.Trace);

        RuleEvaluationResult compiled = Engine.Evaluate(loaded, TypedFact(), options);
        Assert.Equal(CompilationMode.Compiled, compiled.CompilationMode);
        Assert.NotNull(compiled.Trace);

        // The two facts carry the same values, so the same rules must fire on both paths.
        Assert.Equal(
            interpreted.FiredRules.Select(r => r.RuleId).ToArray(),
            compiled.FiredRules.Select(r => r.RuleId).ToArray());
    }

    private static Dictionary<string, object?> RepresentativeFact() => new()
    {
        ["Customer"] = new Dictionary<string, object?>
        {
            ["Name"] = "Alice",
            ["Age"] = 30L,
            ["Tier"] = "vip",
            ["IsVip"] = true,
            ["LoyaltyYears"] = 5L,
            ["Country"] = "US",
            ["Email"] = "alice@acme.com",
            ["PostCode"] = "12345",
        },
        ["Order"] = new Dictionary<string, object?>
        {
            ["Total"] = 150m,
            ["ItemCount"] = 3L,
            ["Weight"] = 2.5,
            ["Coupon"] = "SAVE10",
            ["Category"] = "books",
            ["ShippingCost"] = 5m,
            ["DiscountApplied"] = 10m,
            ["PlacedOn"] = new DateTime(2026, 7, 18),
            ["Tags"] = new object?[] { "gift", "priority" },
            ["Lines"] = new object?[]
            {
                new Dictionary<string, object?> { ["Category"] = "books", ["Quantity"] = 2L, ["InStock"] = true },
                new Dictionary<string, object?> { ["Category"] = "alcohol", ["Quantity"] = 1L, ["InStock"] = true },
                new Dictionary<string, object?> { ["Category"] = "stationery", ["Quantity"] = 3L, ["InStock"] = true },
            },
        },
    };

    /// <summary>
    /// The same values as <see cref="RepresentativeFact"/>, as a CLR type. Every field path used
    /// anywhere in <c>examples/</c> must exist here or compilation fails — which is the point:
    /// it proves the examples bind against a real type, not just against a forgiving dictionary.
    /// </summary>
    private static ExampleFact TypedFact() => new ExampleFact
    {
        Customer = new ExampleCustomer
        {
            Name = "Alice",
            Age = 30,
            Tier = "vip",
            IsVip = true,
            LoyaltyYears = 5,
            Country = "US",
            Email = "alice@acme.com",
            PostCode = "12345",
        },
        Order = new ExampleOrder
        {
            Total = 150m,
            ItemCount = 3,
            Weight = 2.5,
            Coupon = "SAVE10",
            Category = "books",
            ShippingCost = 5m,
            DiscountApplied = 10m,
            PlacedOn = new DateTime(2026, 7, 18),
            Tags = new[] { "gift", "priority" },
            Lines = new List<ExampleLine>
            {
                new ExampleLine { Category = "books", Quantity = 2, InStock = true },
                new ExampleLine { Category = "alcohol", Quantity = 1, InStock = true },
                new ExampleLine { Category = "stationery", Quantity = 3, InStock = true },
            },
        },
    };

    private sealed class ExampleFact
    {
        public ExampleCustomer Customer { get; set; } = new ExampleCustomer();

        public ExampleOrder Order { get; set; } = new ExampleOrder();
    }

    private sealed class ExampleCustomer
    {
        public string? Name { get; set; }

        public long Age { get; set; }

        public string? Tier { get; set; }

        public bool IsVip { get; set; }

        public long LoyaltyYears { get; set; }

        public string? Country { get; set; }

        public string? Email { get; set; }

        public string? PostCode { get; set; }
    }

    private sealed class ExampleOrder
    {
        public decimal Total { get; set; }

        public long ItemCount { get; set; }

        public double Weight { get; set; }

        public string? Coupon { get; set; }

        public string? Category { get; set; }

        public decimal ShippingCost { get; set; }

        public decimal DiscountApplied { get; set; }

        public DateTime PlacedOn { get; set; }

        /// <summary>Absent from the dictionary fact too — exercises the null-path semantics.</summary>
        public decimal? InternationalPenalty { get; set; }

        public string[]? Tags { get; set; }

        public List<ExampleLine> Lines { get; set; } = new List<ExampleLine>();
    }

    private sealed class ExampleLine
    {
        public string? Category { get; set; }

        public long Quantity { get; set; }

        public bool InStock { get; set; }
    }

    private static string FindExamplesDirectory()
    {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Rulewright.slnx"))
                && Directory.Exists(Path.Combine(directory.FullName, "examples")))
            {
                return Path.Combine(directory.FullName, "examples");
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository 'examples' directory.");
    }
}
