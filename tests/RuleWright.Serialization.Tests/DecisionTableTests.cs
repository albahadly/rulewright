using System.Linq;
using RuleWright.Core;
using RuleWright.Json.SystemText;
using RuleWright.Serialization;
using Xunit;

namespace RuleWright.Serialization.Tests;

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

    /// <summary>
    /// The `first` hit policy rides on the rule set, not on the row conditions. Encoding it by
    /// ANDing each row with the negation of every earlier row was quadratic and made each row's
    /// condition unreadable; rows are already in priority order, so stopping after the first match
    /// is equivalent and linear.
    /// </summary>
    [Fact]
    public void FirstPolicy_SetsStopAfterFirstMatch_AndLeavesRowConditionsAlone()
    {
        string firstTable = ValidTable.Replace("\"hitPolicy\": \"collect\"", "\"hitPolicy\": \"first\"");
        RuleSet parsed = Parse(firstTable);

        Assert.True(parsed.StopAfterFirstMatch);

        // Row 1 is "VIP" with a wildcard total, so its condition is exactly its own single leaf -
        // no negation of row 0 grafted on.
        var leaf = Assert.IsType<ConditionLeaf>(parsed.Rules[1].Condition);
        Assert.Equal("Customer.Tier", leaf.Field);

        // Rows stay in document order via descending priority.
        Assert.Equal(new[] { "shipping-0", "shipping-1", "shipping-2" }, parsed.Rules.Select(r => r.Id).ToArray());
        Assert.True(parsed.Rules[0].Priority > parsed.Rules[1].Priority);
    }

    [Fact]
    public void CollectPolicy_DoesNotStopAfterFirstMatch()
        => Assert.False(Parse(ValidTable).StopAfterFirstMatch);

    /// <summary>The row count no longer drives the condition-node count: expansion is linear.</summary>
    [Fact]
    public void FirstPolicy_ExpansionIsLinearInRowCount()
    {
        static int Nodes(ConditionNode n) => n is ConditionGroup g ? 1 + g.Children.Sum(Nodes) : 1;

        static string Table(int rows)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(@"{ ""decisionTable"": { ""hitPolicy"": ""first"", ""inputs"": [ { ""field"": ""A"" } ], ""outputs"": [ { ""target"": ""D"" } ], ""rows"": [");
            for (int i = 0; i < rows; i++)
            {
                sb.Append(i > 0 ? "," : string.Empty).Append(@"{ ""when"": [").Append(i).Append(@"], ""then"": [").Append(i).Append("] }");
            }

            return sb.Append("] } }").ToString();
        }

        int small = Parse(Table(10)).Rules.Sum(r => Nodes(r.Condition));
        int large = Parse(Table(100)).Rules.Sum(r => Nodes(r.Condition));

        Assert.Equal(10, small);
        Assert.Equal(100, large);   // was 5,050 under the negation-chain encoding
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
