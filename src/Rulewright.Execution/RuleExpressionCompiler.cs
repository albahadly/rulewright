using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;
using Rulewright.Core;

namespace Rulewright.Execution;

/// <summary>
/// Compiles a rule's condition tree into delegates via expression trees. Field paths
/// become chained, null-guarded member accesses; comparison constants are converted to
/// the field's exact CLR type at compile time so evaluations run without reflection,
/// string parsing, or boxing of value-type comparisons.
/// </summary>
internal static class RuleExpressionCompiler
{
    private static readonly MethodInfo FunctionEvaluateMethod =
        typeof(IRuleFunction).GetMethod(nameof(IRuleFunction.Evaluate))!;

    private static readonly MethodInfo StringContainsMethod =
        typeof(string).GetMethod(nameof(string.Contains), new[] { typeof(string) })!;

    private static readonly MethodInfo StringStartsWithMethod =
        typeof(string).GetMethod(nameof(string.StartsWith), new[] { typeof(string), typeof(StringComparison) })!;

    private static readonly MethodInfo StringEndsWithMethod =
        typeof(string).GetMethod(nameof(string.EndsWith), new[] { typeof(string), typeof(StringComparison) })!;

    private static readonly MethodInfo StringCompareOrdinalMethod =
        typeof(string).GetMethod(nameof(string.CompareOrdinal), new[] { typeof(string), typeof(string) })!;

    private static readonly MethodInfo RegexIsMatchMethod =
        typeof(Regex).GetMethod(nameof(Regex.IsMatch), new[] { typeof(string) })!;

    private static readonly Expression NullObject = Expression.Constant(null, typeof(object));

    private static readonly MethodInfo ApplyOperatorMethod =
        typeof(RuleInterpreter).GetMethod(nameof(RuleInterpreter.ApplyOperator), BindingFlags.Static | BindingFlags.NonPublic)!;

    private static readonly MethodInfo AddMethod = ValueOp(nameof(ValueExpressionOps.Add));
    private static readonly MethodInfo SubtractMethod = ValueOp(nameof(ValueExpressionOps.Subtract));
    private static readonly MethodInfo MultiplyMethod = ValueOp(nameof(ValueExpressionOps.Multiply));
    private static readonly MethodInfo DivideMethod = ValueOp(nameof(ValueExpressionOps.Divide));
    private static readonly MethodInfo ModuloMethod = ValueOp(nameof(ValueExpressionOps.Modulo));
    private static readonly MethodInfo NegateMethod = ValueOp(nameof(ValueExpressionOps.Negate));
    private static readonly MethodInfo ConcatMethod = ValueOp(nameof(ValueExpressionOps.Concat));
    private static readonly MethodInfo CoalesceMethod = ValueOp(nameof(ValueExpressionOps.Coalesce));
    private static readonly MethodInfo CountMethod = ValueOp(nameof(ValueExpressionOps.Count));

    private static readonly MethodInfo EnumerableAnyMethod = EnumerableQuantifier(nameof(System.Linq.Enumerable.Any));
    private static readonly MethodInfo EnumerableAllMethod = EnumerableQuantifier(nameof(System.Linq.Enumerable.All));

    internal static CompiledRule<TFact> Compile<TFact>(
        Rule rule,
        IReadOnlyDictionary<string, IRuleFunction> functions,
        TimeSpan regexTimeout,
        int[] nodeLayout)
    {
        ParameterExpression fact = Expression.Parameter(typeof(TFact), "fact");
        ParameterExpression results = Expression.Parameter(typeof(bool?[]), "results");

        var fastContext = new Context(rule, functions, regexTimeout, null, nodeLayout);
        Expression fastBody = BuildNode(rule.Condition, 0, fact, fastContext);
        Func<TFact, bool> predicate = Expression.Lambda<Func<TFact, bool>>(fastBody, fact).Compile();

        var tracedContext = new Context(rule, functions, regexTimeout, results, nodeLayout);
        Expression tracedBody = BuildNode(rule.Condition, 0, fact, tracedContext);
        Func<TFact, bool?[], bool> tracedPredicate =
            Expression.Lambda<Func<TFact, bool?[], bool>>(tracedBody, fact, results).Compile();

        OutputStep<TFact>[]? outputSteps = CompileOutputs<TFact>(rule, rule.Actions);
        OutputStep<TFact>[]? elseOutputSteps = CompileOutputs<TFact>(rule, rule.ElseActions);

        return new CompiledRule<TFact>(predicate, tracedPredicate, outputSteps, elseOutputSteps);
    }

