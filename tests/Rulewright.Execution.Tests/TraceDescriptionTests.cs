using Rulewright.Core;
using Xunit;
using static Rulewright.Execution.Tests.TestEngine;

namespace Rulewright.Execution.Tests;

/// <summary>
/// A trace has to name the operator it actually ran. The describer maps each
/// <see cref="ExpressionOperator"/> to its JSON spelling by hand, and a catch-all arm there renders
/// every operator it forgets under some other operator's name - which is how <c>count</c>, added
/// with the collection operators, traced itself as <c>coalesce</c>.
/// </summary>
public class TraceDescriptionTests
{
    /// <summary>Operands chosen so each operator evaluates cleanly; only the name is under test.</summary>
    private static readonly Dictionary<ExpressionOperator, ValueExpression[]> Operands = new()
    {
        [ExpressionOperator.Add] = Literals(1L, 2L),
        [ExpressionOperator.Subtract] = Literals(3L, 1L),
        [ExpressionOperator.Multiply] = Literals(2L, 3L),
        [ExpressionOperator.Divide] = Literals(6L, 2L),
        [ExpressionOperator.Modulo] = Literals(7L, 2L),
        [ExpressionOperator.Negate] = Literals(5L),
        [ExpressionOperator.Concat] = Literals("a", "b"),
        [ExpressionOperator.Coalesce] = Literals(null, 1L),
        // count's operand is the collection itself, not a scalar.
        [ExpressionOperator.Count] = new ValueExpression[] { new FieldExpression("Items") },
    };

    public static TheoryData<ExpressionOperator, string> ExpectedNames => new()
    {
        { ExpressionOperator.Add, "add" },
        { ExpressionOperator.Subtract, "subtract" },
        { ExpressionOperator.Multiply, "multiply" },
        { ExpressionOperator.Divide, "divide" },
        { ExpressionOperator.Modulo, "modulo" },
        { ExpressionOperator.Negate, "negate" },
        { ExpressionOperator.Concat, "concat" },
        { ExpressionOperator.Coalesce, "coalesce" },
        { ExpressionOperator.Count, "count" },
    };

    [Theory]
    [MemberData(nameof(ExpectedNames))]
    public void Trace_NamesTheExpressionOperatorItRan(ExpressionOperator @operator, string expectedName)
    {
        Assert.StartsWith(expectedName + "(", Describe(@operator));
    }

    /// <summary>The regression itself: count described itself as coalesce.</summary>
    [Fact]
    public void Trace_NamesCount_NotCoalesce()
    {
        string description = Describe(ExpressionOperator.Count);

        Assert.StartsWith("count(Items)", description);
        Assert.DoesNotContain("coalesce", description);
    }

    /// <summary>
    /// The guard against the catch-all coming back: every operator in the enum is named here, and
    /// no two of them describe themselves the same way. An operator added later fails this until
    /// the describer - and this table - name it.
    /// </summary>
    [Fact]
    public void Trace_GivesEveryExpressionOperatorItsOwnName()
    {
        ExpressionOperator[] all = Enum.GetValues(typeof(ExpressionOperator))
            .Cast<ExpressionOperator>().ToArray();

        Assert.Equal(all.OrderBy(op => op), Operands.Keys.OrderBy(op => op));

        string[] names = all.Select(op => Describe(op).Split('(')[0]).ToArray();
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// Runs the expression as a rule's left-hand side and returns what the trace called it.
    /// The describer is internal, so a real evaluation is the only way to reach it.
    /// </summary>
    private static string Describe(ExpressionOperator @operator)
    {
        var left = new OperatorExpression(@operator, Operands[@operator]);
        var rule = new Rule("described", new ConditionLeaf(left, ConditionOperator.IsNotNull, null));
        LoadedRuleSet loaded = Engine.LoadRuleSet(new RuleSet(new[] { rule }));

        var fact = new Dictionary<string, object?> { ["Items"] = new object?[] { 1L, 2L, 3L } };
        RuleEvaluationResult result = Engine.Evaluate(loaded, fact, new EvaluationOptions { EnableTrace = true });

        return result.Trace!.Rules.Single().Condition!.Description;
    }

    private static ValueExpression[] Literals(params object?[] values)
        => values.Select(v => (ValueExpression)new LiteralExpression(v)).ToArray();
}
