using Rulewright.Core;
using Xunit;
using static Rulewright.Execution.Tests.TestEngine;

namespace Rulewright.Execution.Tests;

/// <summary>
/// Any/All/None quantify a nested condition over the elements of a collection field, and
/// <c>count</c> measures one. Every case runs on both execution paths — a typed fact through the
/// compiled loop, an equivalent dictionary fact through the interpreter — and the two must agree.
/// </summary>
public class CollectionOperatorTests
{
    private sealed class Basket
    {
        public List<Line> Lines { get; set; } = new List<Line>();

        public string[]? Tags { get; set; }

        public List<int>? Scores { get; set; }
    }

    private sealed class Line
    {
        public string Category { get; set; } = string.Empty;

        public int Quantity { get; set; }

        public decimal Price { get; set; }
    }

    private static Basket TypedBasket() => new Basket
    {
        Lines = new List<Line>
        {
            new Line { Category = "books", Quantity = 2, Price = 10m },
            new Line { Category = "alcohol", Quantity = 1, Price = 30m },
            new Line { Category = "books", Quantity = 5, Price = 8m },
        },
        Tags = new[] { "gift", "priority" },
        Scores = new List<int> { 3, 7, 11 },
    };

    private static Dictionary<string, object?> DictionaryBasket() => new Dictionary<string, object?>
    {
        ["Lines"] = new object?[]
        {
            new Dictionary<string, object?> { ["Category"] = "books", ["Quantity"] = 2L, ["Price"] = 10m },
            new Dictionary<string, object?> { ["Category"] = "alcohol", ["Quantity"] = 1L, ["Price"] = 30m },
            new Dictionary<string, object?> { ["Category"] = "books", ["Quantity"] = 5L, ["Price"] = 8m },
        },
        ["Tags"] = new object?[] { "gift", "priority" },
        ["Scores"] = new object?[] { 3L, 7L, 11L },
    };

    /// <summary>Loads a one-condition rule and reports whether it fires on both paths, asserting they agree.</summary>
    private static bool BothPaths(string conditionJson)
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(WrapRule(conditionJson));

        RuleEvaluationResult compiled = Engine.Evaluate(loaded, TypedBasket());
        RuleEvaluationResult interpreted = Engine.Evaluate(loaded, DictionaryBasket());

