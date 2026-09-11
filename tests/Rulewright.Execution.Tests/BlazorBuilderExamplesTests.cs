using System.Text.Json;
using Xunit;

namespace Rulewright.Execution.Tests;

/// <summary>
/// The Blazor builder serves the example documents from its own <c>wwwroot/examples/</c> folder,
/// because a WebAssembly app can only serve what lives under its web root. That makes those files
/// a second copy of <c>examples/</c>, so these tests supply what a copy cannot give on its own:
/// proof that it still matches the originals, and that the picker's manifest lists exactly them.
/// </summary>
public class BlazorBuilderExamplesTests
{
    /// <summary>The picker's own index. Editor metadata, not a rule document.</summary>
    private const string ManifestFileName = "manifest.json";

    public static IEnumerable<object[]> ExampleFiles()
        => ExampleFileNames(RepositoryPaths.Examples).Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(ExampleFiles))]
    public void SampleCopy_MatchesRepositoryExample(string fileName)
    {
        string sampleFile = Path.Combine(RepositoryPaths.BlazorBuilderExamples, fileName);

        Assert.True(
            File.Exists(sampleFile),
            $"examples/{fileName} has no counterpart in the Blazor builder. Copy it into "
                + "samples/Rulewright.Sample.BlazorBuilder/wwwroot/examples/ and add it to that folder's manifest.json.");

        // Compared as text with newlines normalised: a checkout can hand the two folders different
        // line endings, and that difference is not drift.
        Assert.Equal(
            ReadNormalized(Path.Combine(RepositoryPaths.Examples, fileName)),
            ReadNormalized(sampleFile));
    }

    /// <summary>Catches a copy left behind after an example is renamed or deleted.</summary>
    [Fact]
    public void Sample_HasExactlyTheRepositoryExamples()
    {
        Assert.Equal(
            ExampleFileNames(RepositoryPaths.Examples),
            ExampleFileNames(RepositoryPaths.BlazorBuilderExamples));
    }

    [Fact]
    public void Manifest_ListsEveryExample()
    {
        using JsonDocument manifest = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(RepositoryPaths.BlazorBuilderExamples, ManifestFileName)));

        string[] listed = manifest.RootElement.EnumerateArray()
            .Select(entry => entry.GetProperty("file").GetString() ?? string.Empty)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        // An example missing from the manifest never reaches the picker's dropdown, and an entry
        // with no file behind it 404s the moment someone selects it.
        Assert.Equal(ExampleFileNames(RepositoryPaths.Examples), listed);
    }

    private static string[] ExampleFileNames(string directory)
        => new DirectoryInfo(directory).GetFiles("*.json")
            .Select(file => file.Name)
            .Where(name => !string.Equals(name, ManifestFileName, StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

    private static string ReadNormalized(string path)
        => File.ReadAllText(path).Replace("\r\n", "\n");
}