    /// <summary>
    /// Compiles one branch's actions into ordered output steps, but only when the branch is not
    /// made purely of constant <c>setOutput</c> actions. Such branches return null so the engine
    /// can reuse their pre-materialized outputs and avoid per-firing allocation. Field
    /// references inside value expressions are validated against <typeparamref name="TFact"/>
    /// at compile time, exactly like condition fields.
    /// </summary>
    private static OutputStep<TFact>[]? CompileOutputs<TFact>(Rule rule, IReadOnlyList<RuleAction> actions)
    {
        bool anyComplex = false;
        for (int i = 0; i < actions.Count; i++)
        {
            if (!OutputApplier.IsLiteralSet(actions[i]))
            {
                anyComplex = true;
                break;
            }
        }

        if (!anyComplex)
        {
            return null;
        }

        ParameterExpression fact = Expression.Parameter(typeof(TFact), "fact");
        int count = actions.Count;
        var steps = new OutputStep<TFact>[count];
        for (int i = 0; i < count; i++)
        {
            RuleAction action = actions[i];
            Func<TFact, object?> valueFactory;
            if (action.Value is LiteralExpression literal)
            {
                object? constant = literal.Value;
                valueFactory = _ => constant;
            }
            else
            {
                Expression body = BuildValueExpression(action.Value, fact, rule);
                valueFactory = Expression.Lambda<Func<TFact, object?>>(body, fact).Compile();
            }

            steps[i] = new OutputStep<TFact>(action.Type, action.Target, valueFactory);
        }

        return steps;
    }

    private static Expression BuildValueExpression(ValueExpression expression, ParameterExpression fact, Rule rule)
    {
        switch (expression)
        {
            case LiteralExpression literal:
                return Expression.Constant(literal.Value, typeof(object));

            case FieldExpression field:
                return NavigateValue(fact, field.Path.Split('.'), 0, isRoot: true, field.Path, rule);

            case OperatorExpression op:
                return BuildOperatorExpression(op, fact, rule);

            default:
                return NullObject;
        }
    }

    private static Expression BuildOperatorExpression(OperatorExpression op, ParameterExpression fact, Rule rule)
    {
        switch (op.Operator)
        {
            case ExpressionOperator.Add:
                return Fold(op.Operands, fact, rule, AddMethod);

            case ExpressionOperator.Multiply:
                return Fold(op.Operands, fact, rule, MultiplyMethod);

            case ExpressionOperator.Subtract:
                return Expression.Call(
                    SubtractMethod,
                    BuildValueExpression(op.Operands[0], fact, rule),
                    BuildValueExpression(op.Operands[1], fact, rule));

            case ExpressionOperator.Divide:
                return Expression.Call(
                    DivideMethod,
                    BuildValueExpression(op.Operands[0], fact, rule),
                    BuildValueExpression(op.Operands[1], fact, rule));

            case ExpressionOperator.Modulo:
                return Expression.Call(
                    ModuloMethod,
                    BuildValueExpression(op.Operands[0], fact, rule),
                    BuildValueExpression(op.Operands[1], fact, rule));

            case ExpressionOperator.Negate:
                return Expression.Call(NegateMethod, BuildValueExpression(op.Operands[0], fact, rule));

            case ExpressionOperator.Count:
                return Expression.Call(CountMethod, BuildValueExpression(op.Operands[0], fact, rule));

            case ExpressionOperator.Concat:
                return Expression.Call(ConcatMethod, OperandArray(op.Operands, fact, rule));

            default: // Coalesce
                return Expression.Call(CoalesceMethod, OperandArray(op.Operands, fact, rule));
        }
    }

    private static Expression Fold(
        IReadOnlyList<ValueExpression> operands,
        ParameterExpression fact,
        Rule rule,
        MethodInfo binaryOp)
    {
        Expression accumulator = BuildValueExpression(operands[0], fact, rule);
        for (int i = 1; i < operands.Count; i++)
        {
            accumulator = Expression.Call(binaryOp, accumulator, BuildValueExpression(operands[i], fact, rule));
        }

        return accumulator;
    }

