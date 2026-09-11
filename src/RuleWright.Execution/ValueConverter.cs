using System;
using System.Globalization;

namespace RuleWright.Execution;

/// <summary>
/// Compile-time conversion of JSON-sourced constants (long/decimal/double/string/bool)
/// to a typed field's CLR type, so compiled comparisons operate on unboxed, exactly-typed
/// operands. All conversions happen once at rule compile time, never per evaluation.
/// </summary>
internal static class ValueConverter
{
    internal static bool IsNumericType(Type type)
        => type == typeof(sbyte) || type == typeof(byte)
        || type == typeof(short) || type == typeof(ushort)
        || type == typeof(int) || type == typeof(uint)
        || type == typeof(long) || type == typeof(ulong)
        || type == typeof(float) || type == typeof(double)
        || type == typeof(decimal);

    internal static bool IsNumericValue(object? value)
        => value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;

    /// <summary>
    /// Attempts to convert a numeric constant to <paramref name="targetType"/> without
    /// loss (no silent rounding of 10.5 into an int field). Returns false when the value
    /// is not exactly representable — callers then either widen the comparison or, for
    /// equality, fold to a constant since exact equality is impossible.
    /// </summary>
    internal static bool TryConvertNumberExact(object value, Type targetType, out object converted)
    {
        converted = value;
        try
        {
            if (targetType == typeof(double))
            {
                converted = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return true;
            }

            if (targetType == typeof(float))
            {
                float single = Convert.ToSingle(value, CultureInfo.InvariantCulture);
                if (float.IsInfinity(single))
                {
                    return false;
                }

                converted = single;
                return true;
            }

            if (targetType == typeof(decimal))
            {
                converted = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                return true;
            }

            decimal number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            if (number != decimal.Truncate(number))
            {
                return false;
            }

            converted = Convert.ChangeType(number, targetType, CultureInfo.InvariantCulture);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
        catch (InvalidCastException)
        {
            return false;
        }
    }

    /// <summary>
    /// Converts a non-numeric constant to the field's type: strings to enums, dates,
    /// times, GUIDs, or chars; pass-through for already-assignable values. Throws
    /// <see cref="InvalidOperationException"/> with a consumer-actionable message on
    /// failure (callers wrap it with rule context into a RuleCompilationException).
    /// </summary>
    internal static object ConvertTo(object value, Type targetType)
    {
        if (targetType.IsInstanceOfType(value))
        {
            return value;
        }

        if (targetType.IsEnum)
        {
            if (value is string enumName)
            {
                try
                {
                    return Enum.Parse(targetType, enumName, ignoreCase: false);
                }
                catch (ArgumentException)
                {
                    throw new InvalidOperationException(
                        $"'{enumName}' is not a member of enum {targetType.Name}.");
                }
            }

            if (IsNumericValue(value) && TryConvertNumberExact(value, Enum.GetUnderlyingType(targetType), out object underlying))
            {
                return Enum.ToObject(targetType, underlying);
            }
        }

        if (value is string text)
        {
            try
            {
                if (targetType == typeof(DateTime))
                {
                    return DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                }

                if (targetType == typeof(DateTimeOffset))
                {
                    return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                }

                if (targetType == typeof(TimeSpan))
                {
                    return TimeSpan.Parse(text, CultureInfo.InvariantCulture);
                }

                if (targetType == typeof(Guid))
                {
                    return Guid.Parse(text);
                }

                if (targetType == typeof(char) && text.Length == 1)
                {
                    return text[0];
                }
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException(
                    $"Cannot parse \"{text}\" as {targetType.Name}: {ex.Message}");
            }
        }

        if (IsNumericType(targetType) && value is string)
        {
            throw new InvalidOperationException(
                $"Field type {targetType.Name} is numeric but the comparison value is the string \"{value}\". "
                + "Use a JSON number — RuleWright does not coerce strings to numbers.");
        }

        throw new InvalidOperationException(
            $"Cannot convert comparison value of type {value.GetType().Name} to field type {targetType.Name}.");
    }
}
