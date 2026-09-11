using System;
using RuleWright.Core;

namespace RuleWright.Serialization;

/// <summary>
/// Thrown when JSON text cannot be parsed at all (malformed JSON), as opposed to
/// well-formed JSON that fails schema validation (<see cref="RuleValidationException"/>).
/// </summary>
public sealed class RuleParseException : RuleWrightException
{
    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">The error message.</param>
    public RuleParseException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the JSON library's original error.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The JSON library's parse exception.</param>
    public RuleParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
