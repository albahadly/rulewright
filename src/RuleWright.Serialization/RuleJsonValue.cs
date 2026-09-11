using System;
using System.Collections.Generic;
using System.Globalization;

namespace RuleWright.Serialization;

/// <summary>
/// A minimal, immutable, library-neutral JSON value tree. JSON reader adapters
/// (System.Text.Json, Newtonsoft.Json, …) translate their own DOM into this type,
/// which keeps <c>RuleWright.Serialization</c> free of any JSON library dependency.
/// </summary>
public sealed class RuleJsonValue
{
    private static readonly IReadOnlyList<KeyValuePair<string, RuleJsonValue>> EmptyProperties =
        Array.Empty<KeyValuePair<string, RuleJsonValue>>();

    private static readonly IReadOnlyList<RuleJsonValue> EmptyItems = Array.Empty<RuleJsonValue>();

    private readonly Dictionary<string, RuleJsonValue>? _propertyLookup;
    private readonly string? _stringValue;
    private readonly string? _rawNumber;

    private RuleJsonValue(
        RuleJsonValueKind kind,
        IReadOnlyList<KeyValuePair<string, RuleJsonValue>>? properties,
        IReadOnlyList<RuleJsonValue>? items,
        string? stringValue,
        string? rawNumber)
    {
        Kind = kind;
        Properties = properties ?? EmptyProperties;
        Items = items ?? EmptyItems;
        _stringValue = stringValue;
        _rawNumber = rawNumber;

        if (properties is not null)
        {
            _propertyLookup = new Dictionary<string, RuleJsonValue>(properties.Count, StringComparer.Ordinal);
            foreach (KeyValuePair<string, RuleJsonValue> property in properties)
            {
                // Last duplicate key wins, matching typical JSON DOM behavior.
                _propertyLookup[property.Key] = property.Value;
            }
        }
    }

    /// <summary>The shared JSON <c>null</c> node.</summary>
    public static RuleJsonValue Null { get; } = new RuleJsonValue(RuleJsonValueKind.Null, null, null, null, null);

    /// <summary>The shared JSON <c>true</c> node.</summary>
    public static RuleJsonValue True { get; } = new RuleJsonValue(RuleJsonValueKind.True, null, null, null, null);

    /// <summary>The shared JSON <c>false</c> node.</summary>
    public static RuleJsonValue False { get; } = new RuleJsonValue(RuleJsonValueKind.False, null, null, null, null);

    /// <summary>The node's kind.</summary>
    public RuleJsonValueKind Kind { get; }

    /// <summary>Object properties in document order; empty for non-objects.</summary>
    public IReadOnlyList<KeyValuePair<string, RuleJsonValue>> Properties { get; }

    /// <summary>Array items in document order; empty for non-arrays.</summary>
    public IReadOnlyList<RuleJsonValue> Items { get; }

    /// <summary>Creates an object node.</summary>
    /// <param name="properties">The properties, in document order.</param>
    /// <exception cref="ArgumentNullException"><paramref name="properties"/> is null or contains a null name or value.</exception>
    public static RuleJsonValue CreateObject(IEnumerable<KeyValuePair<string, RuleJsonValue>> properties)
    {
        if (properties is null)
        {
            throw new ArgumentNullException(nameof(properties));
        }

        var materialized = new List<KeyValuePair<string, RuleJsonValue>>();
        foreach (KeyValuePair<string, RuleJsonValue> property in properties)
        {
            if (property.Key is null || property.Value is null)
            {
                throw new ArgumentNullException(nameof(properties), "Property names and values must not be null.");
            }

            materialized.Add(property);
        }

        return new RuleJsonValue(RuleJsonValueKind.Object, materialized, null, null, null);
    }

