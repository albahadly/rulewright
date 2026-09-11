using System;
using System.Collections.Generic;

namespace RuleWright.Serialization;

/// <summary>
/// The outcome of structurally validating a rule document against the RuleWright
/// schema contract (<c>docs/schema/rule-schema.json</c>).
/// </summary>
public sealed class RuleSetValidationResult
{
    /// <summary>A shared successful result.</summary>
    public static RuleSetValidationResult Success { get; } =
        new RuleSetValidationResult(Array.Empty<RuleValidationError>());

    /// <summary>
    /// Creates a validation result.
    /// </summary>
    /// <param name="errors">The validation errors; empty means the document is valid.</param>
    /// <exception cref="ArgumentNullException"><paramref name="errors"/> is null.</exception>
    public RuleSetValidationResult(IReadOnlyList<RuleValidationError> errors)
    {
        Errors = errors ?? throw new ArgumentNullException(nameof(errors));
    }

    /// <summary>Whether the document passed validation.</summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>The validation errors, in document order; empty when valid.</summary>
    public IReadOnlyList<RuleValidationError> Errors { get; }
}
