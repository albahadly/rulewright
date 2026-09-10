using System;
using System.Collections.Generic;
using System.Linq;

namespace Rulewright.Core;

/// <summary>
/// An ordered collection of rules parsed from a single JSON document.
/// Immutable after construction.
/// </summary>
public sealed class RuleSet
{
    /// <summary>
    /// Creates a rule set.
    /// </summary>
    /// <param name="rules">The rules, in document order.</param>
    /// <param name="name">Optional display name.</param>
    /// <param name="stopAfterFirstMatch">
    /// When true, evaluation stops after the first rule whose condition passes, exactly as
    /// <see cref="EvaluationOptions.StopOnFirstMatch"/> does. This is the set's own semantics
    /// rather than the caller's — a <c>first</c>-hit-policy decision table sets it — and the two
    /// combine with OR, so a caller can still stop a <c>collect</c> set early but cannot turn a
    /// <c>first</c> table into a collecting one.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="rules"/> is null or contains null.</exception>
    /// <exception cref="ArgumentException"><paramref name="rules"/> is empty or contains duplicate rule ids.</exception>
    public RuleSet(IEnumerable<Rule> rules, string? name = null, bool stopAfterFirstMatch = false)
    {
        if (rules is null)
        {
            throw new ArgumentNullException(nameof(rules));
        }

        Rule[] materialized = rules.ToArray();
        if (materialized.Length == 0)
        {
            throw new ArgumentException("A rule set must contain at least one rule.", nameof(rules));
        }

        if (materialized.Any(r => r is null))
        {
            throw new ArgumentNullException(nameof(rules), "Rules must not contain null.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Rule rule in materialized)
        {
            if (!seen.Add(rule.Id))
            {
                throw new ArgumentException($"Duplicate rule id '{rule.Id}'.", nameof(rules));
            }
        }

        Rules = materialized;
        Name = name;
        StopAfterFirstMatch = stopAfterFirstMatch;
    }

    /// <summary>Optional display name.</summary>
    public string? Name { get; }

    /// <summary>The rules, in document order.</summary>
    public IReadOnlyList<Rule> Rules { get; }

    /// <summary>
    /// Whether evaluation stops after the first matching rule regardless of the caller's
    /// <see cref="EvaluationOptions.StopOnFirstMatch"/> — the <c>first</c> hit policy of a
    /// decision table, carried on the set the table expanded into.
    /// </summary>
    public bool StopAfterFirstMatch { get; }
}
