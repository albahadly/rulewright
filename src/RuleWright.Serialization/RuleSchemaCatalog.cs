using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using RuleWright.Core;

namespace RuleWright.Serialization;

/// <summary>
/// The closed authoring vocabulary of RuleWright's rule schema, as structured metadata:
/// the condition operators, logical group combinators, computed-value expression operators,
/// and action types a rule document may use. This is the runtime companion to
/// <c>docs/schema/rule-schema.json</c> — a rule-builder UI can enumerate it to populate its
/// palettes and pickers instead of hard-coding the vocabulary, and it stays in lock-step with
/// the engine because it is derived from the same sources of truth the parser and validator use
/// (<see cref="OperatorMap"/>, <see cref="ExpressionOperatorMap"/>, and the domain enums).
///
/// The built-in vocabulary is fixed; the one authoring input that varies per engine is the set
/// of registered <c>custom</c> functions, discoverable via
/// <c>RuleWrightEngine.RegisteredFunctions</c>.
/// </summary>
public static class RuleSchemaCatalog
{
    private static readonly ReadOnlyCollection<ConditionOperatorInfo> ConditionList = BuildConditionOperators();
    private static readonly ReadOnlyCollection<LogicalOperatorInfo> LogicalList = BuildLogicalOperators();
    private static readonly ReadOnlyCollection<ExpressionOperatorInfo> ExpressionList = BuildExpressionOperators();
    private static readonly ReadOnlyCollection<ActionTypeInfo> ActionList = BuildActionTypes();

    private static readonly Dictionary<string, ConditionOperatorInfo> ConditionByName = Index(ConditionList, info => info.JsonName);
    private static readonly Dictionary<string, ExpressionOperatorInfo> ExpressionByName = Index(ExpressionList, info => info.JsonName);
    private static readonly Dictionary<string, ActionTypeInfo> ActionByName = Index(ActionList, info => info.Name);

    /// <summary>Every condition operator, in domain-enum declaration order.</summary>
    public static IReadOnlyList<ConditionOperatorInfo> ConditionOperators => ConditionList;

    /// <summary>Every logical group combinator (<c>AND</c>, <c>OR</c>, <c>NOT</c>).</summary>
    public static IReadOnlyList<LogicalOperatorInfo> LogicalOperators => LogicalList;

    /// <summary>Every computed-value expression operator, in domain-enum declaration order.</summary>
    public static IReadOnlyList<ExpressionOperatorInfo> ExpressionOperators => ExpressionList;

    /// <summary>Every action type (<c>setOutput</c>, <c>addToOutput</c>, <c>appendToOutput</c>, <c>removeOutput</c>).</summary>
    public static IReadOnlyList<ActionTypeInfo> ActionTypes => ActionList;

    /// <summary>Looks up a condition operator by its JSON spelling (e.g. <c>"Equals"</c>).</summary>
    public static bool TryGetConditionOperator(string jsonName, out ConditionOperatorInfo info)
        => ConditionByName.TryGetValue(jsonName ?? throw new ArgumentNullException(nameof(jsonName)), out info!);

    /// <summary>Looks up an expression operator by its JSON <c>op</c> spelling (e.g. <c>"add"</c>).</summary>
    public static bool TryGetExpressionOperator(string jsonName, out ExpressionOperatorInfo info)
        => ExpressionByName.TryGetValue(jsonName ?? throw new ArgumentNullException(nameof(jsonName)), out info!);

    /// <summary>Looks up an action type by its JSON <c>type</c> name (e.g. <c>"setOutput"</c>).</summary>
    public static bool TryGetActionType(string name, out ActionTypeInfo info)
        => ActionByName.TryGetValue(name ?? throw new ArgumentNullException(nameof(name)), out info!);

