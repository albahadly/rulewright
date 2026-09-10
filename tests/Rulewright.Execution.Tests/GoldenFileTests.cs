using System.Text.Json;
using Rulewright.Core;
using Rulewright.Json.SystemText;
using Xunit;

namespace Rulewright.Execution.Tests;

/// <summary>
/// Golden-file contract tests: each fixture is a rule document + fact + expected
/// evaluation result. These pin the JSON schema's observable behavior as it evolves
/// toward the future rule-builder UI — a change that breaks a fixture is a breaking
/// change to the contract.
/// </summary>
public class GoldenFileTests
{
    private static readonly string FixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    public static IEnumerable<object[]> FixtureFiles()
        => Directory.GetFiles(FixtureDirectory, "*.json")
            .Select(file => new object[] { Path.GetFileName(file) });

    [Theory]
    [MemberData(nameof(FixtureFiles))]
    public void Fixture_ProducesExpectedResult(string fileName)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(FixtureDirectory, fileName)));
        JsonElement root = document.RootElement;

        string ruleSetJson = root.GetProperty("ruleSet").GetRawText();
        Dictionary<string, object?> fact = SystemTextJsonFacts.ToDictionary(root.GetProperty("fact"));

        bool stopOnFirstMatch = false;
        bool enableTrace = false;
        if (root.TryGetProperty("options", out JsonElement optionsElement))
        {
            if (optionsElement.TryGetProperty("stopOnFirstMatch", out JsonElement stop))
            {
                stopOnFirstMatch = stop.GetBoolean();
            }

            if (optionsElement.TryGetProperty("enableTrace", out JsonElement trace))
            {
                enableTrace = trace.GetBoolean();
            }
        }

        var options = new EvaluationOptions { StopOnFirstMatch = stopOnFirstMatch, EnableTrace = enableTrace };

        LoadedRuleSet loaded = TestEngine.Engine.LoadRuleSet(ruleSetJson);
        RuleEvaluationResult result = TestEngine.Engine.Evaluate(loaded, fact, options);

        JsonElement expected = root.GetProperty("expected");
        string[] expectedFired = expected.GetProperty("firedRules")
            .EnumerateArray()
            .Select(element => element.GetString()!)
            .ToArray();
        Assert.Equal(expectedFired, result.FiredRules.Select(fired => fired.RuleId).ToArray());

        Dictionary<string, object?> expectedOutputs = SystemTextJsonFacts.ToDictionary(expected.GetProperty("outputs"));
        Assert.Equal(expectedOutputs.Count, result.Outputs.Count);
        foreach (KeyValuePair<string, object?> output in expectedOutputs)
        {
            Assert.True(result.Outputs.ContainsKey(output.Key), $"Missing output '{output.Key}' in {fileName}.");
            Assert.Equal(output.Value, result.Outputs[output.Key]);
        }
    }
}
