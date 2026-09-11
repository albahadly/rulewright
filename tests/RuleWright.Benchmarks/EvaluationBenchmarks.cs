using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;
using RuleWright.Core;
using RuleWright.Execution;
using RuleWright.Json.SystemText;

namespace RuleWright.Benchmarks;

/// <summary>
/// Warm-path throughput: compiled delegates (typed facts) vs the interpreter
/// (dictionary facts) at 1 / 100 / 10,000 rules, with and without tracing.
/// Rule sets are loaded and warmed once in GlobalSetup so the measurements are
/// pure evaluation cost.
/// </summary>
[MemoryDiagnoser]
public class EvaluationBenchmarks
{
    private RuleWrightEngine _engine = null!;
    private LoadedRuleSet _ruleSet = null!;
    private OrderFact _typedFact = null!;
    private Dictionary<string, object?> _dictionaryFact = null!;
    private EvaluationOptions _traceOptions = null!;

    [Params(1, 100, 10_000)]
    public int RuleCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _engine = new RuleWrightBuilder().UseJsonReader(new SystemTextJsonReader()).Build();
        _ruleSet = _engine.LoadRuleSet(BenchmarkRuleSets.Build(RuleCount));
        _typedFact = BenchmarkRuleSets.TypedFact();
        _dictionaryFact = BenchmarkRuleSets.DictionaryFact();
        _traceOptions = new EvaluationOptions { EnableTrace = true };

        // Warm both paths so compile cost never leaks into the measurements.
        _engine.Evaluate(_ruleSet, _typedFact);
        _engine.Evaluate(_ruleSet, _dictionaryFact);
    }

    [Benchmark(Baseline = true)]
    public RuleEvaluationResult Compiled_Typed() => _engine.Evaluate(_ruleSet, _typedFact);

    [Benchmark]
    public RuleEvaluationResult Compiled_Typed_Traced() => _engine.Evaluate(_ruleSet, _typedFact, _traceOptions);

    [Benchmark]
    public RuleEvaluationResult Interpreted_Dictionary() => _engine.Evaluate(_ruleSet, _dictionaryFact);
}

/// <summary>
/// Cold-start cost: loading (parse + validate + hash) and the first compiled
/// evaluation of a fresh engine, vs a warm cached call.
/// </summary>
[MemoryDiagnoser]
public class ColdStartBenchmarks
{
    private string _json = null!;
    private RuleWrightEngine _warmEngine = null!;
    private LoadedRuleSet _warmRuleSet = null!;
    private OrderFact _fact = null!;

    [Params(1, 100)]
    public int RuleCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _json = BenchmarkRuleSets.Build(RuleCount);
        _fact = BenchmarkRuleSets.TypedFact();
        _warmEngine = new RuleWrightBuilder().UseJsonReader(new SystemTextJsonReader()).Build();
        _warmRuleSet = _warmEngine.LoadRuleSet(_json);
        _warmEngine.Evaluate(_warmRuleSet, _fact);
    }

    [Benchmark]
    public LoadedRuleSet ColdLoad()
    {
        var engine = new RuleWrightBuilder().UseJsonReader(new SystemTextJsonReader()).Build();
        return engine.LoadRuleSet(_json);
    }

    [Benchmark]
    public RuleEvaluationResult ColdLoadCompileAndFirstEvaluate()
    {
        var engine = new RuleWrightBuilder().UseJsonReader(new SystemTextJsonReader()).Build();
        return engine.Evaluate(engine.LoadRuleSet(_json), _fact);
    }

    [Benchmark(Baseline = true)]
    public RuleEvaluationResult WarmCachedEvaluate() => _warmEngine.Evaluate(_warmRuleSet, _fact);
}

internal static class BenchmarkRuleSets
{
    /// <summary>Builds a rule set of N structurally distinct discount-style rules.</summary>
    internal static string Build(int ruleCount)
    {
        var builder = new StringBuilder(ruleCount * 512);
        builder.Append("{\"rules\":[");
        for (int i = 0; i < ruleCount; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            string threshold = (i % 200).ToString(CultureInfo.InvariantCulture);
            builder
                .Append("{\"id\":\"rule-").Append(i.ToString(CultureInfo.InvariantCulture))
                .Append("\",\"priority\":").Append((i % 10).ToString(CultureInfo.InvariantCulture))
                .Append(",\"condition\":{\"type\":\"group\",\"operator\":\"AND\",\"rules\":[")
                .Append("{\"field\":\"Customer.Age\",\"operator\":\"GreaterThan\",\"value\":").Append(threshold).Append("},")
                .Append("{\"type\":\"group\",\"operator\":\"OR\",\"rules\":[")
                .Append("{\"field\":\"Order.Total\",\"operator\":\"GreaterThanOrEqual\",\"value\":100},")
                .Append("{\"field\":\"Customer.IsVip\",\"operator\":\"Equals\",\"value\":true}]}]},")
                .Append("\"actions\":[{\"type\":\"setOutput\",\"target\":\"Discount\",\"value\":")
                .Append(threshold).Append("}]}");
        }

        builder.Append("]}");
        return builder.ToString();
    }

    internal static OrderFact TypedFact() => new OrderFact
    {
        Customer = new Customer { Age = 90, IsVip = true, Name = "Bench" },
        Order = new Order { Total = 250m },
    };

    internal static Dictionary<string, object?> DictionaryFact() => new Dictionary<string, object?>
    {
        ["Customer"] = new Dictionary<string, object?> { ["Age"] = 90L, ["IsVip"] = true, ["Name"] = "Bench" },
        ["Order"] = new Dictionary<string, object?> { ["Total"] = 250L },
    };
}

public sealed class OrderFact
{
    public Customer Customer { get; set; } = new();

    public Order Order { get; set; } = new();
}

public sealed class Customer
{
    public int Age { get; set; }

    public bool IsVip { get; set; }

    public string Name { get; set; } = string.Empty;
}

public sealed class Order
{
    public decimal Total { get; set; }
}
