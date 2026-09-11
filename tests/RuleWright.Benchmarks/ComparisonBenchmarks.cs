using System.Globalization;
using System.Linq.Expressions;
using BenchmarkDotNet.Attributes;
using NRules;
using NRules.RuleModel;
using NRules.RuleModel.Builders;
using RuleWright.Execution;
using RuleWright.Json.SystemText;
using RulesEngine.Models;

namespace RuleWright.Benchmarks;

/// <summary>
/// A like-for-like warm-path comparison of RuleWright against NRules and Microsoft RulesEngine:
/// the same logical rule set (<c>Customer.Age &gt; threshold AND (Order.Total &gt;= 100 OR
/// Customer.IsVip)</c>, one rule per threshold) evaluated against the same typed fact. Every
/// engine is fully built/compiled once in <see cref="GlobalSetup"/>; only per-fact evaluation is
/// measured. The three engines agree on which rules match (see the <c>verify</c> program mode),
/// so this compares the cost of the same decision, not different amounts of work.
/// </summary>
[MemoryDiagnoser]
public class EngineComparisonBenchmarks
{
    private ComparisonHarness _harness = null!;

    [Params(10, 100)]
    public int RuleCount { get; set; }

    [GlobalSetup]
    public void Setup() => _harness = ComparisonHarness.Build(RuleCount);

    [Benchmark(Baseline = true, Description = "RuleWright (compiled)")]
    public int RuleWright() => _harness.EvaluateRuleWright();

    [Benchmark(Description = "NRules (Rete)")]
    public int NRules() => _harness.EvaluateNRules();

    [Benchmark(Description = "MS RulesEngine")]
    public int RulesEngine() => _harness.EvaluateRulesEngine();
}

/// <summary>
/// Builds the identical rule set + fact in all three engines and exposes one evaluation call per
/// engine. Shared by the benchmark and the <c>verify</c> program mode so they measure exactly
/// what they check.
/// </summary>
internal sealed class ComparisonHarness
{
    private readonly RuleWrightEngine _rwEngine;
    private readonly LoadedRuleSet _rwRules;
    private readonly ISessionFactory _nrFactory;
    private readonly RulesEngine.RulesEngine _reEngine;
    private readonly OrderFact _fact;

    private ComparisonHarness(
        RuleWrightEngine rwEngine,
        LoadedRuleSet rwRules,
        ISessionFactory nrFactory,
        RulesEngine.RulesEngine reEngine,
        OrderFact fact)
    {
        _rwEngine = rwEngine;
        _rwRules = rwRules;
        _nrFactory = nrFactory;
        _reEngine = reEngine;
        _fact = fact;
    }

    internal static ComparisonHarness Build(int ruleCount)
    {
        // A discriminating fact: high age and a big VIP order, so Age > threshold is the
        // deciding clause across rules — the same fact all three engines see.
        var fact = new OrderFact
        {
            Customer = new Customer { Age = 90, IsVip = true, Name = "Bench" },
            Order = new Order { Total = 250m },
        };

        RuleWrightEngine rwEngine = new RuleWrightBuilder().UseJsonReader(new SystemTextJsonReader()).Build();
        LoadedRuleSet rwRules = rwEngine.LoadRuleSet(BuildRuleWrightJson(ruleCount));

        ISessionFactory nrFactory = new RuleCompiler().Compile(BuildNRules(ruleCount));

        var workflow = new Workflow { WorkflowName = "bench", Rules = BuildRulesEngineRules(ruleCount) };
        var reEngine = new RulesEngine.RulesEngine(new[] { workflow });

        var harness = new ComparisonHarness(rwEngine, rwRules, nrFactory, reEngine, fact);

        // Warm every path so first-call compilation never leaks into a measurement.
        harness.EvaluateRuleWright();
        harness.EvaluateNRules();
        harness.EvaluateRulesEngine();
        return harness;
    }

    internal int EvaluateRuleWright() => _rwEngine.Evaluate(_rwRules, _fact).FiredRules.Count;

    internal int EvaluateNRules()
    {
        // A fresh session per fact — the stateless request-handling pattern.
        ISession session = _nrFactory.CreateSession();
        NRulesSink.Reset();
        session.Insert(_fact);
        session.Fire();
        return NRulesSink.Count;
    }

