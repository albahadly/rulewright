using System;

namespace RuleWright.Core;

/// <summary>
/// Discovery metadata for a single value function registered on an engine — its name plus
/// whatever <see cref="IRuleValueFunctionMetadata"/> it opted to expose, or the "unknown"
/// defaults when it didn't. See <c>RuleWrightEngine.ValueFunctionCatalog</c>.
/// </summary>
public sealed class RuleValueFunctionDescriptor
{
    /// <summary>
    /// Creates a value function descriptor.
    /// </summary>
    /// <param name="name">The case-sensitive name the function is referenced by in rule JSON.</param>
    /// <param name="description">A short description, or null if the function exposes none.</param>
    /// <param name="requiredOperandCount">The exact operand count, or null when any count is accepted.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null or empty.</exception>
    public RuleValueFunctionDescriptor(string name, string? description, int? requiredOperandCount)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("Function name must not be null or empty.", nameof(name));
        }

        Name = name;
        Description = description;
        RequiredOperandCount = requiredOperandCount;
    }

    /// <summary>The case-sensitive name the function is referenced by in rule JSON.</summary>
    public string Name { get; }

    /// <summary>A short description of what the function computes, or null if none was supplied.</summary>
    public string? Description { get; }

    /// <summary>The exact operand count a call must supply, or null when any count is accepted.</summary>
    public int? RequiredOperandCount { get; }
}
