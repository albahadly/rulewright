using System;
using System.Collections.Generic;
using System.Linq;

namespace RuleWright.Serialization;

/// <summary>
/// Engine-specific vocabulary that structural validation folds into the schema contract.
/// The built-in vocabulary is closed and fixed; the parts that legitimately vary per engine —
/// today the custom action types registered via <c>RuleWrightBuilder.RegisterAction</c> —
/// arrive here, so <see cref="RuleSetValidator"/> can keep rejecting misspelled action types
/// while accepting the ones this engine actually implements.
/// </summary>
public sealed class RuleDocumentOptions
{
    /// <summary>The default options: no engine-registered vocabulary beyond the built-ins.</summary>
    public static readonly RuleDocumentOptions Default = new RuleDocumentOptions(null);

    /// <summary>
    /// Creates document options.
    /// </summary>
    /// <param name="customActionTypes">
    /// Case-sensitive action type names to accept in addition to the built-in four, or null
    /// for none.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="customActionTypes"/> contains a null or empty name.</exception>
    public RuleDocumentOptions(IEnumerable<string>? customActionTypes)
    {
        string[] materialized = customActionTypes?.ToArray() ?? Array.Empty<string>();
        if (materialized.Any(string.IsNullOrEmpty))
        {
            throw new ArgumentException(
                "Custom action type names must not be null or empty.", nameof(customActionTypes));
        }

        CustomActionTypes = new HashSet<string>(materialized, StringComparer.Ordinal);
    }

    /// <summary>The engine-registered custom action type names, beyond the built-in four.</summary>
    public IReadOnlyCollection<string> CustomActionTypes { get; }

    internal bool IsCustomActionType(string type)
        => ((HashSet<string>)CustomActionTypes).Contains(type);
}