    private static Expression OperandArray(IReadOnlyList<ValueExpression> operands, ParameterExpression fact, Rule rule)
    {
        var items = new Expression[operands.Count];
        for (int i = 0; i < operands.Count; i++)
        {
            items[i] = BuildValueExpression(operands[i], fact, rule);
        }

        return Expression.NewArrayInit(typeof(object), items);
    }

    private static Expression NavigateValue(
        Expression current,
        string[] segments,
        int segmentIndex,
        bool isRoot,
        string path,
        Rule rule)
    {
        if (isRoot && segments.Length == 1 && segments[0] == ConditionLeaf.ElementSelfPath)
        {
            return current.Type == typeof(object) ? current : Expression.Convert(current, typeof(object));
        }

        Type type = current.Type;

        if (Nullable.GetUnderlyingType(type) is not null)
        {
            return Expression.Condition(
                Expression.Property(current, "HasValue"),
                NavigateValue(Expression.Property(current, "Value"), segments, segmentIndex, isRoot: false, path, rule),
                NullObject);
        }

        if (segmentIndex == segments.Length)
        {
            return type == typeof(object) ? current : Expression.Convert(current, typeof(object));
        }

        MemberExpression member;
        try
        {
            member = Expression.PropertyOrField(current, segments[segmentIndex]);
        }
        catch (ArgumentException ex)
        {
            throw new RuleCompilationException(
                rule.Id,
                $"expression field path '{path}': member '{segments[segmentIndex]}' was not found on type {type.FullName}.",
                ex);
        }

        ParameterExpression variable = Expression.Variable(member.Type, "e" + segmentIndex.ToString(CultureInfo.InvariantCulture));
        Expression inner = Expression.Block(
            new[] { variable },
            Expression.Assign(variable, member),
            NavigateValue(variable, segments, segmentIndex + 1, isRoot: false, path, rule));

        if (!type.IsValueType && !isRoot)
        {
            return Expression.Condition(
                Expression.NotEqual(current, Expression.Constant(null, type)),
                inner,
                NullObject);
        }

        return inner;
    }

    private static MethodInfo ValueOp(string name)
        => typeof(ValueExpressionOps).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;

    /// <summary>The two-argument (sequence, predicate) overload of an Enumerable quantifier.</summary>
    private static MethodInfo EnumerableQuantifier(string name)
    {
        foreach (MethodInfo method in typeof(System.Linq.Enumerable).GetMethods(BindingFlags.Static | BindingFlags.Public))
        {
            if (method.Name == name && method.GetParameters().Length == 2)
            {
                return method;
            }
        }

        throw new InvalidOperationException($"Enumerable.{name}(source, predicate) was not found.");
    }

    private sealed class Context
    {
        internal Context(
            Rule rule,
            IReadOnlyDictionary<string, IRuleFunction> functions,
            TimeSpan regexTimeout,
            ParameterExpression? results,
            int[]? nodeLayout)
        {
            Rule = rule;
            Functions = functions;
            RegexTimeout = regexTimeout;
            Results = results;
            NodeLayout = nodeLayout;
        }

        internal Rule Rule { get; }

        internal IReadOnlyDictionary<string, IRuleFunction> Functions { get; }

        internal TimeSpan RegexTimeout { get; }

        internal ParameterExpression? Results { get; }

        /// <summary>Pre-order subtree sizes; see <see cref="ConditionNodeIndexer"/>.</summary>
        internal int[]? NodeLayout { get; }
    }

    /// <summary>
    /// Builds one condition node. <c>index</c> is the node's pre-order position, which is also its
    /// slot in the traced results array: children start at <c>index + 1</c> and each later sibling
    /// follows the previous one by that sibling's subtree size. Positions rather than node identity,
    /// so a node instance reused at several positions gets a slot per position.
    /// </summary>
    private static Expression BuildNode(ConditionNode node, int index, ParameterExpression fact, Context context)
    {
        Expression body = node is ConditionGroup group
            ? BuildGroup(group, index, fact, context)
            : BuildLeaf((ConditionLeaf)node, fact, context);

        if (context.Results is not null)
        {
            // results[i] = (bool?)body, then unwrap: Assign yields the assigned value.
            body = Expression.Property(
                Expression.Assign(
                    Expression.ArrayAccess(context.Results, Expression.Constant(index)),
                    Expression.Convert(body, typeof(bool?))),
                "Value");
        }

        return body;
    }

