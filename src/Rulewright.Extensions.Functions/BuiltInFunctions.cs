using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Rulewright.Core;

namespace Rulewright.Extensions.Functions;

/// <summary>
/// A small, curated catalog of ready-made <see cref="IRuleFunction"/> predicates for the
/// <c>custom</c> operator — the things the closed built-in operator vocabulary deliberately does
/// not cover: case-insensitive comparison, emptiness/format checks, number parity and
/// divisibility, and date relativity. Every predicate is <em>total</em>: given a value of an
/// unexpected shape it returns <c>false</c> rather than throwing.
///
/// <para>Reference one by <c>Name</c> in a rule leaf, e.g.
/// <c>{ "operator": "custom", "name": "IsEven", "field": "Order.ItemCount" }</c>, after
/// registering the set with <see cref="RulewrightFunctionExtensions.RegisterBuiltInFunctions"/>.</para>
///
/// <list type="table">
///   <item><term>IsNullOrEmpty</term><description>field is null or an empty string</description></item>
///   <item><term>IsNullOrWhiteSpace</term><description>field is null, empty, or whitespace</description></item>
///   <item><term>EqualsIgnoreCase</term><description>field equals the value ignoring case (ordinal)</description></item>
///   <item><term>IsEmail</term><description>field looks like an email address</description></item>
///   <item><term>IsEven / IsOdd</term><description>field is an even / odd integer</description></item>
///   <item><term>IsPositive / IsNegative</term><description>field is a number &gt; 0 / &lt; 0</description></item>
///   <item><term>DivisibleBy</term><description>field is an integer divisible by the value</description></item>
///   <item><term>IsBetweenInclusive</term><description>field is within the [min, max] value array</description></item>
///   <item><term>IsWeekend / IsWeekday</term><description>field date falls on a weekend / weekday</description></item>
///   <item><term>IsInPast / IsInFuture</term><description>field date is before / after now</description></item>
/// </list>
/// </summary>
public static class BuiltInFunctions
{
    private static readonly Regex EmailPattern =
        new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// The built-in functions, using the system clock (<see cref="DateTime.UtcNow"/>) for the
    /// date-relativity predicates.
    /// </summary>
    public static IReadOnlyList<IRuleFunction> All { get; } = Create();

    /// <summary>
    /// Creates the built-in functions with a custom clock for the date-relativity predicates
    /// (<c>IsInPast</c>, <c>IsInFuture</c>) — useful for deterministic tests.
    /// </summary>
    /// <param name="clock">Supplies "now"; defaults to <see cref="DateTime.UtcNow"/> when null.</param>
    public static IReadOnlyList<IRuleFunction> Create(Func<DateTime>? clock = null)
    {
        Func<DateTime> now = clock ?? (() => DateTime.UtcNow);

        var functions = new IRuleFunction[]
        {
            // String
            new NamedRuleFunction("IsNullOrEmpty", (field, value) => field is null || (field is string s && s.Length == 0),
                "Field is null or an empty string.", RuleFunctionValueKind.None),
            new NamedRuleFunction("IsNullOrWhiteSpace", (field, value) => field is null || (field is string s && string.IsNullOrWhiteSpace(s)),
                "Field is null, empty, or whitespace.", RuleFunctionValueKind.None),
            new NamedRuleFunction("EqualsIgnoreCase", (field, value) => field is string a && value is string b
                ? string.Equals(a, b, StringComparison.OrdinalIgnoreCase)
                : field is null && value is null,
                "Field equals the value, ignoring case (ordinal).", RuleFunctionValueKind.Text),
            new NamedRuleFunction("IsEmail", (field, value) => field is string s && EmailPattern.IsMatch(s),
                "Field looks like an email address.", RuleFunctionValueKind.None),

            // Numeric
            new NamedRuleFunction("IsEven", (field, value) => FunctionValues.TryToInt64(field, out long n) && n % 2 == 0,
                "Field is an even integer.", RuleFunctionValueKind.None),
            new NamedRuleFunction("IsOdd", (field, value) => FunctionValues.TryToInt64(field, out long n) && n % 2 != 0,
                "Field is an odd integer.", RuleFunctionValueKind.None),
            new NamedRuleFunction("IsPositive", (field, value) => FunctionValues.TryToDouble(field, out double d) && d > 0,
                "Field is a number greater than zero.", RuleFunctionValueKind.None),
            new NamedRuleFunction("IsNegative", (field, value) => FunctionValues.TryToDouble(field, out double d) && d < 0,
                "Field is a number less than zero.", RuleFunctionValueKind.None),
            new NamedRuleFunction("DivisibleBy", (field, value) =>
                FunctionValues.TryToInt64(field, out long n)
                && FunctionValues.TryToInt64(value, out long divisor)
                && divisor != 0
                && n % divisor == 0,
                "Field is an integer divisible by the value.", RuleFunctionValueKind.Scalar),
            new NamedRuleFunction("IsBetweenInclusive", (field, value) =>
                value is object?[] bounds
                && bounds.Length == 2
                && FunctionValues.TryToDouble(field, out double f)
                && FunctionValues.TryToDouble(bounds[0], out double min)
                && FunctionValues.TryToDouble(bounds[1], out double max)
                && f >= min && f <= max,
                "Field is within the [min, max] value array (inclusive).", RuleFunctionValueKind.Array),

            // Date / time
            new NamedRuleFunction("IsWeekend", (field, value) =>
                FunctionValues.TryToDateTime(field, out DateTime dt)
                && (dt.DayOfWeek == DayOfWeek.Saturday || dt.DayOfWeek == DayOfWeek.Sunday),
                "Field date falls on a Saturday or Sunday.", RuleFunctionValueKind.None),
            new NamedRuleFunction("IsWeekday", (field, value) =>
                FunctionValues.TryToDateTime(field, out DateTime dt)
                && dt.DayOfWeek != DayOfWeek.Saturday && dt.DayOfWeek != DayOfWeek.Sunday,
                "Field date falls on a weekday.", RuleFunctionValueKind.None),
            // Relativity compares instants, not wall clocks: both the field and the clock are
            // resolved to UTC first, so an offset or a Local Kind cannot skew the answer.
            new NamedRuleFunction("IsInPast", (field, value) => FunctionValues.TryToInstant(field, out DateTime dt) && dt < FunctionValues.ToUtc(now()),
                "Field date is before now.", RuleFunctionValueKind.None),
            new NamedRuleFunction("IsInFuture", (field, value) => FunctionValues.TryToInstant(field, out DateTime dt) && dt > FunctionValues.ToUtc(now()),
                "Field date is after now.", RuleFunctionValueKind.None),
        };

        return new ReadOnlyCollection<IRuleFunction>(functions);
    }
}
