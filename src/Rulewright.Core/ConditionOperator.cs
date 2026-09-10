namespace Rulewright.Core;

/// <summary>
/// Comparison operator applied by a <see cref="ConditionLeaf"/>.
/// JSON documents use the schema spellings (<c>"Equals"</c>, <c>"NotEquals"</c>, …);
/// the domain enum uses <see cref="Equal"/>/<see cref="NotEqual"/> to avoid colliding
/// with <see cref="object.Equals(object)"/>.
/// </summary>
public enum ConditionOperator
{
    /// <summary>Field equals the comparison value (JSON: <c>Equals</c>).</summary>
    Equal,

    /// <summary>Field does not equal the comparison value (JSON: <c>NotEquals</c>).</summary>
    NotEqual,

    /// <summary>Field is greater than the comparison value.</summary>
    GreaterThan,

    /// <summary>Field is greater than or equal to the comparison value.</summary>
    GreaterThanOrEqual,

    /// <summary>Field is less than the comparison value.</summary>
    LessThan,

    /// <summary>Field is less than or equal to the comparison value.</summary>
    LessThanOrEqual,

    /// <summary>String field contains the comparison string (ordinal).</summary>
    Contains,

    /// <summary>String field starts with the comparison string (ordinal).</summary>
    StartsWith,

    /// <summary>String field ends with the comparison string (ordinal).</summary>
    EndsWith,

    /// <summary>String field matches the comparison regular expression.</summary>
    MatchesRegex,

    /// <summary>Field is contained in the comparison array.</summary>
    In,

    /// <summary>Field is not contained in the comparison array.</summary>
    NotIn,

    /// <summary>Field (or any segment of its path) is null.</summary>
    IsNull,

    /// <summary>Field is not null (and every segment of its path resolves).</summary>
    IsNotNull,

    /// <summary>A registered <see cref="IRuleFunction"/> decides (JSON: <c>custom</c> with <c>name</c>).</summary>
    Custom,

    /// <summary>
    /// At least one element of the collection field satisfies <see cref="ConditionLeaf.ElementCondition"/>
    /// (JSON: <c>Any</c> with <c>condition</c>). False for an empty collection.
    /// </summary>
    Any,

    /// <summary>
    /// Every element of the collection field satisfies <see cref="ConditionLeaf.ElementCondition"/>
    /// (JSON: <c>All</c> with <c>condition</c>). Vacuously true for an empty collection.
    /// </summary>
    All,

    /// <summary>
    /// No element of the collection field satisfies <see cref="ConditionLeaf.ElementCondition"/>
    /// (JSON: <c>None</c> with <c>condition</c>). Vacuously true for an empty collection.
    /// </summary>
    None,
}
