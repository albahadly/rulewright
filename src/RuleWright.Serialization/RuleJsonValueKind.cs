namespace RuleWright.Serialization;

/// <summary>
/// The kind of a <see cref="RuleJsonValue"/> node.
/// </summary>
public enum RuleJsonValueKind
{
    /// <summary>A JSON object.</summary>
    Object,

    /// <summary>A JSON array.</summary>
    Array,

    /// <summary>A JSON string.</summary>
    String,

    /// <summary>A JSON number.</summary>
    Number,

    /// <summary>The JSON literal <c>true</c>.</summary>
    True,

    /// <summary>The JSON literal <c>false</c>.</summary>
    False,

    /// <summary>The JSON literal <c>null</c>.</summary>
    Null,
}
