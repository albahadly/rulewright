using System.Globalization;
using RuleWright.Core;
using Xunit;
using static RuleWright.Execution.Tests.TestEngine;

namespace RuleWright.Execution.Tests;

/// <summary>
/// Ordering comparisons on string fields are ordinal in both execution paths, so a rule's
/// outcome never depends on the machine's current culture and the compiled and interpreted
/// paths agree.
/// </summary>
public class StringOrderingTests
{
    private const string GreaterThanBob =
        "{\"field\":\"Customer.Name\",\"operator\":\"GreaterThan\",\"value\":\"Bob\"}";

    private static OrderFact Named(string name)
    {
        OrderFact fact = DefaultFact();
        fact.Customer.Name = name;
        return fact;
    }

    private static Dictionary<string, object?> DictionaryNamed(string name)
        => new Dictionary<string, object?>
        {
            ["Customer"] = new Dictionary<string, object?> { ["Name"] = name },
        };

    [Theory]
    [InlineData("alice", true)]   // 'a' (97) > 'B' (66) ordinally
    [InlineData("Alice", false)]  // 'A' (65) < 'B' (66)
    [InlineData("Bob", false)]
    [InlineData("Bobby", true)]
    public void CompiledPath_ComparesOrdinally(string name, bool expected)
    {
        Assert.Equal(expected, Matches(GreaterThanBob, Named(name)));
    }

    [Theory]
    [InlineData("alice")]
    [InlineData("Alice")]
    [InlineData("Bobby")]
    [InlineData("a-b")]
    public void CompiledAndInterpretedPathsAgree(string name)
    {
        Assert.Equal(Matches(GreaterThanBob, DictionaryNamed(name)), Matches(GreaterThanBob, Named(name)));
    }

    [Fact]
    public void OutcomeIsIndependentOfCurrentCulture()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            var results = new List<bool>();
            foreach (string culture in new[] { "en-US", "tr-TR", "da-DK", "sv-SE" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                results.Add(Matches(GreaterThanBob, Named("alice")));
            }

            Assert.All(results, r => Assert.True(r));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
