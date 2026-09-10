using System.Linq;
using Rulewright.Core;
using Rulewright.Json.SystemText;
using Rulewright.Serialization;
using Xunit;

namespace Rulewright.Serialization.Tests;

public class DecisionTableTests
{
    private const string ValidTable = @"{
      ""decisionTable"": {
        ""id"": ""shipping"",
        ""hitPolicy"": ""collect"",
        ""inputs"": [
          { ""field"": ""Customer.Tier"", ""operator"": ""Equals"" },
          { ""field"": ""Order.Total"", ""operator"": ""GreaterThanOrEqual"" }
        ],
        ""outputs"": [ { ""target"": ""Discount"" }, { ""target"": ""Label"" } ],
        ""rows"": [
          { ""when"": [""VIP"", 100], ""then"": [20, ""vip-big""] },
          { ""when"": [""VIP"", null], ""then"": [10, ""vip""] },
          { ""when"": [null, null], ""then"": [0, ""default""] }
        ]
      }
    }";

    private static RuleSet Parse(string json) => RuleSetParser.Parse(new SystemTextJsonReader().Read(json));

    private static RuleSetValidationResult Validate(string json) => RuleSetValidator.Validate(new SystemTextJsonReader().Read(json));

    // --- Validation ---

    [Fact]
    public void ValidTable_Passes() => Assert.True(Validate(ValidTable).IsValid);

    [Fact]
    public void MissingInputs_IsReported()
    {
        RuleSetValidationResult result = Validate(@"{ ""decisionTable"": {
            ""outputs"": [ { ""target"": ""D"" } ],
            ""rows"": [ { ""when"": [], ""then"": [1] } ] } }");
        Assert.Contains(result.Errors, e => e.Path == "/decisionTable/inputs");
    }

    [Fact]
    public void BadHitPolicy_IsReported()
    {
        RuleSetValidationResult result = Validate(@"{ ""decisionTable"": {
            ""hitPolicy"": ""priority"",
            ""inputs"": [ { ""field"": ""A"" } ],
            ""outputs"": [ { ""target"": ""D"" } ],
            ""rows"": [ { ""when"": [1], ""then"": [1] } ] } }");
        Assert.Contains(result.Errors, e => e.Path == "/decisionTable/hitPolicy");
    }

    [Fact]
    public void CellCountMismatch_IsReported()
    {
        RuleSetValidationResult whenResult = Validate(@"{ ""decisionTable"": {
            ""inputs"": [ { ""field"": ""A"" }, { ""field"": ""B"" } ],
            ""outputs"": [ { ""target"": ""D"" } ],
            ""rows"": [ { ""when"": [1], ""then"": [1] } ] } }");
        Assert.Contains(whenResult.Errors, e => e.Path == "/decisionTable/rows/0/when");

        RuleSetValidationResult thenResult = Validate(@"{ ""decisionTable"": {
            ""inputs"": [ { ""field"": ""A"" } ],
            ""outputs"": [ { ""target"": ""D"" }, { ""target"": ""E"" } ],
            ""rows"": [ { ""when"": [1], ""then"": [1] } ] } }");
        Assert.Contains(thenResult.Errors, e => e.Path == "/decisionTable/rows/0/then");
    }

    [Fact]
    public void NonComparisonInputOperator_IsReported()
    {
        RuleSetValidationResult result = Validate(@"{ ""decisionTable"": {
            ""inputs"": [ { ""field"": ""A"", ""operator"": ""IsNull"" } ],
            ""outputs"": [ { ""target"": ""D"" } ],
            ""rows"": [ { ""when"": [null], ""then"": [1] } ] } }");
        Assert.Contains(result.Errors, e => e.Path == "/decisionTable/inputs/0/operator");
    }

    [Fact]
    public void InColumnCellMustBeArray_IsReported()
    {
        RuleSetValidationResult result = Validate(@"{ ""decisionTable"": {
            ""inputs"": [ { ""field"": ""A"", ""operator"": ""In"" } ],
            ""outputs"": [ { ""target"": ""D"" } ],
            ""rows"": [ { ""when"": [5], ""then"": [1] } ] } }");
        Assert.Contains(result.Errors, e => e.Path == "/decisionTable/rows/0/when/0");
    }

    [Fact]
    public void BadThenExpression_ReportsNestedPointer()
    {
        RuleSetValidationResult result = Validate(@"{ ""decisionTable"": {
            ""inputs"": [ { ""field"": ""A"" } ],
            ""outputs"": [ { ""target"": ""D"" } ],
            ""rows"": [ { ""when"": [1], ""then"": [ { ""op"": ""divide"", ""operands"": [1] } ] } ] } }");
        Assert.Contains(result.Errors, e => e.Path == "/decisionTable/rows/0/then/0/operands");
    }

    // --- Expansion ---

    [Fact]
    public void ExpandsToOneRulePerRow_WithDescendingPriority()
    {
        RuleSet ruleSet = Parse(ValidTable);
        Assert.Equal(new[] { "shipping-0", "shipping-1", "shipping-2" }, ruleSet.Rules.Select(r => r.Id).ToArray());
        Assert.Equal(new[] { 3, 2, 1 }, ruleSet.Rules.Select(r => r.Priority).ToArray());
    }

    [Fact]
    public void TwoActiveCells_BecomeAndGroup()
    {
        Rule row0 = Parse(ValidTable).Rules[0];
        var group = Assert.IsType<ConditionGroup>(row0.Condition);
        Assert.Equal(LogicalOperator.And, group.Operator);
        Assert.Equal(2, group.Children.Count);
        var tier = Assert.IsType<ConditionLeaf>(group.Children[0]);
        Assert.Equal("Customer.Tier", tier.Field);
        Assert.Equal(ConditionOperator.Equal, tier.Operator);
        Assert.Equal("VIP", tier.Value);
    }

    [Fact]
    public void OneActiveCell_BecomesLeaf_WildcardsDropped()
    {
        Rule row1 = Parse(ValidTable).Rules[1];
        var leaf = Assert.IsType<ConditionLeaf>(row1.Condition);
        Assert.Equal("Customer.Tier", leaf.Field);
        Assert.Equal("VIP", leaf.Value);
    }

    [Fact]
    public void AllWildcardRow_BecomesCatchAll()
    {
        Rule row2 = Parse(ValidTable).Rules[2];
        var group = Assert.IsType<ConditionGroup>(row2.Condition);
        Assert.Equal(LogicalOperator.Or, group.Operator);
        Assert.Equal(
            new[] { ConditionOperator.IsNotNull, ConditionOperator.IsNull },
            group.Children.Cast<ConditionLeaf>().Select(l => l.Operator).ToArray());
    }

    [Fact]
    public void ThenCells_BecomeActions_WithColumnTargets()
    {
        Rule row0 = Parse(ValidTable).Rules[0];
        Assert.Equal(new[] { "Discount", "Label" }, row0.Actions.Select(a => a.Target).ToArray());
        Assert.Equal(20L, Assert.IsType<LiteralExpression>(row0.Actions[0].Value).Value);
        Assert.All(row0.Actions, a => Assert.Equal(RuleAction.SetOutputType, a.Type));
    }

    [Fact]
    public void FirstPolicy_BakesNegationOfEarlierRows()
    {
        string firstTable = ValidTable.Replace("\"hitPolicy\": \"collect\"", "\"hitPolicy\": \"first\"");
        Rule row1 = Parse(firstTable).Rules[1];

        var group = Assert.IsType<ConditionGroup>(row1.Condition);
        Assert.Equal(LogicalOperator.And, group.Operator);
        Assert.Equal(2, group.Children.Count);
        Assert.IsType<ConditionLeaf>(group.Children[0]);            // this row's own condition
        var negation = Assert.IsType<ConditionGroup>(group.Children[1]);
        Assert.Equal(LogicalOperator.Not, negation.Operator);       // NOT(row 0's own condition)
    }

    [Fact]
    public void NullThenCell_SkipsThatOutput()
    {
        RuleSet ruleSet = Parse(@"{ ""decisionTable"": {
            ""inputs"": [ { ""field"": ""A"" } ],
            ""outputs"": [ { ""target"": ""D"" }, { ""target"": ""E"" } ],
            ""rows"": [ { ""when"": [1], ""then"": [5, null] } ] } }");
        Rule row = ruleSet.Rules[0];
        Assert.Equal("D", Assert.Single(row.Actions).Target);
    }

    [Fact]
    public void OutputType_CarriesToAction()
    {
        RuleSet ruleSet = Parse(@"{ ""decisionTable"": {
            ""inputs"": [ { ""field"": ""A"" } ],
            ""outputs"": [ { ""target"": ""Score"", ""type"": ""addToOutput"" } ],
            ""rows"": [ { ""when"": [1], ""then"": [5] } ] } }");
        Assert.Equal(RuleAction.AddToOutputType, ruleSet.Rules[0].Actions[0].Type);
    }

    /// <summary>
    /// An In/NotIn cell holds the same operand shapes a leaf's In/NotIn array does - scalars only.
    /// A null or nested array has no CLR mapping the two execution paths agree on, so it is
    /// rejected at the same pointer depth the leaf validator uses.
    /// </summary>
    [Fact]
    public void InColumnCellItemsMustBeScalars_IsReported()
    {
        RuleSetValidationResult nullItem = Validate(@"{ ""decisionTable"": {
            ""inputs"": [ { ""field"": ""A"", ""operator"": ""In"" } ],
            ""outputs"": [ { ""target"": ""D"" } ],
            ""rows"": [ { ""when"": [ [null, ""gold""] ], ""then"": [1] } ] } }");
        Assert.Contains(nullItem.Errors, e => e.Path == "/decisionTable/rows/0/when/0/0");

        RuleSetValidationResult nestedArray = Validate(@"{ ""decisionTable"": {
            ""inputs"": [ { ""field"": ""A"", ""operator"": ""NotIn"" } ],
            ""outputs"": [ { ""target"": ""D"" } ],
            ""rows"": [ { ""when"": [ [ [""gold""] ] ], ""then"": [1] } ] } }");
        Assert.Contains(nestedArray.Errors, e => e.Path == "/decisionTable/rows/0/when/0/0");

        RuleSetValidationResult scalars = Validate(@"{ ""decisionTable"": {
            ""inputs"": [ { ""field"": ""A"", ""operator"": ""In"" } ],
            ""outputs"": [ { ""target"": ""D"" } ],
            ""rows"": [ { ""when"": [ [""gold"", 1, true] ], ""then"": [1] } ] } }");
        Assert.True(scalars.IsValid);
    }

    /// <summary>
    /// A cell's regular expression is compiled during validation, exactly as a leaf's is, so a bad
    /// pattern is an error with a pointer rather than a compilation failure at load time.
    /// </summary>
    [Fact]
    public void MatchesRegexCellWithBadPattern_IsReported()
    {
        RuleSetValidationResult result = Validate(@"{ ""decisionTable"": {
            ""inputs"": [ { ""field"": ""A"", ""operator"": ""MatchesRegex"" } ],
            ""outputs"": [ { ""target"": ""D"" } ],
            ""rows"": [ { ""when"": [""(""], ""then"": [1] } ] } }");
        Assert.Contains(result.Errors, e => e.Path == "/decisionTable/rows/0/when/0");
    }
}
