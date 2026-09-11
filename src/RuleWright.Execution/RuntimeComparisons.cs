using System;
using System.Globalization;

namespace RuleWright.Execution;

/// <summary>
/// Runtime value comparisons for the dynamic-fact interpreter, matching the semantics
/// the expression compiler bakes in at compile time: numeric values compare across
/// numeric types, strings compare ordinally, and DateTime fields compare against
/// ISO-formatted string constants.
/// </summary>
internal static class RuntimeComparisons
{
    internal static bool AreEqual(object? left, object? right)
    {
        if (left is null && right is null)
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        if (ValueConverter.IsNumericValue(left) && ValueConverter.IsNumericValue(right))
        {
            return CompareNumbers(left, right) == 0;
        }

        if (left is string leftText && right is string rightText)
        {
            return string.Equals(leftText, rightText, StringComparison.Ordinal);
        }

        if (TryNormalizeDates(ref left, ref right))
        {
            return left.Equals(right);
        }

        return left.Equals(right);
    }

    internal static int? TryCompare(object? left, object? right)
    {
        if (left is null || right is null)
        {
            return null;
        }

        if (ValueConverter.IsNumericValue(left) && ValueConverter.IsNumericValue(right))
        {
            return CompareNumbers(left, right);
        }

        if (left is string leftText && right is string rightText)
        {
            return string.CompareOrdinal(leftText, rightText);
        }

        TryNormalizeDates(ref left, ref right);

        if (left.GetType() == right.GetType() && left is IComparable comparable)
        {
            return comparable.CompareTo(right);
        }

        return null;
    }

    private static int CompareNumbers(object left, object right)
    {
        if (left is double or float || right is double or float)
        {
            return Convert.ToDouble(left, CultureInfo.InvariantCulture)
                .CompareTo(Convert.ToDouble(right, CultureInfo.InvariantCulture));
        }

        try
        {
            return Convert.ToDecimal(left, CultureInfo.InvariantCulture)
                .CompareTo(Convert.ToDecimal(right, CultureInfo.InvariantCulture));
        }
        catch (OverflowException)
        {
            return Convert.ToDouble(left, CultureInfo.InvariantCulture)
                .CompareTo(Convert.ToDouble(right, CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// When one side is a DateTime/DateTimeOffset and the other an ISO string (the shape
    /// JSON rule constants arrive in), parses the string so both sides share a type.
    /// </summary>
    private static bool TryNormalizeDates(ref object left, ref object right)
    {
        if (left is DateTime && right is string rightDateText
            && DateTime.TryParse(rightDateText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsedRight))
        {
            right = parsedRight;
            return true;
        }

        if (right is DateTime && left is string leftDateText
            && DateTime.TryParse(leftDateText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsedLeft))
        {
            left = parsedLeft;
            return true;
        }

        if (left is DateTimeOffset && right is string rightOffsetText
            && DateTimeOffset.TryParse(rightOffsetText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsedRightOffset))
        {
            right = parsedRightOffset;
            return true;
        }

        if (right is DateTimeOffset && left is string leftOffsetText
            && DateTimeOffset.TryParse(leftOffsetText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsedLeftOffset))
        {
            left = parsedLeftOffset;
            return true;
        }

        return false;
    }
}
