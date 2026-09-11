using System;
using System.Linq;
using RuleWright.Core;
using RuleWright.Serialization;
using Xunit;

namespace RuleWright.Serialization.Tests;

/// <summary>
/// The discovery catalog must expose the whole closed vocabulary and stay consistent with the
/// domain enums it is derived from — a UI reads it instead of hard-coding operators.
/// </summary>
public class RuleSchemaCatalogTests
{
    [Fact]
    public void ConditionOperators_CoverEveryEnumValue_InDeclarationOrder()
    {
        var expected = (ConditionOperator[])Enum.GetValues(typeof(ConditionOperator));
        Assert.Equal(expected, RuleSchemaCatalog.ConditionOperators.Select(i => i.Operator).ToArray());
    }

    [Fact]
    public void ConditionOperators_HaveDistinctNonEmptyJsonNames()
    {
        string[] names = RuleSchemaCatalog.ConditionOperators.Select(i => i.JsonName).ToArray();
        Assert.All(names, n => Assert.False(string.IsNullOrEmpty(n)));
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("IsNull", OperatorValueKind.None, false, true)]
    [InlineData("Equals", OperatorValueKind.Scalar, true, true)]
    [InlineData("GreaterThan", OperatorValueKind.Scalar, true, true)]
    [InlineData("Contains", OperatorValueKind.Text, true, true)]
    [InlineData("In", OperatorValueKind.Array, true, true)]
    [InlineData("custom", OperatorValueKind.Custom, false, false)]
    public void ConditionOperator_Classification(string jsonName, OperatorValueKind kind, bool requiresValue, bool allowsExpression)
    {
        Assert.True(RuleSchemaCatalog.TryGetConditionOperator(jsonName, out ConditionOperatorInfo info));
        Assert.Equal(kind, info.ValueKind);
        Assert.Equal(requiresValue, info.RequiresValue);
        Assert.Equal(allowsExpression, info.AllowsExpressionLeft);
    }

    [Fact]
    public void CustomOperator_RequiresFunctionName()
    {
        Assert.True(RuleSchemaCatalog.TryGetConditionOperator("custom", out ConditionOperatorInfo info));
        Assert.True(info.RequiresFunctionName);
        Assert.All(
            RuleSchemaCatalog.ConditionOperators.Where(i => i.Operator != ConditionOperator.Custom),
            i => Assert.False(i.RequiresFunctionName));
    }

    [Fact]
    public void ExpressionOperators_CoverEveryEnumValue()
    {
        var expected = (ExpressionOperator[])Enum.GetValues(typeof(ExpressionOperator));
        Assert.Equal(expected, RuleSchemaCatalog.ExpressionOperators.Select(i => i.Operator).ToArray());
    }

    [Theory]
    [InlineData("negate", 1, 1, ExpressionOperatorCategory.Arithmetic)]
    [InlineData("subtract", 2, 2, ExpressionOperatorCategory.Arithmetic)]
    [InlineData("divide", 2, 2, ExpressionOperatorCategory.Arithmetic)]
    [InlineData("add", 2, null, ExpressionOperatorCategory.Arithmetic)]
    [InlineData("concat", 2, null, ExpressionOperatorCategory.Text)]
    [InlineData("coalesce", 2, null, ExpressionOperatorCategory.NullHandling)]
    public void ExpressionOperator_ArityAndCategory(string op, int min, int? max, ExpressionOperatorCategory category)
    {
        Assert.True(RuleSchemaCatalog.TryGetExpressionOperator(op, out ExpressionOperatorInfo info));
        Assert.Equal(min, info.MinOperands);
        Assert.Equal(max, info.MaxOperands);
        Assert.Equal(category, info.Category);
    }

    [Fact]
    public void LogicalOperators_AreAndOrNot_WithArity()
    {
        Assert.Equal(new[] { "AND", "OR", "NOT" }, RuleSchemaCatalog.LogicalOperators.Select(i => i.JsonName).ToArray());

        LogicalOperatorInfo not = RuleSchemaCatalog.LogicalOperators.Single(i => i.JsonName == "NOT");
        Assert.Equal(1, not.MinChildren);
        Assert.Equal(1, not.MaxChildren);

        LogicalOperatorInfo and = RuleSchemaCatalog.LogicalOperators.Single(i => i.JsonName == "AND");
        Assert.Equal(1, and.MinChildren);
        Assert.Null(and.MaxChildren);
    }

    [Fact]
    public void ActionTypes_CoverAllFour_WithValueRequirement()
    {
        Assert.Equal(
            new[] { RuleAction.SetOutputType, RuleAction.AddToOutputType, RuleAction.AppendToOutputType, RuleAction.RemoveOutputType },
            RuleSchemaCatalog.ActionTypes.Select(i => i.Name).ToArray());

        Assert.True(RuleSchemaCatalog.TryGetActionType(RuleAction.RemoveOutputType, out ActionTypeInfo remove));
        Assert.False(remove.RequiresValue);
        Assert.Equal(ActionEffect.Remove, remove.Effect);

        Assert.True(RuleSchemaCatalog.TryGetActionType(RuleAction.SetOutputType, out ActionTypeInfo set));
        Assert.True(set.RequiresValue);
        Assert.Equal(ActionEffect.Replace, set.Effect);
    }

    [Fact]
    public void TryGet_Unknown_ReturnsFalse()
    {
        Assert.False(RuleSchemaCatalog.TryGetConditionOperator("Nope", out _));
        Assert.False(RuleSchemaCatalog.TryGetExpressionOperator("power", out _));
        Assert.False(RuleSchemaCatalog.TryGetActionType("sendEmail", out _));
    }

    [Fact]
    public void TryGet_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => RuleSchemaCatalog.TryGetConditionOperator(null!, out _));
    }
}
