using System;

namespace RuleWright.Core;

/// <summary>
/// Discovery metadata for a single custom function registered on an engine — its name plus
/// whatever <see cref="IRuleFunctionMetadata"/> it opted to expose, or the "unknown" defaults
/// when it didn't. See <c>RuleWrightEngine.FunctionCatalog</c>.
/// </summary>
public sealed class RuleFunctionDescriptor
{
    /// <summary>
    /// Creates a function descriptor.
    /// </summary>
    /// <param name="name">The case-sensitive name the function is referenced by in rule JSON.</param>
    /// <param name="description">A short description, or null if the function exposes none.</param>
    /// <param name="valueKind">The expected shape of the function's <c>value</c> operand.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null or empty.</exception>
    public RuleFunctionDescriptor(string name, string? description, RuleFunctionValueKind valueKind)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("Function name must not be null or empty.", nameof(name));
        }

        Name = name;
        Description = description;
        ValueKind = valueKind;
    }

    /// <summary>The case-sensitive name the function is referenced by in rule JSON.</summary>
    public string Name { get; }

    /// <summary>A short description of what the function checks, or null if none was supplied.</summary>
    public string? Description { get; }

    /// <summary>The expected shape of the function's <c>value</c> operand.</summary>
    public RuleFunctionValueKind ValueKind { get; }
}
