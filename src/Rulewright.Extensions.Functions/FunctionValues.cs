using System;
using System.Globalization;

namespace Rulewright.Extensions.Functions;

/// <summary>
/// Tolerant, total coercions used by the built-in functions. Like the rest of the engine, they
/// never throw on data: a value that isn't of the expected shape simply yields <c>false</c>, so a
/// predicate can never crash an evaluation. Numeric coercions are strictly numeric — a string is
/// not treated as a number, since a JSON number field already resolves to a numeric CLR type.
/// </summary>
internal static class FunctionValues
{
    internal static bool TryToInt64(object? value, out long result)
    {
        switch (value)
        {
            case sbyte or byte or short or ushort or int or uint or long:
                result = Convert.ToInt64(value, CultureInfo.InvariantCulture);
                return true;
            case ulong ul when ul <= long.MaxValue:
                result = (long)ul;
                return true;
            case decimal m when m == decimal.Truncate(m) && m >= long.MinValue && m <= long.MaxValue:
                result = (long)m;
                return true;
            case double d when IsIntegral(d):
                result = (long)d;
                return true;
            case float f when IsIntegral(f):
                result = (long)f;
                return true;
            default:
                result = 0;
                return false;
        }
    }

    internal static bool TryToDouble(object? value, out double result)
    {
        switch (value)
        {
            case sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal:
                result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return true;
            default:
                result = 0;
                return false;
        }
    }

    /// <summary>
    /// The calendar reading of a value: the wall-clock date and time as written, so "placed on a
    /// Saturday" means the Saturday where it happened. A <see cref="DateTimeOffset"/> therefore
    /// keeps its own local date rather than being shifted to UTC. Use
    /// <see cref="TryToInstant"/> instead for anything compared against a clock.
    /// </summary>
    internal static bool TryToDateTime(object? value, out DateTime result)
    {
        switch (value)
        {
            case DateTime dateTime:
                result = dateTime;
                return true;
            case DateTimeOffset dateTimeOffset:
                result = dateTimeOffset.DateTime;
                return true;
            case string text when DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out result):
                return true;
            default:
                result = default;
                return false;
        }
    }

    /// <summary>
    /// The instant a value denotes, as UTC — the reading date <em>relativity</em> needs. A wall
    /// clock is not an instant: 23:00 at +12:00 is 11:00 UTC, and a <see cref="DateTimeKind.Local"/>
    /// value is offset from UTC by the machine's zone, so both must be resolved before comparing
    /// against a clock or the answer is wrong by the size of the offset.
    /// </summary>
    internal static bool TryToInstant(object? value, out DateTime utc)
    {
        switch (value)
        {
            case DateTime dateTime:
                utc = ToUtc(dateTime);
                return true;
            case DateTimeOffset dateTimeOffset:
                utc = dateTimeOffset.UtcDateTime;
                return true;
            case string text when DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsed):
                // RoundtripKind carries an offset-bearing string into a Local (or Utc) value, which
                // ToUtc then resolves back to the instant the string named.
                utc = ToUtc(parsed);
                return true;
            default:
                utc = default;
                return false;
        }
    }

    /// <summary>
    /// Resolves a <see cref="DateTime"/> to UTC. An <see cref="DateTimeKind.Unspecified"/> value is
    /// read as UTC rather than as machine-local: a rule and a fact must not answer differently
    /// depending on which machine evaluates them.
    /// </summary>
    internal static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    // Finite, whole-valued, and within long's range so the (long) cast is safe.
    private static bool IsIntegral(double d)
        => !double.IsNaN(d) && !double.IsInfinity(d)
        && Math.Floor(d) == d
        && d >= -9.2233720368547758E18 && d <= 9.2233720368547758E18;
}
