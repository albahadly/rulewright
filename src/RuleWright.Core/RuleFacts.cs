using System;
using System.Collections;
using System.Collections.Generic;

namespace RuleWright.Core;

/// <summary>
/// Several named facts evaluated as one — the multi-input shape. Each fact is addressed by
/// its name as the first segment of a field path: with
/// <c>RuleFacts.With("customer", c).And("order", o)</c>, a rule reads
/// <c>customer.Age</c> and <c>order.Total</c>.
///
/// <para>A <see cref="RuleFacts"/> is a dictionary fact, so it runs the interpreter
/// (<c>CompilationMode.Interpreted</c>) — the set of member types isn't a single CLR shape to
/// compile against. Facts inside it are still read through the interpreter's cached
/// reflection. When evaluation is hot enough that the compiled path matters, wrap the same
/// inputs in a composite POCO (<c>class Checkout { Customer Customer; Order Order; }</c>)
/// instead; the two spell the same field paths.</para>
/// </summary>
public sealed class RuleFacts : IReadOnlyDictionary<string, object?>
{
    private readonly Dictionary<string, object?> _facts;

    private RuleFacts(bool ignoreCase)
        => _facts = new Dictionary<string, object?>(
            ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    /// <summary>
    /// Starts a named-facts collection with its first fact.
    /// </summary>
    /// <param name="name">The name rules address this fact by (the first path segment).</param>
    /// <param name="fact">The fact instance; may be null (rules then see it as absent).</param>
    /// <param name="ignoreCase">
    /// Whether fact names (this level only) match case-insensitively. Facts inside follow the
    /// ordinary resolution rules: POCO members are case-insensitive, dictionary keys use their
    /// own comparer.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null or empty.</exception>
    public static RuleFacts With(string name, object? fact, bool ignoreCase = false)
        => new RuleFacts(ignoreCase).And(name, fact);

    /// <summary>
    /// Adds another named fact.
    /// </summary>
    /// <param name="name">The name rules address this fact by.</param>
    /// <param name="fact">The fact instance; may be null.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null or empty, or already added.</exception>
    public RuleFacts And(string name, object? fact)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("Fact name must not be null or empty.", nameof(name));
        }

        if (_facts.ContainsKey(name))
        {
            throw new ArgumentException($"A fact named '{name}' was already added.", nameof(name));
        }

        _facts.Add(name, fact);
        return this;
    }

    /// <inheritdoc />
    public int Count => _facts.Count;

    /// <inheritdoc />
    public IEnumerable<string> Keys => _facts.Keys;

    /// <inheritdoc />
    public IEnumerable<object?> Values => _facts.Values;

    /// <inheritdoc />
    public object? this[string key] => _facts[key];

    /// <inheritdoc />
    public bool ContainsKey(string key) => _facts.ContainsKey(key);

    /// <inheritdoc />
    public bool TryGetValue(string key, out object? value) => _facts.TryGetValue(key, out value);

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => _facts.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
