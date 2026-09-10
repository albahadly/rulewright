using System;
using System.Linq;
using Rulewright.Core;
using Rulewright.Extensions.Functions;
using Xunit;
using static Rulewright.Execution.Tests.TestEngine;

namespace Rulewright.Execution.Tests;

/// <summary>
/// Each built-in function is a total predicate: it returns the expected boolean for well-typed
/// input and simply returns <c>false</c> — never throws — for input of an unexpected shape.
/// </summary>
public class BuiltInFunctionTests
{
    private static IRuleFunction Fn(string name) => BuiltInFunctions.All.Single(f => f.Name == name);

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("x", false)]
    [InlineData(5, false)]
    public void IsNullOrEmpty(object? field, bool expected)
        => Assert.Equal(expected, Fn("IsNullOrEmpty").Evaluate(field, null));

    [Theory]
    [InlineData(null, true)]
    [InlineData("   ", true)]
    [InlineData("x", false)]
    public void IsNullOrWhiteSpace(object? field, bool expected)
        => Assert.Equal(expected, Fn("IsNullOrWhiteSpace").Evaluate(field, null));

    [Theory]
    [InlineData("Gold", "gold", true)]
    [InlineData("gold", "GOLD", true)]
    [InlineData("gold", "silver", false)]
    [InlineData(5, "5", false)]
    public void EqualsIgnoreCase(object? field, object? value, bool expected)
        => Assert.Equal(expected, Fn("EqualsIgnoreCase").Evaluate(field, value));

    [Theory]
    [InlineData("alice@acme.com", true)]
    [InlineData("not-an-email", false)]
    [InlineData("a@b", false)]
    [InlineData(42, false)]
    public void IsEmail(object? field, bool expected)
        => Assert.Equal(expected, Fn("IsEmail").Evaluate(field, null));

    [Fact]
    public void IsEven_And_IsOdd_AcrossNumericTypes()
    {
        Assert.True(Fn("IsEven").Evaluate(4L, null));
        Assert.True(Fn("IsEven").Evaluate(4m, null));
        Assert.False(Fn("IsEven").Evaluate(3L, null));
        Assert.False(Fn("IsEven").Evaluate(4.5, null));   // not integral
        Assert.False(Fn("IsEven").Evaluate("4", null));   // strings are not numbers
        Assert.False(Fn("IsEven").Evaluate(null, null));

        Assert.True(Fn("IsOdd").Evaluate(3L, null));
        Assert.True(Fn("IsOdd").Evaluate(-3L, null));
        Assert.False(Fn("IsOdd").Evaluate(4L, null));
    }

    [Fact]
    public void IsPositive_And_IsNegative()
    {
        Assert.True(Fn("IsPositive").Evaluate(0.5, null));
        Assert.False(Fn("IsPositive").Evaluate(0L, null));
        Assert.True(Fn("IsNegative").Evaluate(-2m, null));
        Assert.False(Fn("IsNegative").Evaluate(2L, null));
        Assert.False(Fn("IsPositive").Evaluate("nope", null));
    }

    [Fact]
    public void DivisibleBy()
    {
        Assert.True(Fn("DivisibleBy").Evaluate(10L, 5L));
        Assert.False(Fn("DivisibleBy").Evaluate(10L, 3L));
        Assert.False(Fn("DivisibleBy").Evaluate(10L, 0L));   // divisor zero → false, not a throw
        Assert.False(Fn("DivisibleBy").Evaluate("10", 5L));
    }

    [Fact]
    public void IsBetweenInclusive()
    {
        IRuleFunction between = Fn("IsBetweenInclusive");
        Assert.True(between.Evaluate(5L, new object?[] { 1L, 10L }));
        Assert.True(between.Evaluate(1L, new object?[] { 1L, 10L }));   // inclusive
        Assert.True(between.Evaluate(10m, new object?[] { 1L, 10L }));  // inclusive
        Assert.False(between.Evaluate(11L, new object?[] { 1L, 10L }));
        Assert.False(between.Evaluate(5L, new object?[] { 1L }));        // wrong arity
        Assert.False(between.Evaluate(5L, "1,10"));                       // not an array
    }

    [Fact]
    public void Weekend_And_Weekday()
    {
        var saturday = new DateTime(2021, 6, 19);
        var tuesday = new DateTime(2021, 6, 15);

        Assert.True(Fn("IsWeekend").Evaluate(saturday, null));
        Assert.True(Fn("IsWeekend").Evaluate("2021-06-19", null));  // string date
        Assert.False(Fn("IsWeekend").Evaluate(tuesday, null));
        Assert.False(Fn("IsWeekend").Evaluate("not a date", null));

        Assert.True(Fn("IsWeekday").Evaluate(tuesday, null));
        Assert.False(Fn("IsWeekday").Evaluate(saturday, null));
    }

    [Fact]
    public void InPast_And_InFuture_UseTheInjectedClock()
    {
        var clockNow = new DateTime(2021, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        var functions = BuiltInFunctions.Create(() => clockNow);
        IRuleFunction inPast = functions.Single(f => f.Name == "IsInPast");
        IRuleFunction inFuture = functions.Single(f => f.Name == "IsInFuture");

        Assert.True(inPast.Evaluate(new DateTime(2021, 6, 14), null));
        Assert.False(inPast.Evaluate(new DateTime(2021, 6, 16), null));
        Assert.True(inFuture.Evaluate(new DateTime(2021, 6, 16), null));
        Assert.False(inFuture.Evaluate(new DateTime(2021, 6, 14), null));
        Assert.False(inPast.Evaluate("not a date", null));
    }

    [Fact]
    public void NamedRuleFunction_RejectsEmptyNameAndNullPredicate()
    {
        Assert.Throws<ArgumentException>(() => new NamedRuleFunction(string.Empty, (f, v) => true));
        Assert.Throws<ArgumentNullException>(() => new NamedRuleFunction("x", null!));
    }

    [Fact]
    public void IsBetweenInclusive_FromJsonRule_CompiledAndInterpreted()
    {
        // The array-valued custom operand has to survive the JSON parser, not just a direct
        // IRuleFunction.Evaluate call — that is the only way a consumer reaches this function.
        const string rule =
            "{\"field\":\"Customer.Age\",\"operator\":\"custom\",\"name\":\"IsBetweenInclusive\",\"value\":[18,65]}";

        OrderFact poco = DefaultFact();
        poco.Customer.Age = 21;
        Assert.True(Matches(rule, poco));

        poco.Customer.Age = 70;
        Assert.False(Matches(rule, poco));

        Assert.True(Matches(rule, new Dictionary<string, object?>
        {
            ["Customer"] = new Dictionary<string, object?> { ["Age"] = 21L },
        }));
        Assert.False(Matches(rule, new Dictionary<string, object?>
        {
            ["Customer"] = new Dictionary<string, object?> { ["Age"] = 70L },
        }));
    }

    /// <summary>
    /// Date relativity is about an instant, not a wall clock. A DateTimeOffset carries its own
    /// offset and a DateTime carries a Kind; both must be resolved to UTC before comparing against
    /// the clock, or the answer is wrong by the size of the offset.
    /// </summary>
    [Fact]
    public void InPast_And_InFuture_ResolveOffsetsAndKindsToUtc()
    {
        var clockNow = new DateTime(2021, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        var functions = BuiltInFunctions.Create(() => clockNow);
        IRuleFunction inPast = functions.Single(f => f.Name == "IsInPast");
        IRuleFunction inFuture = functions.Single(f => f.Name == "IsInFuture");

        // 23:00 at +12:00 is 11:00 UTC - one hour before the clock, however the wall clock reads.
        var pastAtPlusTwelve = new DateTimeOffset(2021, 6, 15, 23, 0, 0, TimeSpan.FromHours(12));
        Assert.True(inPast.Evaluate(pastAtPlusTwelve, null));
        Assert.False(inFuture.Evaluate(pastAtPlusTwelve, null));

        // 01:00 at -12:00 is 13:00 UTC - one hour after the clock.
        var futureAtMinusTwelve = new DateTimeOffset(2021, 6, 15, 1, 0, 0, TimeSpan.FromHours(-12));
        Assert.True(inFuture.Evaluate(futureAtMinusTwelve, null));
        Assert.False(inPast.Evaluate(futureAtMinusTwelve, null));

        // An offset-bearing string constant resolves the same way.
        Assert.True(inPast.Evaluate("2021-06-15T23:00:00+12:00", null));
        Assert.True(inFuture.Evaluate("2021-06-15T01:00:00-12:00", null));

        // A Local DateTime is an instant too: five hours before the clock is in the past in every
        // time zone, not only in UTC.
        DateTime localFiveHoursBefore = new DateTime(clockNow.Ticks, DateTimeKind.Utc).AddHours(-5).ToLocalTime();
        Assert.Equal(DateTimeKind.Local, localFiveHoursBefore.Kind);
        Assert.True(inPast.Evaluate(localFiveHoursBefore, null));
        Assert.False(inFuture.Evaluate(localFiveHoursBefore, null));
    }

    /// <summary>
    /// A clock that reports local time must not shift the comparison - the engine normalizes both
    /// sides, so the same fact answers the same way whatever Kind the clock hands back.
    /// </summary>
    [Fact]
    public void InPast_NormalizesTheClockItself()
    {
        var instant = new DateTime(2021, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        IRuleFunction utcClock = BuiltInFunctions.Create(() => instant).Single(f => f.Name == "IsInPast");
        IRuleFunction localClock = BuiltInFunctions.Create(() => instant.ToLocalTime()).Single(f => f.Name == "IsInPast");

        var subject = new DateTimeOffset(2021, 6, 15, 11, 0, 0, TimeSpan.Zero);
        Assert.Equal(utcClock.Evaluate(subject, null), localClock.Evaluate(subject, null));
        Assert.True(utcClock.Evaluate(subject, null));
    }

    /// <summary>
    /// The calendar predicates stay wall-clock: "placed on a Saturday" means the Saturday where it
    /// happened, so a DateTimeOffset keeps its own local date rather than being shifted to UTC.
    /// </summary>
    [Fact]
    public void Weekend_UsesTheWallClockDate_NotUtc()
    {
        // Saturday 01:00 at +12:00 is still Friday 13:00 UTC.
        var saturdayAtPlusTwelve = new DateTimeOffset(2021, 6, 19, 1, 0, 0, TimeSpan.FromHours(12));
        Assert.True(Fn("IsWeekend").Evaluate(saturdayAtPlusTwelve, null));
        Assert.False(Fn("IsWeekday").Evaluate(saturdayAtPlusTwelve, null));
    }
}
