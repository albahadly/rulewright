namespace RuleWright.Serialization;

/// <summary>
/// Parses JSON text into the neutral <see cref="RuleJsonValue"/> tree. Implemented by
/// adapter packages (<c>RuleWright.Json.SystemText</c>, <c>RuleWright.Json.NewtonsoftJson</c>)
/// so consumers choose their JSON library instead of RuleWright forcing one.
/// </summary>
public interface IRuleJsonReader
{
    /// <summary>
    /// Parses a JSON document.
    /// </summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>The parsed value tree.</returns>
    /// <exception cref="RuleParseException">The text is not well-formed JSON.</exception>
    RuleJsonValue Read(string json);
}