        Assert.Equal(CompilationMode.Compiled, compiled.CompilationMode);
        Assert.Equal(CompilationMode.Interpreted, interpreted.CompilationMode);
        Assert.Equal(compiled.FiredRules.Count, interpreted.FiredRules.Count);
        return compiled.FiredRules.Count == 1;
    }

    [Theory]
    // Any: at least one element satisfies the condition.
    [InlineData(@"{ ""field"": ""Lines"", ""operator"": ""Any"",
        ""condition"": { ""field"": ""Category"", ""operator"": ""Equals"", ""value"": ""alcohol"" } }", true)]
    [InlineData(@"{ ""field"": ""Lines"", ""operator"": ""Any"",
        ""condition"": { ""field"": ""Category"", ""operator"": ""Equals"", ""value"": ""firearms"" } }", false)]
    // All: every element satisfies it.
    [InlineData(@"{ ""field"": ""Lines"", ""operator"": ""All"",
        ""condition"": { ""field"": ""Quantity"", ""operator"": ""GreaterThan"", ""value"": 0 } }", true)]
    [InlineData(@"{ ""field"": ""Lines"", ""operator"": ""All"",
        ""condition"": { ""field"": ""Category"", ""operator"": ""Equals"", ""value"": ""books"" } }", false)]
    // None: no element satisfies it.
    [InlineData(@"{ ""field"": ""Lines"", ""operator"": ""None"",
        ""condition"": { ""field"": ""Category"", ""operator"": ""Equals"", ""value"": ""firearms"" } }", true)]
    [InlineData(@"{ ""field"": ""Lines"", ""operator"": ""None"",
        ""condition"": { ""field"": ""Category"", ""operator"": ""Equals"", ""value"": ""alcohol"" } }", false)]
    public void Quantifiers_AgreeOnBothPaths(string condition, bool expected)
        => Assert.Equal(expected, BothPaths(condition));

    /// <summary>The element condition is an ordinary condition tree, so groups work inside it.</summary>
    [Theory]
    [InlineData("books", 5, true)]    // the third line is books with quantity 5
    [InlineData("books", 9, false)]   // no books line has quantity 9
    [InlineData("alcohol", 5, false)] // the alcohol line has quantity 1
    public void ElementCondition_SupportsGroups(string category, int quantity, bool expected)
    {
        string condition = $@"{{ ""field"": ""Lines"", ""operator"": ""Any"", ""condition"": {{
            ""type"": ""group"", ""operator"": ""AND"", ""rules"": [
              {{ ""field"": ""Category"", ""operator"": ""Equals"", ""value"": ""{category}"" }},
              {{ ""field"": ""Quantity"", ""operator"": ""GreaterThanOrEqual"", ""value"": {quantity} }} ] }} }}";

        Assert.Equal(expected, BothPaths(condition));
    }

    /// <summary>"$" names the element itself, which is how a collection of scalars is tested.</summary>
    [Theory]
    [InlineData(@"{ ""field"": ""Tags"", ""operator"": ""Any"",
        ""condition"": { ""field"": ""$"", ""operator"": ""Equals"", ""value"": ""priority"" } }", true)]
    [InlineData(@"{ ""field"": ""Tags"", ""operator"": ""Any"",
        ""condition"": { ""field"": ""$"", ""operator"": ""Equals"", ""value"": ""nope"" } }", false)]
    [InlineData(@"{ ""field"": ""Tags"", ""operator"": ""None"",
        ""condition"": { ""field"": ""$"", ""operator"": ""StartsWith"", ""value"": ""z"" } }", true)]
    [InlineData(@"{ ""field"": ""Scores"", ""operator"": ""All"",
        ""condition"": { ""field"": ""$"", ""operator"": ""GreaterThan"", ""value"": 2 } }", true)]
    [InlineData(@"{ ""field"": ""Scores"", ""operator"": ""Any"",
        ""condition"": { ""field"": ""$"", ""operator"": ""GreaterThan"", ""value"": 10 } }", true)]
    [InlineData(@"{ ""field"": ""Scores"", ""operator"": ""Any"",
        ""condition"": { ""field"": ""$"", ""operator"": ""In"", ""value"": [7, 99] } }", true)]
    public void ElementSelfPath_TestsScalarElements(string condition, bool expected)
        => Assert.Equal(expected, BothPaths(condition));

    /// <summary>Quantifiers nest: a collection of collections, or a quantifier inside a group.</summary>
    [Fact]
    public void Quantifiers_Nest()
    {
        const string condition = @"{ ""field"": ""Lines"", ""operator"": ""Any"", ""condition"": {
            ""type"": ""group"", ""operator"": ""OR"", ""rules"": [
              { ""field"": ""Category"", ""operator"": ""Equals"", ""value"": ""firearms"" },
              { ""field"": ""Price"", ""operator"": ""GreaterThan"", ""value"": 25 } ] } }";

        Assert.True(BothPaths(condition));
    }

    /// <summary>
    /// A null collection is the field's absence, so it follows the ordinary null semantics —
    /// false for Any and All, true for None, exactly as NotIn is true for a null field. An empty
    /// collection is a collection, so All and None are vacuously true over it.
    /// </summary>
    [Theory]
    [InlineData("Any", null, false)]
    [InlineData("All", null, false)]
    [InlineData("None", null, true)]
    [InlineData("Any", "empty", false)]
    [InlineData("All", "empty", true)]
    [InlineData("None", "empty", true)]
    public void NullAndEmptyCollections(string quantifier, string? shape, bool expected)
    {
        string condition = $@"{{ ""field"": ""Tags"", ""operator"": ""{quantifier}"",
            ""condition"": {{ ""field"": ""$"", ""operator"": ""IsNotNull"" }} }}";

        LoadedRuleSet loaded = Engine.LoadRuleSet(WrapRule(condition));

        var typed = new Basket { Tags = shape == "empty" ? Array.Empty<string>() : null };
        var dictionary = new Dictionary<string, object?>
        {
            ["Tags"] = shape == "empty" ? Array.Empty<object?>() : null,
        };

        bool compiled = Engine.Evaluate(loaded, typed).FiredRules.Count == 1;
        bool interpreted = Engine.Evaluate(loaded, dictionary).FiredRules.Count == 1;

        Assert.Equal(expected, compiled);
        Assert.Equal(expected, interpreted);
    }

    /// <summary>A string is text, not a collection of characters.</summary>
    [Theory]
    [InlineData("Any", false)]
    [InlineData("All", false)]
    [InlineData("None", true)]
    public void AStringIsNotACollection(string quantifier, bool expected)
    {
        string condition = $@"{{ ""field"": ""Text"", ""operator"": ""{quantifier}"",
            ""condition"": {{ ""field"": ""$"", ""operator"": ""IsNotNull"" }} }}";

        LoadedRuleSet loaded = Engine.LoadRuleSet(WrapRule(condition));
        var fact = new Dictionary<string, object?> { ["Text"] = "abc" };
        Assert.Equal(expected, Engine.Evaluate(loaded, fact).FiredRules.Count == 1);
    }

    // --- count ---

    /// <summary>
    /// count measures a collection, so "more than three lines" works through the existing
    /// computed left-hand side rather than needing an operator of its own.
    /// </summary>
    [Theory]
    [InlineData("GreaterThan", 2, true)]
    [InlineData("GreaterThan", 3, false)]
    [InlineData("Equals", 3, true)]
    [InlineData("LessThanOrEqual", 3, true)]
    public void Count_MeasuresACollection(string op, int value, bool expected)
    {
        string condition = $@"{{ ""expression"": {{ ""op"": ""count"", ""operands"": [ {{ ""field"": ""Lines"" }} ] }},
            ""operator"": ""{op}"", ""value"": {value} }}";

        Assert.Equal(expected, BothPaths(condition));
    }

    /// <summary>count is total: a null, a non-collection, and a string all yield null, not an error.</summary>
    [Theory]
    [InlineData(null, false)]
    [InlineData(42, false)]
    [InlineData("abc", false)]
    public void Count_OfANonCollectionIsNull(object? value, bool expected)
    {
        const string condition = @"{ ""expression"": { ""op"": ""count"", ""operands"": [ { ""field"": ""Thing"" } ] },
            ""operator"": ""GreaterThanOrEqual"", ""value"": 0 }";

        LoadedRuleSet loaded = Engine.LoadRuleSet(WrapRule(condition));
        var fact = new Dictionary<string, object?> { ["Thing"] = value };
        Assert.Equal(expected, Engine.Evaluate(loaded, fact).FiredRules.Count == 1);
    }

    /// <summary>count feeds an output as readily as a condition.</summary>
    [Fact]
    public void Count_CanBeWrittenToAnOutput()
    {
        const string json = @"{ ""id"": ""c"",
            ""condition"": { ""field"": ""Lines"", ""operator"": ""Any"",
                ""condition"": { ""field"": ""Category"", ""operator"": ""IsNotNull"" } },
            ""actions"": [ { ""type"": ""setOutput"", ""target"": ""LineCount"",
                ""value"": { ""op"": ""count"", ""operands"": [ { ""field"": ""Lines"" } ] } } ] }";

        LoadedRuleSet loaded = Engine.LoadRuleSet(json);
        Assert.Equal(3L, Engine.Evaluate(loaded, TypedBasket()).Outputs["LineCount"]);
        Assert.Equal(3L, Engine.Evaluate(loaded, DictionaryBasket()).Outputs["LineCount"]);
    }

    // --- Trace and hashing ---

    /// <summary>
    /// A quantifier is one trace node: per-element results have no single slot, so the element
    /// condition is rendered into the node's description instead of being traced separately.
    /// </summary>
    [Fact]
    public void Trace_RendersTheElementConditionInline()
    {
        const string condition = @"{ ""field"": ""Lines"", ""operator"": ""Any"", ""condition"": {
            ""type"": ""group"", ""operator"": ""AND"", ""rules"": [
              { ""field"": ""Category"", ""operator"": ""Equals"", ""value"": ""alcohol"" },
              { ""field"": ""Quantity"", ""operator"": ""GreaterThan"", ""value"": 0 } ] } }";

        RuleEvaluationResult result = Engine.Evaluate(
            Engine.LoadRuleSet(WrapRule(condition)), TypedBasket(), new EvaluationOptions { EnableTrace = true });

        ConditionTraceNode node = result.Trace!.Rules.Single().Condition!;
        Assert.Equal(
            "Lines Any (AND(Category Equals \"alcohol\", Quantity GreaterThan 0))",
            node.Description);
        Assert.True(node.Passed);
        Assert.Empty(node.Children);
    }

    /// <summary>
    /// Two quantifiers over the same field differing only in their element condition must not
    /// share a compiled delegate, so the element condition has to be part of the content hash.
    /// </summary>
    [Fact]
    public void ElementCondition_IsPartOfTheContentHash()
    {
        const string alcohol = @"{ ""field"": ""Lines"", ""operator"": ""Any"",
            ""condition"": { ""field"": ""Category"", ""operator"": ""Equals"", ""value"": ""alcohol"" } }";
        const string firearms = @"{ ""field"": ""Lines"", ""operator"": ""Any"",
            ""condition"": { ""field"": ""Category"", ""operator"": ""Equals"", ""value"": ""firearms"" } }";

        Assert.True(BothPaths(alcohol));
        Assert.False(BothPaths(firearms));
    }

    // --- Domain model ---

    /// <summary>The quantifier is a factory, not a constructor overload, so existing calls are untouched.</summary>
    [Fact]
    public void Quantifier_IsBuildableFromTheDomainModel()
    {
        ConditionLeaf leaf = ConditionLeaf.Quantifier(
            "Lines",
            ConditionOperator.Any,
            new ConditionLeaf("Category", ConditionOperator.Equal, "alcohol"));

        Assert.Equal(ConditionOperator.Any, leaf.Operator);
        Assert.NotNull(leaf.ElementCondition);

        var ruleSet = new RuleSet(new[] { new Rule("q", leaf) });
        Assert.Single(Engine.Evaluate(Engine.LoadRuleSet(ruleSet), TypedBasket()).FiredRules);
    }

    [Fact]
    public void Quantifier_RejectsMismatchedOperators()
    {
        var inner = new ConditionLeaf("Category", ConditionOperator.Equal, "x");

        // A quantifier needs a condition, not a value.
        Assert.Throws<ArgumentException>(() => new ConditionLeaf("Lines", ConditionOperator.Any, "x"));

        // And a value operator cannot take a per-element condition.
        Assert.Throws<ArgumentException>(() => ConditionLeaf.Quantifier("Lines", ConditionOperator.Equal, inner));

        Assert.Throws<ArgumentException>(() => ConditionLeaf.Quantifier(string.Empty, ConditionOperator.Any, inner));
        Assert.Throws<ArgumentNullException>(() => ConditionLeaf.Quantifier("Lines", ConditionOperator.Any, null!));
    }
}
