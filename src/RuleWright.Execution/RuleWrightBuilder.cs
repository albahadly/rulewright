using System;
using System.Collections.Generic;
using RuleWright.Core;
using RuleWright.Serialization;

namespace RuleWright.Execution;

/// <summary>
/// Fluent entry point for configuring and creating a <see cref="RuleWrightEngine"/>.
/// </summary>
/// <example>
/// <code>
/// var engine = new RuleWrightBuilder()
///     .UseJsonReader(new SystemTextJsonReader())
///     .RegisterFunction("IsBusinessDay", (fieldValue, value) => ...)
///     .Build();
/// </code>
/// </example>
public sealed class RuleWrightBuilder
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

    private readonly Dictionary<string, IRuleValueFunction> _valueFunctions =
        new Dictionary<string, IRuleValueFunction>(StringComparer.Ordinal);

    private readonly Dictionary<string, IRuleActionHandler> _actions =
        new Dictionary<string, IRuleActionHandler>(StringComparer.Ordinal);

    private IRuleJsonReader? _jsonReader;
    private TimeSpan _regexTimeout = DefaultRegexTimeout;

    /// <summary>
    /// Sets the JSON reader adapter used by <see cref="RuleWrightEngine.LoadRuleSet(string)"/>.
    /// Required before loading JSON; engines built without one can still load domain
    /// <see cref="RuleSet"/> instances directly.
    /// </summary>
    /// <param name="reader">The adapter, e.g. <c>SystemTextJsonReader</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is null.</exception>
    public RuleWrightBuilder UseJsonReader(IRuleJsonReader reader)
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
    public RuleWrightBuilder UseRegexTimeout(TimeSpan timeout)
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
    public RuleWrightBuilder RegisterFunction(string name, Func<object?, object?, bool> function)
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
    public RuleWrightBuilder RegisterFunction(IRuleFunction function)
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
    /// Registers a custom value function referenced from a value expression as
    /// <c>{ "call": "...", "operands": [...] }</c>. The delegate receives the evaluated
    /// operand values in document order. It is bound into compiled rules at compile time,
    /// must be thread-safe, and should be total — return null for argument shapes it does
    /// not understand, like the built-in expression operators do.
    /// </summary>
    /// <param name="name">The case-sensitive function name used in rule JSON.</param>
    /// <param name="function">The value implementation.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null/empty or already registered.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="function"/> is null.</exception>
    public RuleWrightBuilder RegisterValueFunction(string name, Func<object?[], object?> function)
    {
        if (function is null)
        {
            throw new ArgumentNullException(nameof(function));
        }

        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("Function name must not be null or empty.", nameof(name));
        }

        return RegisterValueFunction(new DelegateRuleValueFunction(name, function));
    }

    /// <summary>
    /// Registers a custom value function implementation under its
    /// <see cref="IRuleValueFunction.Name"/>. A function that also implements
    /// <see cref="IRuleValueFunctionMetadata"/> with a <c>RequiredOperandCount</c> gets its
    /// calls arity-checked at <c>LoadRuleSet</c>.
    /// </summary>
    /// <param name="function">The function; must expose a non-empty name and be thread-safe.</param>
    /// <exception cref="ArgumentNullException"><paramref name="function"/> is null.</exception>
    /// <exception cref="ArgumentException">The function's name is null/empty or already registered.</exception>
    public RuleWrightBuilder RegisterValueFunction(IRuleValueFunction function)
    {
        if (function is null)
        {
            throw new ArgumentNullException(nameof(function));
        }

        if (string.IsNullOrEmpty(function.Name))
        {
            throw new ArgumentException("Function name must not be null or empty.", nameof(function));
        }

        if (_valueFunctions.ContainsKey(function.Name))
        {
            throw new ArgumentException(
                $"A value function named '{function.Name}' is already registered.", nameof(function));
        }

        _valueFunctions.Add(function.Name, function);
        return this;
    }

    /// <summary>
    /// Registers a custom action type referenced from rule JSON as
    /// <c>{ "type": "...", "target": "...", "value": ... }</c>. The delegate receives a
    /// <see cref="RuleActionContext"/> carrying the action's target, its already-evaluated
    /// value, the fact, and read/write access to the running outputs. It must be thread-safe.
    /// </summary>
    /// <param name="name">The case-sensitive action type name used in rule JSON.</param>
    /// <param name="handler">The action implementation.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null/empty, a built-in action type, or already registered.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="handler"/> is null.</exception>
    public RuleWrightBuilder RegisterAction(string name, Action<RuleActionContext> handler)
    {
        if (handler is null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("Action type name must not be null or empty.", nameof(name));
        }

        return RegisterAction(new DelegateRuleActionHandler(name, handler));
    }

    /// <summary>
    /// Registers a custom action handler under its <see cref="IRuleActionHandler.Name"/>.
    /// </summary>
    /// <param name="handler">The handler; must expose a non-empty name and be thread-safe.</param>
    /// <exception cref="ArgumentNullException"><paramref name="handler"/> is null.</exception>
    /// <exception cref="ArgumentException">The handler's name is null/empty, a built-in action type, or already registered.</exception>
    public RuleWrightBuilder RegisterAction(IRuleActionHandler handler)
    {
        if (handler is null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        if (string.IsNullOrEmpty(handler.Name))
        {
            throw new ArgumentException("Action type name must not be null or empty.", nameof(handler));
        }

        if (handler.Name is RuleAction.SetOutputType or RuleAction.AddToOutputType
            or RuleAction.AppendToOutputType or RuleAction.RemoveOutputType)
        {
            throw new ArgumentException(
                $"'{handler.Name}' is a built-in action type and cannot be re-registered.", nameof(handler));
        }

        if (_actions.ContainsKey(handler.Name))
        {
            throw new ArgumentException(
                $"An action named '{handler.Name}' is already registered.", nameof(handler));
        }

        _actions.Add(handler.Name, handler);
        return this;
    }

    /// <summary>
    /// Creates the engine. The builder can be reused afterwards; the engine takes a
    /// snapshot of the registered functions and actions.
    /// </summary>
    public RuleWrightEngine Build()
        => new RuleWrightEngine(
            _jsonReader,
            new Dictionary<string, IRuleFunction>(_functions, StringComparer.Ordinal),
            new Dictionary<string, IRuleValueFunction>(_valueFunctions, StringComparer.Ordinal),
            new Dictionary<string, IRuleActionHandler>(_actions, StringComparer.Ordinal),
            _regexTimeout);
}