    private static ReadOnlyCollection<ConditionOperatorInfo> BuildConditionOperators()
    {
        var list = new List<ConditionOperatorInfo>();
        foreach (ConditionOperator @operator in (ConditionOperator[])Enum.GetValues(typeof(ConditionOperator)))
        {
            OperatorValueKind kind = @operator switch
            {
                ConditionOperator.IsNull or ConditionOperator.IsNotNull => OperatorValueKind.None,
                ConditionOperator.In or ConditionOperator.NotIn => OperatorValueKind.Array,
                ConditionOperator.Contains or ConditionOperator.StartsWith
                    or ConditionOperator.EndsWith or ConditionOperator.MatchesRegex => OperatorValueKind.Text,
                ConditionOperator.Custom => OperatorValueKind.Custom,
                ConditionOperator.Any or ConditionOperator.All
                    or ConditionOperator.None => OperatorValueKind.Condition,
                _ => OperatorValueKind.Scalar,
            };

            bool custom = @operator == ConditionOperator.Custom;

            // A quantifier reads its collection from a dotted field path, so there is no computed
            // left-hand side to offer either.
            bool fieldOnly = custom || ConditionLeaf.IsQuantifier(@operator);
            list.Add(new ConditionOperatorInfo(
                @operator,
                OperatorMap.ToJsonName(@operator),
                kind,
                requiresFunctionName: custom,
                allowsExpressionLeft: !fieldOnly));
        }

        return new ReadOnlyCollection<ConditionOperatorInfo>(list);
    }

    private static ReadOnlyCollection<LogicalOperatorInfo> BuildLogicalOperators()
        => new ReadOnlyCollection<LogicalOperatorInfo>(new[]
        {
            new LogicalOperatorInfo(LogicalOperator.And, "AND", minChildren: 1, maxChildren: null),
            new LogicalOperatorInfo(LogicalOperator.Or, "OR", minChildren: 1, maxChildren: null),
            new LogicalOperatorInfo(LogicalOperator.Not, "NOT", minChildren: 1, maxChildren: 1),
        });

    private static ReadOnlyCollection<ExpressionOperatorInfo> BuildExpressionOperators()
    {
        var list = new List<ExpressionOperatorInfo>();
        foreach (ExpressionOperator @operator in (ExpressionOperator[])Enum.GetValues(typeof(ExpressionOperator)))
        {
            // A fixed arity means min == max; a null arity means "two or more" (unbounded max).
            int? arity = ExpressionOperatorMap.RequiredArity(@operator);
            ExpressionOperatorCategory category = @operator switch
            {
                ExpressionOperator.Concat => ExpressionOperatorCategory.Text,
                ExpressionOperator.Coalesce => ExpressionOperatorCategory.NullHandling,
                ExpressionOperator.Count => ExpressionOperatorCategory.Collection,
                _ => ExpressionOperatorCategory.Arithmetic,
            };

            list.Add(new ExpressionOperatorInfo(
                @operator,
                ExpressionOperatorMap.ToJsonName(@operator),
                minOperands: arity ?? 2,
                maxOperands: arity,
                category));
        }

        return new ReadOnlyCollection<ExpressionOperatorInfo>(list);
    }

    private static ReadOnlyCollection<ActionTypeInfo> BuildActionTypes()
        => new ReadOnlyCollection<ActionTypeInfo>(new[]
        {
            new ActionTypeInfo(RuleAction.SetOutputType, requiresValue: true, ActionEffect.Replace),
            new ActionTypeInfo(RuleAction.AddToOutputType, requiresValue: true, ActionEffect.Add),
            new ActionTypeInfo(RuleAction.AppendToOutputType, requiresValue: true, ActionEffect.Append),
            new ActionTypeInfo(RuleAction.RemoveOutputType, requiresValue: false, ActionEffect.Remove),
        });

    private static Dictionary<string, T> Index<T>(IReadOnlyList<T> items, Func<T, string> keySelector)
    {
        var map = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (T item in items)
        {
            map[keySelector(item)] = item;
        }

        return map;
    }
}
