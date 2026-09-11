using RuleWright.Core;

namespace RuleWright.Serialization;

/// <summary>
/// The shape of the comparison <c>value</c> a <see cref="ConditionOperator"/> expects — a hint
/// for authoring UIs deciding what kind of value editor (if any) to show.
/// </summary>
public enum OperatorValueKind
{
    /// <summary>No value is taken (<c>IsNull</c>, <c>IsNotNull</c>).</summary>
    None,

    /// <summary>A single scalar comparand (<c>Equals</c> and the ordering comparisons).</summary>
    Scalar,

    /// <summary>A string operand (<c>Contains</c>, <c>StartsWith</c>, <c>EndsWith</c>, <c>MatchesRegex</c>).</summary>
    Text,

    /// <summary>A non-empty array of scalars (<c>In</c>, <c>NotIn</c>).</summary>
    Array,

    /// <summary>An optional value whose meaning is defined by a named function (<c>custom</c>).</summary>
    Custom,

    /// <summary>
    /// No value at all, but a nested per-element <c>condition</c> instead (<c>Any</c>, <c>All</c>,
    /// <c>None</c>). An authoring UI should offer a condition editor here, not a value editor.
    /// </summary>
    Condition,
}

/// <summary>
/// Discovery metadata for one condition operator: its JSON spelling, domain enum, and the kind
/// of value it takes. Enumerated from <see cref="RuleSchemaCatalog.ConditionOperators"/> so a
/// rule-builder UI can populate operator pickers and value editors without hard-coding the
/// vocabulary. Authoritative validation still lives in <see cref="RuleSetValidator"/>; this is a
/// coarser authoring hint.
/// </summary>
public sealed class ConditionOperatorInfo
{
    internal ConditionOperatorInfo(
        ConditionOperator @operator,
        string jsonName,
        OperatorValueKind valueKind,
        bool requiresFunctionName,
        bool allowsExpressionLeft)
    {
        Operator = @operator;
        JsonName = jsonName;
        ValueKind = valueKind;
        RequiresFunctionName = requiresFunctionName;
        AllowsExpressionLeft = allowsExpressionLeft;
    }

    /// <summary>The domain enum value.</summary>
    public ConditionOperator Operator { get; }

    /// <summary>The JSON schema spelling used in rule documents (e.g. <c>"Equals"</c>).</summary>
    public string JsonName { get; }

    /// <summary>The kind of comparison value this operator takes.</summary>
    public OperatorValueKind ValueKind { get; }

    /// <summary>Whether a comparison <c>value</c> is required (true for Scalar/Text/Array).</summary>
    public bool RequiresValue
        => ValueKind is OperatorValueKind.Scalar or OperatorValueKind.Text or OperatorValueKind.Array;

    /// <summary>Whether a function <c>name</c> is required (the <c>custom</c> operator).</summary>
    public bool RequiresFunctionName { get; }

    /// <summary>
    /// Whether the left-hand side may be a computed <c>expression</c> rather than only a
    /// <c>field</c> (true for every operator except <c>custom</c> and the quantifiers).
    /// </summary>
    public bool AllowsExpressionLeft { get; }

    /// <summary>
    /// Whether the operator quantifies over a collection and therefore requires a nested
    /// per-element <c>condition</c> (<c>Any</c>, <c>All</c>, <c>None</c>).
    /// </summary>
    public bool RequiresElementCondition => ValueKind == OperatorValueKind.Condition;
}
