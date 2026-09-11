using System;

namespace RuleWright.Core;

/// <summary>
/// Base type for all exceptions thrown by RuleWright libraries.
/// </summary>
public class RuleWrightException : Exception
{
    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">The error message.</param>
    public RuleWrightException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying cause.</param>
    public RuleWrightException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
