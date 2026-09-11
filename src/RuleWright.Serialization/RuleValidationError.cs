using System;

namespace RuleWright.Serialization;

/// <summary>
/// A single structural validation failure, located by a JSON pointer
/// (RFC 6901) into the offending document — the shape a rule-builder UI's
/// real-time validation can bind directly to.
/// </summary>
public sealed class RuleValidationError
{
    /// <summary>
    /// Creates a validation error.
    /// </summary>
    /// <param name="path">JSON pointer to the offending value, e.g. <c>/rules/0/condition/operator</c>.</param>
    /// <param name="message">Human-readable description of the problem.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="message"/> is null or empty.</exception>
    public RuleValidationError(string path, string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            throw new ArgumentException("Message must not be null or empty.", nameof(message));
        }

        Path = path ?? throw new ArgumentNullException(nameof(path));
        Message = message;
    }

    /// <summary>JSON pointer (RFC 6901) to the offending value; empty string for the document root.</summary>
    public string Path { get; }

    /// <summary>Human-readable description of the problem.</summary>
    public string Message { get; }

    /// <inheritdoc />
    public override string ToString() => $"{(Path.Length == 0 ? "(root)" : Path)}: {Message}";
}
