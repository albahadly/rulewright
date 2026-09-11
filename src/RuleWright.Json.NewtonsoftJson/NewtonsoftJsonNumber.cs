using System;
using System.Globalization;
using System.Numerics;
using Newtonsoft.Json.Linq;

namespace RuleWright.Json.NewtonsoftJson;

/// <summary>
/// Renders a Newtonsoft numeric token as invariant raw text equivalent to what the
/// System.Text.Json adapter would see from the same JSON, so both adapters feed the neutral DOM's
/// number policy (long when integral, else decimal when representable, else double) the same input.
///
/// Newtonsoft materializes numbers into CLR types (long / <see cref="BigInteger"/> / double) and
/// does not expose the original JSON text. For typical rule values this is exact; only
/// floating-point values outside <see cref="decimal"/>'s range or precision (very large or very
/// small exponents, or more than ~28 significant digits) can differ slightly from the
/// System.Text.Json adapter, which preserves the raw text.
/// </summary>
internal static class NewtonsoftJsonNumber
{
    internal static string ToRawText(JValue value)
    {
        switch (value.Value)
        {
            case long l:
                return l.ToString(CultureInfo.InvariantCulture);
            case int i:
                return i.ToString(CultureInfo.InvariantCulture);
            case short or sbyte or byte or ushort or uint:
                return Convert.ToInt64(value.Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
            case ulong ul:
                return ul.ToString(CultureInfo.InvariantCulture);
            case BigInteger bi:
                return bi.ToString(CultureInfo.InvariantCulture);
            case decimal m:
                return NonIntegral(m.ToString(CultureInfo.InvariantCulture));
            case double d:
                return FromDouble(d);
            case float f:
                return FromDouble(f);
            default:
                return Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? "0";
        }
    }

    private static string FromDouble(double d)
    {
        // A JSON float token must stay non-integral — System.Text.Json keeps e.g. "2.0" as a
        // decimal, never a long — and collapse to decimal when representable to preserve precision.
        try
        {
            return NonIntegral(((decimal)d).ToString(CultureInfo.InvariantCulture));
        }
        catch (OverflowException)
        {
            // Outside decimal's range (e.g. 1e300): fall back to round-trippable double text.
            return d.ToString("R", CultureInfo.InvariantCulture);
        }
    }

    // Keep a float token's text out of the long-parseable range so the DOM's policy classifies it
    // as decimal/double, exactly as it would the System.Text.Json raw text for the same token.
    private static string NonIntegral(string text)
        => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ? text + ".0" : text;
}
