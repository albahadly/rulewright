using System;
using System.Collections.Generic;
using Rulewright.Core;
using Rulewright.Serialization;

namespace Rulewright.Execution;

/// <summary>
/// Fluent entry point for configuring and creating a <see cref="RulewrightEngine"/>.
/// </summary>
/// <example>
/// <code>
/// var engine = new RulewrightBuilder()
///     .UseJsonReader(new SystemTextJsonReader())
///     .RegisterFunction("IsBusinessDay", (fieldValue, value) => ...)
///     .Build();
/// </code>
/// </example>
public sealed class RulewrightBuilder
{
    /// <summary>
    /// The default <c>MatchesRegex</c> match timeout: one second. A pattern with catastrophic
    /// backtracking runs against consumer-supplied fact data, so matching is bounded rather than
    /// able to pin a thread indefinitely; exceeding the bound raises
    /// <see cref="System.Text.RegularExpressions.RegexMatchTimeoutException"/>.
    /// </summary>
    public static TimeSpan DefaultRegexTimeout { get; } = TimeSpan.FromSeconds(1);

    private readonly Dictionary<string, IRuleFunction> _functions =
        new Dictionary<string, IRuleFunction>(StringComparer.Ordinal);

    private IRuleJsonReader? _jsonReader;
    private TimeSpan _regexTimeout = DefaultRegexTimeout;

    /// <summary>
    /// Sets the JSON reader adapter used by <see cref="RulewrightEngine.LoadRuleSet(string)"/>.
    /// Required before loading JSON; engines built without one can still load domain
    /// <see cref="RuleSet"/> instances directly.
    /// </summary>
    /// <param name="reader">The adapter, e.g. <c>SystemTextJsonReader</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is null.</exception>
    public RulewrightBuilder UseJsonReader(IRuleJsonReader reader)
    {
        _jsonReader = reader ?? throw new ArgumentNullException(nameof(reader));
        return this;
    }

    /// <summary>
    /// Sets the per-match time limit for the <c>MatchesRegex</c> operator, overriding
    /// <see cref="DefaultRegexTimeout"/>. Applies to both execution paths; a match that exceeds it
    /// raises <see cref="System.Text.RegularExpressions.RegexMatchTimeoutException"/> rather than
    /// running unbounded.
    /// </summary>
    /// <param name="timeout">
    /// A positive timeout below <see cref="System.Text.RegularExpressions.Regex.InfiniteMatchTimeout"/>'s
    /// upper bound (about 24 days). An infinite timeout is deliberately not accepted.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is not a positive, finite duration Regex accepts.</exception>
    public RulewrightBuilder UseRegexTimeout(TimeSpan timeout)
    {
        // Rejected here rather than at first rule compile, so a misconfiguration surfaces at the
        // call that caused it. Regex's own ceiling is int.MaxValue milliseconds.
        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                "The regex match timeout must be a positive duration under about 24 days; "
                + "an unbounded match can pin a thread indefinitely.");
        }

        _regexTimeout = timeout;
        return this;
    }

    /// <summary>
    /// Registers a custom condition function referenced from JSON as
    /// <c>{ "operator": "custom", "name": "..." }</c>. The delegate receives the resolved
    /// field value (or the whole fact when the leaf has no <c>field</c>) and the leaf's
    /// constant <c>value</c>. It is bound into compiled rules at compile time and must be
    /// thread-safe.
    /// </summary>
    /// <param name="name">The case-sensitive function name used in rule JSON.</param>
    /// <param name="function">The condition implementation.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null/empty or already registered.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="function"/> is null.</exception>
    public RulewrightBuilder RegisterFunction(string name, Func<object?, object?, bool> function)
    {
        if (function is null)
        {
            throw new ArgumentNullException(nameof(function));
        }

        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("Function name must not be null or empty.", nameof(name));
        }

        return RegisterFunction(new DelegateRuleFunction(name, function));
    }

    /// <summary>
    /// Registers a custom condition function implementation under its
    /// <see cref="IRuleFunction.Name"/>.
    /// </summary>
    /// <param name="function">The function; must expose a non-empty name and be thread-safe.</param>
    /// <exception cref="ArgumentNullException"><paramref name="function"/> is null.</exception>
    /// <exception cref="ArgumentException">The function's name is null/empty or already registered.</exception>
    public RulewrightBuilder RegisterFunction(IRuleFunction function)
    {
        if (function is null)
        {
            throw new ArgumentNullException(nameof(function));
        }

        if (string.IsNullOrEmpty(function.Name))
        {
            throw new ArgumentException("Function name must not be null or empty.", nameof(function));
        }

        if (_functions.ContainsKey(function.Name))
        {
            throw new ArgumentException($"A function named '{function.Name}' is already registered.", nameof(function));
        }

        _functions.Add(function.Name, function);
        return this;
    }

    /// <summary>
    /// Creates the engine. The builder can be reused afterwards; the engine takes a
    /// snapshot of the registered functions.
    /// </summary>
    public RulewrightEngine Build()
        => new RulewrightEngine(
            _jsonReader,
            new Dictionary<string, IRuleFunction>(_functions, StringComparer.Ordinal),
            _regexTimeout);
}
