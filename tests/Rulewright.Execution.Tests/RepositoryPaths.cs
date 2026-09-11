namespace Rulewright.Execution.Tests;

/// <summary>
/// Locates repository folders from a test run's output directory, which sits several levels below
/// the repository root and at a different depth per target framework and configuration.
/// </summary>
internal static class RepositoryPaths
{
    private static readonly string RootDirectory = FindRoot();

    /// <summary>The repository's canonical <c>examples/</c> folder.</summary>
    internal static string Examples { get; } = Path.Combine(RootDirectory, "examples");

    /// <summary>
    /// The Blazor builder's served copy of the examples. A WebAssembly app can only serve files
    /// that live under its own <c>wwwroot</c>, so the sample keeps its own copy of every example
    /// document; <see cref="BlazorBuilderExamplesTests"/> keeps that copy honest.
    /// </summary>
    internal static string BlazorBuilderExamples { get; } = Path.Combine(
        RootDirectory, "samples", "Rulewright.Sample.BlazorBuilder", "wwwroot", "examples");

    /// <summary>
    /// The Blazor builder's canvas script. It hardcodes its own copies of the operator and action
    /// vocabularies in JavaScript, so <see cref="BlazorBuilderVocabularyTests"/> holds them against
    /// <c>RuleSchemaCatalog</c>.
    /// </summary>
    internal static string BlazorBuilderCanvasScript { get; } = Path.Combine(
        RootDirectory, "samples", "Rulewright.Sample.BlazorBuilder", "wwwroot", "js", "rule-canvas.js");

    /// <summary>The Blazor builder's page shell, which carries the node palette.</summary>
    internal static string BlazorBuilderCanvasPage { get; } = Path.Combine(
        RootDirectory, "samples", "Rulewright.Sample.BlazorBuilder", "Pages", "Canvas.razor");

    private static string FindRoot()
    {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Rulewright.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository root: no Rulewright.slnx above the test output directory.");
    }
}
