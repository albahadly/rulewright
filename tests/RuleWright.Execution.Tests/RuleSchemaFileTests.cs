using System.Reflection;
using System.Text.Json;
using RuleWright.Serialization;
using Xunit;

namespace RuleWright.Execution.Tests;

/// <summary>
/// <c>docs/schema/rule-schema.json</c> and <see cref="RuleSetValidator"/> are two hand-written
/// copies of one closed vocabulary: the schema is what an editor or a CI job checks a document
/// against, the validator is what the engine enforces at load. Nothing links them, which is how
/// <c>stopAfterFirstMatch</c> came to be accepted by the engine and rejected by the published
/// schema - every example still loaded, so the suite stayed green while the two drifted.
///
/// These tests hold each schema object that closes its vocabulary with
/// <c>"additionalProperties": false</c> against the validator's matching list, so the next property
/// added to one copy fails here until it reaches the other.
/// </summary>
public class RuleSchemaFileTests
{
    /// <summary>
    /// Schema definition name per <see cref="RuleSetValidator"/> property-list field. The lists
    /// themselves are read by reflection rather than restated here: a third copy of the vocabulary
    /// would drift exactly the way the second one did.
    /// </summary>
    private static readonly Dictionary<string, string> DefinitionByField = new(StringComparer.Ordinal)
    {
        ["DecisionTableDocumentProperties"] = "decisionTableDocument",
        ["RuleSetProperties"] = "ruleSet",
        ["RuleProperties"] = "rule",
        ["GroupProperties"] = "conditionGroup",
        ["LeafProperties"] = "conditionLeaf",
        ["ActionProperties"] = "action",
        ["DecisionTableProperties"] = "decisionTable",
        ["DecisionInputProperties"] = "decisionInput",
        ["DecisionOutputProperties"] = "decisionOutput",
        ["DecisionRowProperties"] = "decisionRow",
    };

    /// <summary>
    /// Expression objects are the one closed shape the validator does not police with a property
    /// list: it discriminates on exactly one of <c>op</c>, <c>field</c> or <c>literal</c> and
    /// validates from there, so it accepts an undefined key the schema rejects. Named here so that
    /// a definition added later fails <see cref="EveryClosedSchemaDefinition_IsHeldAgainstTheValidator"/>
    /// rather than quietly going unchecked.
    /// </summary>
    private static readonly HashSet<string> DefinitionsWithoutValidatorList = new(StringComparer.Ordinal)
    {
        "literalExpression",
        "fieldExpression",
        "operatorExpression",
    };

    /// <summary>Property names per schema definition, for the definitions that close their set.</summary>
    private static readonly IReadOnlyDictionary<string, string[]> ClosedSchemaDefinitions = ReadClosedSchemaDefinitions();

    public static IEnumerable<object[]> MappedDefinitions()
        => DefinitionByField.Select(pair => new object[] { pair.Key, pair.Value });

    [Theory]
    [MemberData(nameof(MappedDefinitions))]
    public void SchemaDefinition_NamesExactlyTheValidatorsProperties(string fieldName, string definition)
    {
        Assert.True(
            ClosedSchemaDefinitions.TryGetValue(definition, out string[]? schema),
            $"docs/schema/rule-schema.json has no $defs/{definition} that closes its vocabulary with "
                + $"\"additionalProperties\": false, so nothing holds RuleSetValidator.{fieldName} to it.");

        string[] validator = ValidatorProperties(fieldName);
        string[] schemaRejects = validator.Except(schema!, StringComparer.Ordinal).ToArray();
        string[] engineRejects = schema!.Except(validator, StringComparer.Ordinal).ToArray();

        Assert.True(
            schemaRejects.Length == 0 && engineRejects.Length == 0,
            $"$defs/{definition} in docs/schema/rule-schema.json is out of sync with "
                + $"RuleSetValidator.{fieldName}. "
                + $"Accepted by the engine, rejected by the schema: [{string.Join(", ", schemaRejects)}]. "
                + $"Allowed by the schema, rejected by the engine: [{string.Join(", ", engineRejects)}].");
    }

    /// <summary>Catches a definition added to the schema that no validator list is held against.</summary>
    [Fact]
    public void EveryClosedSchemaDefinition_IsHeldAgainstTheValidator()
    {
        string[] unheld = ClosedSchemaDefinitions.Keys
            .Where(name => !DefinitionByField.ContainsValue(name)
                && !DefinitionsWithoutValidatorList.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            unheld.Length == 0,
            "These schema definitions close their vocabulary but nothing compares them to the "
                + $"engine's: [{string.Join(", ", unheld)}]. Map each to its RuleSetValidator "
                + "property list in DefinitionByField, or record why it has none in "
                + "DefinitionsWithoutValidatorList.");
    }

    /// <summary>Catches a property list added to the validator that no schema definition is held against.</summary>
    [Fact]
    public void EveryValidatorPropertyList_IsHeldAgainstTheSchema()
    {
        string[] fields = PropertyListFields()
            .Where(name => !DefinitionByField.ContainsKey(name))
            .ToArray();

        Assert.True(
            fields.Length == 0,
            "These RuleSetValidator property lists have no schema definition mapped to them: "
                + $"[{string.Join(", ", fields)}]. Add each to DefinitionByField.");
    }

    private static string[] ValidatorProperties(string fieldName)
    {
        FieldInfo? field = typeof(RuleSetValidator).GetField(
            fieldName, BindingFlags.NonPublic | BindingFlags.Static);

        Assert.True(
            field is not null,
            $"RuleSetValidator has no field '{fieldName}'. It was renamed or removed - update "
                + "DefinitionByField to match.");

        return (string[])field!.GetValue(null)!;
    }

    /// <summary>
    /// The validator's lists are private, so they are discovered by shape and name rather than
    /// named one by one: a list added there has to be mapped before this suite goes green again.
    /// </summary>
    private static string[] PropertyListFields()
    {
        string[] fields = typeof(RuleSetValidator)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(string[])
                && field.Name.EndsWith("Properties", StringComparison.Ordinal))
            .Select(field => field.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        // Reflection that silently matches nothing would make every assertion here vacuous.
        Assert.NotEmpty(fields);
        return fields;
    }

    private static IReadOnlyDictionary<string, string[]> ReadClosedSchemaDefinitions()
    {
        using JsonDocument schema = JsonDocument.Parse(File.ReadAllText(RepositoryPaths.RuleSchemaFile));

        var definitions = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (JsonProperty definition in schema.RootElement.GetProperty("$defs").EnumerateObject())
        {
            // Only a definition that closes its vocabulary states the whole set; the rest compose
            // other definitions and have nothing to compare.
            if (!definition.Value.TryGetProperty("additionalProperties", out JsonElement additional)
                || additional.ValueKind != JsonValueKind.False)
            {
                continue;
            }

            definitions[definition.Name] = definition.Value.GetProperty("properties")
                .EnumerateObject()
                .Select(property => property.Name)
                .ToArray();
        }

        return definitions;
    }
}