    internal int EvaluateRulesEngine()
    {
        List<RuleResultTree> results = _reEngine.ExecuteAllRulesAsync("bench", _fact).GetAwaiter().GetResult();
        int matched = 0;
        foreach (RuleResultTree result in results)
        {
            if (result.IsSuccess)
            {
                matched++;
            }
        }

        return matched;
    }

    private static string BuildRuleWrightJson(int ruleCount)
    {
        var builder = new System.Text.StringBuilder(ruleCount * 320);
        builder.Append("{\"rules\":[");
        for (int i = 0; i < ruleCount; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            string threshold = Threshold(i).ToString(CultureInfo.InvariantCulture);
            builder
                .Append("{\"id\":\"rule-").Append(i.ToString(CultureInfo.InvariantCulture))
                .Append("\",\"condition\":{\"type\":\"group\",\"operator\":\"AND\",\"rules\":[")
                .Append("{\"field\":\"Customer.Age\",\"operator\":\"GreaterThan\",\"value\":").Append(threshold).Append("},")
                .Append("{\"type\":\"group\",\"operator\":\"OR\",\"rules\":[")
                .Append("{\"field\":\"Order.Total\",\"operator\":\"GreaterThanOrEqual\",\"value\":100},")
                .Append("{\"field\":\"Customer.IsVip\",\"operator\":\"Equals\",\"value\":true}]}]},")
                .Append("\"actions\":[{\"type\":\"setOutput\",\"target\":\"Discount\",\"value\":").Append(threshold).Append("}]}");
        }

        builder.Append("]}");
        return builder.ToString();
    }

    private static IReadOnlyCollection<IRuleDefinition> BuildNRules(int ruleCount)
    {
        var definitions = new List<IRuleDefinition>(ruleCount);
        for (int i = 0; i < ruleCount; i++)
        {
            var builder = new RuleBuilder();
            builder.Name("rule-" + i.ToString(CultureInfo.InvariantCulture));

            ParameterExpression fact = Expression.Parameter(typeof(OrderFact), "fact");
            Expression customer = Expression.Property(fact, nameof(OrderFact.Customer));
            Expression order = Expression.Property(fact, nameof(OrderFact.Order));
            Expression condition = Expression.AndAlso(
                Expression.GreaterThan(Expression.Property(customer, nameof(Customer.Age)), Expression.Constant(Threshold(i))),
                Expression.OrElse(
                    Expression.GreaterThanOrEqual(Expression.Property(order, nameof(Order.Total)), Expression.Constant(100m)),
                    Expression.Property(customer, nameof(Customer.IsVip))));

            PatternBuilder pattern = builder.LeftHandSide().Pattern(typeof(OrderFact), "fact");
            pattern.Condition(Expression.Lambda(condition, fact));

            ParameterExpression context = Expression.Parameter(typeof(IContext), "ctx");
            ParameterExpression actionFact = Expression.Parameter(typeof(OrderFact), "fact");
            Expression action = Expression.Call(typeof(NRulesSink).GetMethod(nameof(NRulesSink.Hit))!, actionFact);
            builder.RightHandSide().Action(Expression.Lambda(action, context, actionFact));

            definitions.Add(builder.Build());
        }

        return definitions;
    }

    private static List<Rule> BuildRulesEngineRules(int ruleCount)
    {
        var rules = new List<Rule>(ruleCount);
        for (int i = 0; i < ruleCount; i++)
        {
            string threshold = Threshold(i).ToString(CultureInfo.InvariantCulture);
            rules.Add(new Rule
            {
                RuleName = "rule-" + i.ToString(CultureInfo.InvariantCulture),
                Expression = "input1.Customer.Age > " + threshold
                    + " && (input1.Order.Total >= 100 || input1.Customer.IsVip == true)",
            });
        }

        return rules;
    }

    // Age is 90, so thresholds below 90 match and the rest do not — a realistic partial match.
    private static int Threshold(int i) => i % 200;
}

/// <summary>
/// A trivial action sink for the NRules rules, counting matched activations. Public so the
/// compiled NRules action expression can bind to <see cref="Hit"/> via reflection.
/// </summary>
public static class NRulesSink
{
    public static int Count;

    public static void Hit(OrderFact fact) => Count++;

    public static void Reset() => Count = 0;
}