    private static Expression BuildGroup(ConditionGroup group, int index, ParameterExpression fact, Context context)
    {
        int childIndex = index + 1;
        if (group.Operator == LogicalOperator.Not)
        {
            return Expression.Not(BuildNode(group.Children[0], childIndex, fact, context));
        }

        Expression combined = BuildNode(group.Children[0], childIndex, fact, context);
        for (int i = 1; i < group.Children.Count; i++)
        {
            if (context.Results is not null)
            {
                childIndex += context.NodeLayout![childIndex];
            }

            Expression child = BuildNode(group.Children[i], childIndex, fact, context);
            combined = group.Operator == LogicalOperator.And
                ? Expression.AndAlso(combined, child)
                : Expression.OrElse(combined, child);
        }

        return combined;
    }

    private static Expression BuildLeaf(ConditionLeaf leaf, ParameterExpression fact, Context context)
    {
        if (leaf.Left is not null)
        {
            // Computed left-hand side: compile it (field access stays reflection-free), then
            // apply the operator through the shared boxed evaluator so the result matches the
            // interpreter exactly.
            return ApplySharedOperator(leaf, BuildValueExpression(leaf.Left, fact, context.Rule), context);
        }

        if (leaf.Field is null)
        {
            // Field-less custom condition: the function receives the whole fact.
            return CallFunction(leaf, Expression.Convert(fact, typeof(object)), context);
        }

        if (ConditionLeaf.IsQuantifier(leaf.Operator))
        {
            return BuildQuantifier(leaf, fact, context);
        }

        if (leaf.Field == ConditionLeaf.ElementSelfPath)
        {
            // "$" is the value in scope itself - the element a quantifier is testing.
            return BuildComparison(fact, leaf, context);
        }

        string[] segments = leaf.Field.Split('.');
        return Navigate(fact, segments, 0, isRoot: true, leaf, context);
    }

    private static Expression Navigate(
        Expression current,
        string[] segments,
        int segmentIndex,
        bool isRoot,
        ConditionLeaf leaf,
        Context context)
    {
        Type type = current.Type;

        if (Nullable.GetUnderlyingType(type) is not null)
        {
            return Expression.Condition(
                Expression.Property(current, "HasValue"),
                Navigate(Expression.Property(current, "Value"), segments, segmentIndex, isRoot: false, leaf, context),
                NullFallback(leaf, context));
        }

        if (segmentIndex == segments.Length)
        {
            return BuildComparison(current, leaf, context);
        }

        MemberExpression member;
        try
        {
            member = Expression.PropertyOrField(current, segments[segmentIndex]);
        }
        catch (ArgumentException ex)
        {
            throw new RuleCompilationException(
                context.Rule.Id,
                $"field path '{leaf.Field}': member '{segments[segmentIndex]}' was not found on type {type.FullName}.",
                ex);
        }

        ParameterExpression variable = Expression.Variable(member.Type, "p" + segmentIndex.ToString(CultureInfo.InvariantCulture));
        Expression inner = Expression.Block(
            new[] { variable },
            Expression.Assign(variable, member),
            Navigate(variable, segments, segmentIndex + 1, isRoot: false, leaf, context));

        if (!type.IsValueType && !isRoot)
        {
            // Null-safe navigation: a null intermediate short-circuits to the operator's
            // null semantics instead of throwing NullReferenceException.
            return Expression.Condition(
                Expression.NotEqual(current, Expression.Constant(null, type)),
                inner,
                NullFallback(leaf, context));
        }

        return inner;
    }

