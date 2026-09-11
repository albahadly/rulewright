using BenchmarkDotNet.Running;
using RuleWright.Benchmarks;

// `verify` mode: confirm the three engines agree on which rules match, so the comparison
// benchmark measures the same decision rather than different amounts of work.
if (args.Length > 0 && args[0] == "verify")
{
    foreach (int ruleCount in new[] { 10, 100 })
    {
        ComparisonHarness harness = ComparisonHarness.Build(ruleCount);
        int rw = harness.EvaluateRuleWright();
        int nr = harness.EvaluateNRules();
        int re = harness.EvaluateRulesEngine();
        bool agree = rw == nr && nr == re;
        Console.WriteLine($"RuleCount={ruleCount,-5} RuleWright={rw,-4} NRules={nr,-4} RulesEngine={re,-4} => {(agree ? "AGREE" : "MISMATCH")}");
    }

    return;
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

public partial class Program
{
}
