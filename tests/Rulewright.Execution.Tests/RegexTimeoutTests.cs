using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using Rulewright.Json.SystemText;
using Xunit;
using static Rulewright.Execution.Tests.TestEngine;

namespace Rulewright.Execution.Tests;

/// <summary>
/// MatchesRegex runs an author-supplied pattern against consumer-supplied fact data, so a
/// pattern with catastrophic backtracking must be bounded in time rather than able to pin a
/// thread indefinitely.
/// </summary>
public class RegexTimeoutTests
{
    // Classic exponential backtracker: every extra 'a' before the non-matching '!' doubles work.
    private const string CatastrophicRule =
        "{\"id\":\"r\",\"condition\":{\"field\":\"Customer.Name\",\"operator\":\"MatchesRegex\",\"value\":\"^(a+)+$\"}}";

    private static OrderFact HostileFact()
    {
        OrderFact fact = DefaultFact();
        fact.Customer.Name = new string('a', 40) + "!";
        return fact;
    }

    [Fact]
    public void CompiledPath_BoundsCatastrophicBacktracking()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(CatastrophicRule);
        Stopwatch sw = Stopwatch.StartNew();
        Assert.Throws<RegexMatchTimeoutException>(() => Engine.Evaluate(loaded, HostileFact()));
        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), $"evaluation took {sw.Elapsed}");
    }

    [Fact]
    public void InterpretedPath_BoundsCatastrophicBacktracking()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(CatastrophicRule);
        var fact = new Dictionary<string, object?>
        {
            ["Customer"] = new Dictionary<string, object?> { ["Name"] = new string('a', 40) + "!" },
        };

        Stopwatch sw = Stopwatch.StartNew();
        Assert.Throws<RegexMatchTimeoutException>(() => Engine.Evaluate(loaded, fact));
        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), $"evaluation took {sw.Elapsed}");
    }

    [Fact]
    public void TimeoutIsConfigurable()
    {
        RulewrightEngine engine = new RulewrightBuilder()
            .UseJsonReader(new SystemTextJsonReader())
            .UseRegexTimeout(TimeSpan.FromMilliseconds(50))
            .Build();

        LoadedRuleSet loaded = engine.LoadRuleSet(CatastrophicRule);
        Stopwatch sw = Stopwatch.StartNew();
        Assert.Throws<RegexMatchTimeoutException>(() => engine.Evaluate(loaded, HostileFact()));
        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"evaluation took {sw.Elapsed}");
    }

    [Fact]
    public void WellBehavedPatternsStillMatch()
    {
        Assert.True(Matches(
            "{\"field\":\"Customer.Email\",\"operator\":\"MatchesRegex\",\"value\":\"^[^@]+@example\\\\.com$\"}",
            DefaultFact()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveTimeoutIsRejected(int milliseconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RulewrightBuilder().UseRegexTimeout(TimeSpan.FromMilliseconds(milliseconds)));
    }

    [Fact]
    public void InfiniteTimeoutIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RulewrightBuilder().UseRegexTimeout(Timeout.InfiniteTimeSpan));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RulewrightBuilder().UseRegexTimeout(TimeSpan.MaxValue));
    }
}