    /// <summary>
    /// Compiles a quantifier into a typed <c>Enumerable.Any</c>/<c>All</c> over the collection's
    /// element type, with the per-element condition compiled against that element exactly as a
    /// rule's own condition is compiled against the fact — so member access inside the loop stays
    /// reflection-free and every operator, group, and nested quantifier works there too.
    ///
    /// <para>The element condition is built with a untraced context: per-element results have no
    /// single slot to live in, so a quantifier is one node in a trace and its inner condition is
    /// rendered into that node's description instead.</para>
    /// </summary>
    private static Expression BuildQuantifier(ConditionLeaf leaf, ParameterExpression fact, Context context)
    {
        // Navigated with its static type intact, not boxed to object: the whole point is to learn
        // the element type at compile time so member access inside the loop stays reflection-free.
        Expression collection = leaf.Field == ConditionLeaf.ElementSelfPath
            ? (Expression)fact
            : NavigateTyped(fact, leaf.Field!.Split('.'), 0, isRoot: true, leaf.Field, context.Rule);

        // A string is text, not a collection of characters, even though it is IEnumerable<char>.
        if (collection.Type == typeof(string))
        {
            return Expression.Constant(leaf.Operator == ConditionOperator.None);
        }

        Type elementType = GetElementType(collection.Type);
        Type sequenceType = typeof(IEnumerable<>).MakeGenericType(elementType);

        if (!sequenceType.IsAssignableFrom(collection.Type))
        {
            // The field's static type says nothing about what it holds (an object-typed member, or
            // a non-generic IEnumerable). Defer to the shared runtime evaluator, exactly as an
            // object-typed field does, so both paths still answer identically.
            return ApplySharedOperator(leaf, Expression.Convert(collection, typeof(object)), context);
        }

        ParameterExpression element = Expression.Parameter(elementType, "element");
        var elementContext = new Context(context.Rule, context.Functions, context.RegexTimeout, null, null);
        LambdaExpression predicate = Expression.Lambda(
            typeof(Func<,>).MakeGenericType(elementType, typeof(bool)),
            BuildNode(leaf.ElementCondition!, 0, element, elementContext),
            element);

        MethodInfo quantify = (leaf.Operator == ConditionOperator.All ? EnumerableAllMethod : EnumerableAnyMethod)
            .MakeGenericMethod(elementType);
        Expression call = Expression.Call(quantify, Expression.Convert(collection, sequenceType), predicate);
        if (leaf.Operator == ConditionOperator.None)
        {
            call = Expression.Not(call);
        }

        if (collection.Type.IsValueType)
        {
            return call;
        }

        // A null collection is the field's absence, so it takes the ordinary null semantics:
        // false for Any and All, true for None, matching NotIn and the interpreter.
        return Expression.Condition(
            Expression.NotEqual(collection, Expression.Constant(null, collection.Type)),
            call,
            Expression.Constant(leaf.Operator == ConditionOperator.None));
    }

    /// <summary>
    /// Navigates a field path the way <see cref="Navigate"/> does but yields the member's own CLR
    /// type rather than folding into a comparison or boxing to <c>object</c>. A null link
    /// short-circuits to that type's default, which for the reference types collections actually
    /// are is null — and the caller turns that into the operator's null semantics.
    /// </summary>
    private static Expression NavigateTyped(
        Expression current, string[] segments, int segmentIndex, bool isRoot, string path, Rule rule)
    {
        Type type = current.Type;

        if (Nullable.GetUnderlyingType(type) is not null)
        {
            Expression unwrapped = NavigateTyped(
                Expression.Property(current, "Value"), segments, segmentIndex, isRoot: false, path, rule);
            return Expression.Condition(
                Expression.Property(current, "HasValue"), unwrapped, Expression.Default(unwrapped.Type));
        }

        if (segmentIndex == segments.Length)
        {
            return current;
        }

        MemberExpression member;
        try
        {
            member = Expression.PropertyOrField(current, segments[segmentIndex]);
        }
        catch (ArgumentException ex)
        {
            throw new RuleCompilationException(
                rule.Id,
                $"field path '{path}': member '{segments[segmentIndex]}' was not found on type {type.FullName}.",
                ex);
        }

        ParameterExpression variable = Expression.Variable(
            member.Type, "c" + segmentIndex.ToString(CultureInfo.InvariantCulture));
        Expression inner = Expression.Block(
            new[] { variable },
            Expression.Assign(variable, member),
            NavigateTyped(variable, segments, segmentIndex + 1, isRoot: false, path, rule));

        if (!type.IsValueType && !isRoot)
        {
            return Expression.Condition(
                Expression.NotEqual(current, Expression.Constant(null, type)),
                inner,
                Expression.Default(inner.Type));
        }

        return inner;
    }

    /// <summary>
    /// The element type a collection yields: the <c>T</c> of its <see cref="IEnumerable{T}"/>, or
    /// <see cref="object"/> for a bare <see cref="System.Collections.IEnumerable"/> or an
    /// <c>object</c>-typed field whose shape is only known at runtime.
    /// </summary>
    private static Type GetElementType(Type collectionType)
    {
        if (collectionType.IsArray)
        {
            return collectionType.GetElementType()!;
        }

        if (collectionType.IsGenericType && collectionType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            return collectionType.GetGenericArguments()[0];
        }

        foreach (Type contract in collectionType.GetInterfaces())
        {
            if (contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                return contract.GetGenericArguments()[0];
            }
        }

        return typeof(object);
    }

