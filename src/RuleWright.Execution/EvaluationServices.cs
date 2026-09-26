using System;
using System.Collections.Generic;
using RuleWright.Core;

namespace RuleWright.Execution;

/// <summary>
/// The per-engine services both execution paths evaluate against: the registered
/// <c>custom</c> condition functions, the registered <c>call</c> value functions, and the
/// <c>MatchesRegex</c> match timeout. One immutable instance per engine, threaded through the
/// compiler and the interpreter as a unit so the two paths can never be built against
/// different registries.
/// </summary>
internal sealed class EvaluationServices
{
    internal EvaluationServices(
        IReadOnlyDictionary<string, IRuleFunction> functions,
        IReadOnlyDictionary<string, IRuleValueFunction> valueFunctions,
        TimeSpan regexTimeout)
    {
        Functions = functions;
        ValueFunctions = valueFunctions;
        RegexTimeout = regexTimeout;
    }

    /// <summary>The registered <c>custom</c> condition functions, by case-sensitive name.</summary>
    internal IReadOnlyDictionary<string, IRuleFunction> Functions { get; }

    /// <summary>The registered <c>call</c> value functions, by case-sensitive name.</summary>
    internal IReadOnlyDictionary<string, IRuleValueFunction> ValueFunctions { get; }

    /// <summary>The per-match time limit for the <c>MatchesRegex</c> operator.</summary>
    internal TimeSpan RegexTimeout { get; }
}
