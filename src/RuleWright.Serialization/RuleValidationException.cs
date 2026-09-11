using System.Collections.Generic;
using System.Linq;
using RuleWright.Core;

namespace RuleWright.Serialization;

/// <summary>
/// Thrown when a well-formed JSON document fails structural validation against the
/// RuleWright schema contract. Carries the individual <see cref="RuleValidationError"/>
/// entries with their JSON pointer paths.
/// </summary>
public sealed class RuleValidationException : RuleWrightException
{
    /// <summary>
    /// Creates the exception from validation errors.
    /// </summary>
    /// <param name="errors">The validation errors; must be non-empty.</param>
    public RuleValidationException(IReadOnlyList<RuleValidationError> errors)
        : base(BuildMessage(errors))
    {
        Errors = errors;
    }

    /// <summary>The validation errors with JSON pointer paths.</summary>
    public IReadOnlyList<RuleValidationError> Errors { get; }

    private static string BuildMessage(IReadOnlyList<RuleValidationError> errors)
    {
        if (errors is null || errors.Count == 0)
        {
            return "Rule document validation failed.";
        }

        return "Rule document validation failed: "
            + string.Join("; ", errors.Select(e => e.ToString()));
    }
}