    private static Expression NullFallback(ConditionLeaf leaf, Context context) => leaf.Operator switch
    {
        ConditionOperator.IsNull => Expression.Constant(true),
        ConditionOperator.IsNotNull => Expression.Constant(false),
        ConditionOperator.Equal => Expression.Constant(leaf.Value is null),
        ConditionOperator.NotEqual => Expression.Constant(leaf.Value is not null),
        ConditionOperator.NotIn => Expression.Constant(true),
        ConditionOperator.Custom => CallFunction(leaf, Expression.Constant(null, typeof(object)), context),
        _ => Expression.Constant(false),
    };

    /// <summary>
    /// Routes a comparison through the same boxed evaluator the interpreter uses, so both paths
    /// answer identically for operands that only have a runtime type.
    /// </summary>
    private static Expression ApplySharedOperator(ConditionLeaf leaf, Expression boxedValue, Context context)
        => Expression.Call(
            ApplyOperatorMethod,
            Expression.Constant(leaf, typeof(ConditionLeaf)),
            boxedValue,
            Expression.Constant(context.Functions, typeof(IReadOnlyDictionary<string, IRuleFunction>)),
            Expression.Constant(context.RegexTimeout, typeof(TimeSpan)));

    private static Expression BuildComparison(Expression value, ConditionLeaf leaf, Context context)
    {
        Type type = value.Type;

        // An object-typed field carries whatever the fact put in it, so there is no CLR type to
        // convert the constant to at compile time. EqualityComparer<object> would then make an
        // int 5 and a long 5 unequal where the interpreter compares them numerically, so this
        // defers to the shared runtime evaluator instead - the same treatment a computed
        // left-hand side gets. Custom keeps its compile-time function binding.
        if (type == typeof(object) && leaf.Operator != ConditionOperator.Custom)
        {
            return ApplySharedOperator(leaf, value, context);
        }

        switch (leaf.Operator)
        {
            case ConditionOperator.IsNull:
                return type.IsValueType
                    ? Expression.Constant(false)
                    : Expression.Equal(value, Expression.Constant(null, type));

            case ConditionOperator.IsNotNull:
                return type.IsValueType
                    ? Expression.Constant(true)
                    : Expression.NotEqual(value, Expression.Constant(null, type));

            case ConditionOperator.Custom:
                return CallFunction(leaf, Expression.Convert(value, typeof(object)), context);

            case ConditionOperator.Equal:
                return BuildEquality(value, leaf, context, negate: false);

            case ConditionOperator.NotEqual:
                return BuildEquality(value, leaf, context, negate: true);

            case ConditionOperator.GreaterThan:
            case ConditionOperator.GreaterThanOrEqual:
            case ConditionOperator.LessThan:
            case ConditionOperator.LessThanOrEqual:
                return BuildOrdering(value, leaf, context);

            case ConditionOperator.Contains:
            case ConditionOperator.StartsWith:
            case ConditionOperator.EndsWith:
            case ConditionOperator.MatchesRegex:
                return BuildStringOperator(value, leaf, context);

            default:
                return BuildSetOperator(value, leaf, context);
        }
    }

    private static Expression BuildEquality(Expression value, ConditionLeaf leaf, Context context, bool negate)
    {
        Type type = value.Type;
        object? comparand = leaf.Value;

        if (comparand is null)
        {
            if (type.IsValueType)
            {
                return Expression.Constant(negate);
            }

            return negate
                ? Expression.NotEqual(value, Expression.Constant(null, type))
                : Expression.Equal(value, Expression.Constant(null, type));
        }

        if (ValueConverter.IsNumericType(type) && ValueConverter.IsNumericValue(comparand))
        {
            if (!ValueConverter.TryConvertNumberExact(comparand, type, out object exact))
            {
                // e.g. int field vs 10.5 — exact equality is impossible, fold to a constant.
                return Expression.Constant(negate);
            }

            Expression numericEqual = Expression.Equal(value, Expression.Constant(exact, type));
            return negate ? Expression.Not(numericEqual) : numericEqual;
        }

        object converted = ConvertConstant(comparand, type, leaf, context);
        Expression equal;
        if (type == typeof(string) || type.IsValueType)
        {
            try
            {
                equal = Expression.Equal(value, Expression.Constant(converted, type));
            }
            catch (InvalidOperationException)
            {
                equal = EqualityComparerCall(value, converted, type);
            }
        }
        else
        {
            // Reference types without a meaningful == operator: use EqualityComparer<T>
            // rather than reference equality.
            equal = EqualityComparerCall(value, converted, type);
        }

        return negate ? Expression.Not(equal) : equal;
    }

