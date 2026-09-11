using System.Text.RegularExpressions;
using RuleWright.Core;
using RuleWright.Serialization;
using Xunit;

namespace RuleWright.Execution.Tests;

/// <summary>
/// The Blazor builder is a JavaScript canvas, so it keeps its own copies of the operator and action
/// vocabularies as literal arrays. Nothing links those to the engine's, which is how the collection
/// operators and <c>count</c> came to be missing from the builder long after the engine gained
/// them: every example still loaded, the builder just quietly produced wrong JSON for them.
///
/// These tests hold the canvas's lists against <see cref="RuleSchemaCatalog"/> - the same catalog a
/// third-party UI is meant to build its palette from - so the next operator added to the engine
/// fails here instead of silently degrading the builder.
/// </summary>
public class BlazorBuilderVocabularyTests
{
    private static readonly string Script = File.ReadAllText(RepositoryPaths.BlazorBuilderCanvasScript);

    /// <summary>Compare nodes take a value, so their dropdown is every non-quantifier, non-custom operator.</summary>
    [Fact]
    public void CompareOperators_MatchTheCatalog()
    {
        string[] expected = RuleSchemaCatalog.ConditionOperators
            .Where(o => o.ValueKind != OperatorValueKind.Condition && !o.RequiresFunctionName)
            .Select(o => o.JsonName)
            .ToArray();

        AssertSameSet(expected, ReadArray("OPERATORS"), "OPERATORS");
    }

    /// <summary>
    /// Quantifiers take a per-element condition rather than a value, so they are their own canvas
    /// node type and must stay out of the Compare dropdown - listing one there builds a leaf with a
    /// 'value', which the validator rejects.
    /// </summary>
    [Fact]
    public void Quantifiers_MatchTheCatalog_AndAreNotCompareOperators()
    {
        string[] expected = RuleSchemaCatalog.ConditionOperators
            .Where(o => o.ValueKind == OperatorValueKind.Condition)
            .Select(o => o.JsonName)
            .ToArray();

        string[] quantifiers = ReadArray("QUANTIFIERS");
        AssertSameSet(expected, quantifiers, "QUANTIFIERS");
        Assert.Empty(quantifiers.Intersect(ReadArray("OPERATORS"), StringComparer.Ordinal));
    }

    [Fact]
    public void ExpressionOperators_MatchTheCatalog()
        => AssertSameSet(
            RuleSchemaCatalog.ExpressionOperators.Select(o => o.JsonName).ToArray(),
            ReadArray("EXPR_OPERATORS"),
            "EXPR_OPERATORS");

    [Fact]
    public void ActionTypes_MatchTheCatalog()
        => AssertSameSet(
            RuleSchemaCatalog.ActionTypes.Select(a => a.Name).ToArray(),
            ReadArray("ACTION_TYPES"),
            "ACTION_TYPES");

    /// <summary>
    /// EXPR_ARITY only lists operators whose operand count is fixed; an absent entry means "two or
    /// more". A wrong or missing entry lets the canvas wire an operand count the engine rejects.
    /// </summary>
    [Fact]
    public void ExpressionOperatorArity_MatchesTheCatalog()
    {
        Dictionary<string, (int Min, int Max)> canvas = ReadArity("EXPR_ARITY");

        foreach (ExpressionOperatorInfo op in RuleSchemaCatalog.ExpressionOperators)
        {
            bool listed = canvas.TryGetValue(op.JsonName, out (int Min, int Max) arity);

            if (op.MaxOperands is null)
            {
                Assert.False(
                    listed,
                    $"EXPR_ARITY lists '{op.JsonName}', but it accepts any number of operands from "
                        + $"{op.MinOperands} up - an entry here caps it in the canvas.");
                continue;
            }

            Assert.True(listed, $"EXPR_ARITY is missing '{op.JsonName}' ({op.MinOperands}..{op.MaxOperands}).");
            Assert.Equal((op.MinOperands, op.MaxOperands.Value), arity);
        }
    }

    /// <summary>Every quantifier needs a node to author it, which means a palette entry.</summary>
    [Fact]
    public void Canvas_OffersAPaletteNodeForQuantifiers()
    {
        Assert.Contains("'quant'", Script, StringComparison.Ordinal);

        Assert.Contains(
            "data-type=\"quant\"",
            File.ReadAllText(RepositoryPaths.BlazorBuilderCanvasPage),
            StringComparison.Ordinal);
    }

    private static void AssertSameSet(string[] expected, string[] actual, string declaration)
    {
        string[] missing = expected.Except(actual, StringComparer.Ordinal).ToArray();
        string[] unknown = actual.Except(expected, StringComparer.Ordinal).ToArray();

        Assert.True(
            missing.Length == 0 && unknown.Length == 0,
            $"rule-canvas.js {declaration} has drifted from RuleSchemaCatalog."
                + (missing.Length > 0 ? $" Missing from the canvas: {string.Join(", ", missing)}." : string.Empty)
                + (unknown.Length > 0 ? $" Not in the engine: {string.Join(", ", unknown)}." : string.Empty));
    }

    /// <summary>
    /// Reads one <c>const NAME = [...]</c> literal. None of these declarations contains a
    /// semicolon, so the next one ends it - no JavaScript engine required.
    /// </summary>
    private static string[] ReadArray(string name)
        => Regex.Matches(Declaration(name), "\"([^\"]*)\"")
            .Cast<Match>()
            .Select(m => m.Groups[1].Value)
            .ToArray();

    private static Dictionary<string, (int Min, int Max)> ReadArity(string name)
        => Regex.Matches(
                Declaration(name),
                @"(\w+)\s*:\s*\{\s*min\s*:\s*(\d+)\s*,\s*max\s*:\s*(\d+)\s*\}")
            .Cast<Match>()
            .ToDictionary(
                m => m.Groups[1].Value,
                m => (int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)),
                StringComparer.Ordinal);

    private static string Declaration(string name)
    {
        string marker = "const " + name + " =";
        int start = Script.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"rule-canvas.js no longer declares '{marker}' - update this test with it.");

        int from = start + marker.Length;
        int end = Script.IndexOf(';', from);
        Assert.True(end > from, $"'{marker}' is not terminated by a semicolon.");

        return Script.Substring(from, end - from);
    }
}