    /// <summary>Creates an array node.</summary>
    /// <param name="items">The items, in document order.</param>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is null or contains null.</exception>
    public static RuleJsonValue CreateArray(IEnumerable<RuleJsonValue> items)
    {
        if (items is null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        var materialized = new List<RuleJsonValue>();
        foreach (RuleJsonValue item in items)
        {
            materialized.Add(item ?? throw new ArgumentNullException(nameof(items), "Items must not be null."));
        }

        return new RuleJsonValue(RuleJsonValueKind.Array, null, materialized, null, null);
    }

    /// <summary>Creates a string node.</summary>
    /// <param name="value">The string value.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null (use <see cref="Null"/> for JSON null).</exception>
    public static RuleJsonValue CreateString(string value)
        => new RuleJsonValue(
            RuleJsonValueKind.String,
            null,
            null,
            value ?? throw new ArgumentNullException(nameof(value)),
            null);

    /// <summary>Creates a number node from its raw JSON text (invariant culture).</summary>
    /// <param name="rawText">The number exactly as written in JSON, e.g. <c>"10.5"</c> or <c>"1e3"</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="rawText"/> is null, empty, or not a valid number.</exception>
    public static RuleJsonValue CreateNumber(string rawText)
    {
        if (string.IsNullOrEmpty(rawText))
        {
            throw new ArgumentException("Number text must not be null or empty.", nameof(rawText));
        }

        // Shape first, then range — and deliberately not double.TryParse for both. On .NET Core an
        // overflowing literal parses as infinity while on .NET Framework it simply fails, so using
        // TryParse to decide *which* complaint to make would give the same document two different
        // errors depending on the target framework. Checking the JSON grammar directly keeps the
        // two legs word for word identical.
        if (!IsJsonNumberSyntax(rawText))
        {
            throw new ArgumentException($"'{rawText}' is not a valid JSON number.", nameof(rawText));
        }

        bool parsed = double.TryParse(rawText, NumberStyles.Float, CultureInfo.InvariantCulture, out double value);
        if (!parsed || double.IsInfinity(value) || double.IsNaN(value))
        {
            throw new ArgumentException(
                $"'{rawText}' is outside the range RuleWright can represent; JSON numbers must be finite.",
                nameof(rawText));
        }

        return new RuleJsonValue(RuleJsonValueKind.Number, null, null, null, rawText);
    }

    /// <summary>
    /// Whether the text matches the JSON number grammar (RFC 8259):
    /// <c>-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?</c>. Hand-rolled rather than a regular
    /// expression because it runs once per number token in every document parsed.
    /// </summary>
    private static bool IsJsonNumberSyntax(string text)
    {
        int i = 0;
        int length = text.Length;

        if (i < length && text[i] == '-')
        {
            i++;
        }

        // Integer part: a lone zero, or a non-zero digit followed by any digits.
        if (i >= length || !IsDigit(text[i]))
        {
            return false;
        }

        if (text[i] == '0')
        {
            i++;
        }
        else
        {
            while (i < length && IsDigit(text[i]))
            {
                i++;
            }
        }

        // Optional fraction: a point followed by at least one digit.
        if (i < length && text[i] == '.')
        {
            i++;
            if (i >= length || !IsDigit(text[i]))
            {
                return false;
            }

            while (i < length && IsDigit(text[i]))
            {
                i++;
            }
        }

        // Optional exponent: e or E, an optional sign, then at least one digit.
        if (i < length && (text[i] == 'e' || text[i] == 'E'))
        {
            i++;
            if (i < length && (text[i] == '+' || text[i] == '-'))
            {
                i++;
            }

            if (i >= length || !IsDigit(text[i]))
            {
                return false;
            }

            while (i < length && IsDigit(text[i]))
            {
                i++;
            }
        }

        return i == length;
    }

    private static bool IsDigit(char c) => c >= '0' && c <= '9';

    /// <summary>Creates a number node from an integer.</summary>
    /// <param name="value">The value.</param>
    public static RuleJsonValue CreateNumber(long value)
        => new RuleJsonValue(RuleJsonValueKind.Number, null, null, null, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Creates a number node from a double.</summary>
    /// <param name="value">The value; must be finite.</param>
    /// <exception cref="ArgumentException"><paramref name="value"/> is NaN or infinite.</exception>
    public static RuleJsonValue CreateNumber(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentException("JSON numbers must be finite.", nameof(value));
        }

        return new RuleJsonValue(RuleJsonValueKind.Number, null, null, null, value.ToString("R", CultureInfo.InvariantCulture));
    }

    /// <summary>Creates a number node from a decimal.</summary>
    /// <param name="value">The value.</param>
    public static RuleJsonValue CreateNumber(decimal value)
        => new RuleJsonValue(RuleJsonValueKind.Number, null, null, null, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Creates a boolean node.</summary>
    /// <param name="value">The value.</param>
    public static RuleJsonValue CreateBoolean(bool value) => value ? True : False;

    /// <summary>Looks up an object property by exact (ordinal) name.</summary>
    /// <param name="name">The property name.</param>
    /// <param name="value">The property value when found.</param>
    /// <returns>Whether the property exists (only ever true for object nodes).</returns>
    public bool TryGetProperty(string name, out RuleJsonValue value)
    {
        if (_propertyLookup is not null && name is not null && _propertyLookup.TryGetValue(name, out RuleJsonValue? found))
        {
            value = found;
            return true;
        }

        value = Null;
        return false;
    }

    /// <summary>The string value of a <see cref="RuleJsonValueKind.String"/> node.</summary>
    /// <exception cref="InvalidOperationException">The node is not a string.</exception>
    public string GetString()
        => _stringValue ?? throw new InvalidOperationException($"Node is {Kind}, not a string.");

    /// <summary>The raw invariant-culture number text of a <see cref="RuleJsonValueKind.Number"/> node.</summary>
    /// <exception cref="InvalidOperationException">The node is not a number.</exception>
    public string GetRawNumber()
        => _rawNumber ?? throw new InvalidOperationException($"Node is {Kind}, not a number.");

    /// <summary>
    /// Whether this number node holds an integral value representable as <see cref="long"/>.
    /// </summary>
    /// <param name="value">The integral value when representable.</param>
    /// <returns>False for non-number nodes and non-integral or out-of-range numbers.</returns>
    public bool TryGetInt64(out long value)
    {
        value = 0;
        return _rawNumber is not null
            && long.TryParse(_rawNumber, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// Converts a scalar node to its CLR value: string, bool, null, or a number as
    /// <see cref="long"/> when integral, otherwise <see cref="decimal"/> when exactly
    /// representable, otherwise <see cref="double"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The node is an object or array.</exception>
    public object? ToClrValue()
    {
        switch (Kind)
        {
            case RuleJsonValueKind.Null:
                return null;
            case RuleJsonValueKind.True:
                return true;
            case RuleJsonValueKind.False:
                return false;
            case RuleJsonValueKind.String:
                return _stringValue;
            case RuleJsonValueKind.Number:
                if (long.TryParse(_rawNumber, NumberStyles.Integer, CultureInfo.InvariantCulture, out long integral))
                {
                    return integral;
                }

                if (decimal.TryParse(_rawNumber, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal exact))
                {
                    return exact;
                }

                return double.Parse(_rawNumber!, NumberStyles.Float, CultureInfo.InvariantCulture);
            default:
                throw new InvalidOperationException($"A {Kind} node has no scalar CLR value.");
        }
    }
}