    private static Expression BuildOrdering(Expression value, ConditionLeaf leaf, Context context)
    {
        Type type = value.Type;
        object? comparand = leaf.Value;
        if (comparand is null)
        {
            throw new RuleCompilationException(
                context.Rule.Id,
                $"operator {ConditionDescriber.Describe(leaf)} requires a non-null comparison value.");
        }

        Expression comparison;
        if (ValueConverter.IsNumericType(type) && ValueConverter.IsNumericValue(comparand))
        {
            if (ValueConverter.TryConvertNumberExact(comparand, type, out object exact))
            {
                comparison = MakeOrdering(leaf.Operator, value, Expression.Constant(exact, type));
            }
            else
            {
                // Mixed numeric shapes (int field vs 10.5): widen both sides once, at compile time.
                Type wide = type == typeof(double) || type == typeof(float) || comparand is double or float
                    ? typeof(double)
                    : typeof(decimal);
                object wideComparand = wide == typeof(double)
                    ? Convert.ToDouble(comparand, CultureInfo.InvariantCulture)
                    : Convert.ToDecimal(comparand, CultureInfo.InvariantCulture);
                comparison = MakeOrdering(
                    leaf.Operator,
                    Expression.Convert(value, wide),
                    Expression.Constant(wideComparand, wide));
            }
        }
        else if (type == typeof(string))
        {
            // Strings order ordinally — Comparer<string>.Default would order by the ambient
            // culture, making the same rule and fact answer differently per machine and
            // disagreeing with the interpreter's string.CompareOrdinal.
            comparison = MakeOrdering(
                leaf.Operator,
                Expression.Call(StringCompareOrdinalMethod, value, Expression.Constant(ConvertConstant(comparand, type, leaf, context), type)),
                Expression.Constant(0));
        }
        else
        {
            object converted = ConvertConstant(comparand, type, leaf, context);
            ConstantExpression constant = Expression.Constant(converted, type);
            try
            {
                comparison = MakeOrdering(leaf.Operator, value, constant);
            }
            catch (InvalidOperationException)
            {
                // Types without comparison operators (Guid, …): Comparer<T>.Default.
                comparison = MakeOrdering(leaf.Operator, ComparerCall(value, constant, type), Expression.Constant(0));
            }
        }

        if (!type.IsValueType)
        {
            comparison = Expression.Condition(
                Expression.NotEqual(value, Expression.Constant(null, type)),
                comparison,
                Expression.Constant(false));
        }

        return comparison;
    }

    private static Expression BuildStringOperator(Expression value, ConditionLeaf leaf, Context context)
    {
        if (value.Type != typeof(string))
        {
            throw new RuleCompilationException(
                context.Rule.Id,
                $"operator '{ConditionDescriber.Describe(leaf)}' requires a string field, but '{leaf.Field}' is {value.Type.Name}.");
        }

        string operand = (string)leaf.Value!;
        Expression call = leaf.Operator switch
        {
            ConditionOperator.Contains => Expression.Call(value, StringContainsMethod, Expression.Constant(operand)),
            ConditionOperator.StartsWith => Expression.Call(
                value, StringStartsWithMethod, Expression.Constant(operand), Expression.Constant(StringComparison.Ordinal)),
            ConditionOperator.EndsWith => Expression.Call(
                value, StringEndsWithMethod, Expression.Constant(operand), Expression.Constant(StringComparison.Ordinal)),
            _ => Expression.Call(
                Expression.Constant(CompileRegex(operand, leaf, context)), RegexIsMatchMethod, value),
        };

        return Expression.Condition(
            Expression.NotEqual(value, Expression.Constant(null, typeof(string))),
            call,
            Expression.Constant(false));
    }

    private static Expression BuildSetOperator(Expression value, ConditionLeaf leaf, Context context)
    {
        Type type = value.Type;
        bool negate = leaf.Operator == ConditionOperator.NotIn;
        var items = (object?[])leaf.Value!;

        Type setType = typeof(HashSet<>).MakeGenericType(type);
        object set = Activator.CreateInstance(setType)!;
        MethodInfo add = setType.GetMethod(nameof(HashSet<int>.Add))!;
        int count = 0;

        foreach (object? item in items)
        {
            if (item is null)
            {
                continue;
            }

            object converted;
            if (ValueConverter.IsNumericType(type) && ValueConverter.IsNumericValue(item))
            {
                if (!ValueConverter.TryConvertNumberExact(item, type, out converted))
                {
                    continue; // e.g. 10.5 can never equal an int field — drop it from the set.
                }
            }
            else
            {
                converted = ConvertConstant(item, type, leaf, context);
            }

            add.Invoke(set, new[] { converted });
            count++;
        }

        if (count == 0)
        {
            return Expression.Constant(negate);
        }

        MethodInfo contains = setType.GetMethod(nameof(HashSet<int>.Contains))!;
        Expression membership = Expression.Call(Expression.Constant(set, setType), contains, value);
        Expression result = negate ? Expression.Not(membership) : membership;

        if (!type.IsValueType)
        {
            result = Expression.Condition(
                Expression.NotEqual(value, Expression.Constant(null, type)),
                result,
                Expression.Constant(negate));
        }

        return result;
    }

    private static Expression CallFunction(ConditionLeaf leaf, Expression boxedFieldValue, Context context)
    {
        if (!context.Functions.TryGetValue(leaf.FunctionName!, out IRuleFunction? function))
        {
            throw new RuleCompilationException(
                context.Rule.Id,
                $"custom function '{leaf.FunctionName}' is not registered.");
        }

        return Expression.Call(
            Expression.Constant(function, typeof(IRuleFunction)),
            FunctionEvaluateMethod,
            boxedFieldValue,
            Expression.Constant(leaf.Value, typeof(object)));
    }

    private static Expression MakeOrdering(ConditionOperator @operator, Expression left, Expression right)
        => @operator switch
        {
            ConditionOperator.GreaterThan => Expression.GreaterThan(left, right),
            ConditionOperator.GreaterThanOrEqual => Expression.GreaterThanOrEqual(left, right),
            ConditionOperator.LessThan => Expression.LessThan(left, right),
            _ => Expression.LessThanOrEqual(left, right),
        };

    private static Expression EqualityComparerCall(Expression value, object comparand, Type type)
    {
        Type comparerType = typeof(EqualityComparer<>).MakeGenericType(type);
        object comparer = comparerType.GetProperty(nameof(EqualityComparer<int>.Default))!.GetValue(null)!;
        MethodInfo equals = comparerType.GetMethod(nameof(EqualityComparer<int>.Default.Equals), new[] { type, type })!;
        return Expression.Call(
            Expression.Constant(comparer, comparerType),
            equals,
            value,
            Expression.Constant(comparand, type));
    }

    private static Expression ComparerCall(Expression value, ConstantExpression comparand, Type type)
    {
        Type comparerType = typeof(Comparer<>).MakeGenericType(type);
        object comparer = comparerType.GetProperty(nameof(Comparer<int>.Default))!.GetValue(null)!;
        MethodInfo compare = comparerType.GetMethod(nameof(Comparer<int>.Default.Compare), new[] { type, type })!;
        return Expression.Call(Expression.Constant(comparer, comparerType), compare, value, comparand);
    }

    private static Regex CompileRegex(string pattern, ConditionLeaf leaf, Context context)
    {
        try
        {
            // Bounded: the pattern comes from the rule author, the subject from consumer data,
            // so a catastrophic backtracker must fail loudly instead of pinning the thread.
            return new Regex(pattern, RegexOptions.Compiled, context.RegexTimeout);
        }
        catch (ArgumentException ex)
        {
            throw new RuleCompilationException(
                context.Rule.Id,
                $"field '{leaf.Field}': invalid regular expression \"{pattern}\": {ex.Message}",
                ex);
        }
    }

    private static object ConvertConstant(object comparand, Type type, ConditionLeaf leaf, Context context)
    {
        try
        {
            return ValueConverter.ConvertTo(comparand, type);
        }
        catch (InvalidOperationException ex)
        {
            throw new RuleCompilationException(
                context.Rule.Id,
                $"field '{leaf.Field}': {ex.Message}",
                ex);
        }
    }
}
