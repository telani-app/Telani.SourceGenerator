using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Telani.SourceGenerator.Tests;

/// <summary>
/// The result of driving one or more generators over a compilation.
/// </summary>
internal sealed class GeneratorRun
{
    public required ImmutableArray<GeneratedSourceResult> Sources { get; init; }

    /// <summary>The exception a generator threw, if any. Roslyn turns this into a CS8785 warning.</summary>
    public required Exception? Exception { get; init; }

    /// <summary>Diagnostics the generators reported themselves.</summary>
    public required ImmutableArray<Diagnostic> GeneratorDiagnostics { get; init; }

    /// <summary>Errors from compiling the original sources together with the generated ones.</summary>
    public required ImmutableArray<Diagnostic> CompileErrors { get; init; }

    /// <summary>The generated file with the given hint name.</summary>
    public string Source(string hintName)
    {
        var match = Sources.SingleOrDefault(s => s.HintName == hintName);
        Assert.False(match.SourceText is null,
            $"No generated source named '{hintName}'. Generated: {string.Join(", ", Sources.Select(s => s.HintName))}");
        return match.SourceText.ToString();
    }

    public bool Has(string hintName) => Sources.Any(s => s.HintName == hintName);

    /// <summary>Every generated file concatenated, for coarse assertions.</summary>
    public string AllSources => string.Join("\n", Sources.Select(s => s.SourceText.ToString()));

    /// <summary>Fails with the full generated source and error list attached, which makes a broken test readable.</summary>
    public void AssertCompiles()
    {
        Assert.Null(Exception);
        Assert.True(CompileErrors.IsEmpty,
            "Expected the generated code to compile, but got:\n  "
            + string.Join("\n  ", CompileErrors.Select(e => e.ToString()))
            + "\n\n--- generated ---\n" + AllSources);
    }
}

internal static class GeneratorTest
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    /// <summary>
    /// The generated attributes rely on <c>System</c> being in scope. Every consuming project in
    /// practice has ImplicitUsings enabled, so the tests mimic that by default; the tests that
    /// cover the missing using directives pass <paramref name="implicitUsings"/> as false.
    /// </summary>
    private const string ImplicitUsingsFile = "global using System;\nglobal using Telani.SourceGenerator;";

    public static IIncrementalGenerator Create(string generatorTypeName)
    {
        var type = typeof(StringValueGenerator).Assembly.GetType("Telani.SourceGenerator." + generatorTypeName, throwOnError: true)!;
        return (IIncrementalGenerator)Activator.CreateInstance(type)!;
    }

    public static CSharpCompilation CreateCompilation(string[] sources, bool implicitUsings = true)
    {
        if (implicitUsings)
        {
            sources = [ImplicitUsingsFile, .. sources];
        }

        var trees = sources.Select((s, i) => CSharpSyntaxTree.ParseText(s, ParseOptions, path: $"Input{i}.cs"));

        return CSharpCompilation.Create(
            "TestCompilation",
            trees,
            Basic.Reference.Assemblies.Net80.References.All,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }

    public static GeneratorRun Run(string source, string generator, bool implicitUsings = true)
        => Run([source], [generator], implicitUsings);

    public static GeneratorRun Run(string[] sources, string[] generators, bool implicitUsings = true)
    {
        var compilation = CreateCompilation(sources, implicitUsings);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators.Select(g => Create(g).AsSourceGenerator()),
            parseOptions: ParseOptions,
            // IncrementalGeneratorOutputKind.None so that RegisterImplementationSourceOutput also runs,
            // which is where DateSourceGenerator puts the body of GetBuildDate.
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

        var result = driver.GetRunResult();

        return new GeneratorRun
        {
            Sources = [.. result.Results.SelectMany(r => r.GeneratedSources)],
            Exception = result.Results.Select(r => r.Exception).FirstOrDefault(e => e is not null),
            GeneratorDiagnostics = [.. result.Results.SelectMany(r => r.Diagnostics)],
            CompileErrors = [.. output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)],
        };
    }

    /// <summary>
    /// Runs a generator twice, changing only a file that has nothing to do with it, and reports how
    /// the driver classified each tracked step the second time around. Used to prove that a generator
    /// keeps its cache instead of re-running its transform on every keystroke.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<IncrementalStepRunReason>> RunTwiceWithUnrelatedEdit(
        string source, string generator)
    {
        const string unrelatedBefore = "namespace Unrelated { class C { void M() { int x = 1; } } }";
        const string unrelatedAfter = "namespace Unrelated { class C { void M() { int x = 2; } } }";

        var first = CreateCompilation([source, unrelatedBefore]);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [Create(generator).AsSourceGenerator()],
            parseOptions: ParseOptions,
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(first);

        var changed = first.SyntaxTrees.Last();
        var second = first.ReplaceSyntaxTree(changed, CSharpSyntaxTree.ParseText(unrelatedAfter, ParseOptions, path: changed.FilePath));

        driver = driver.RunGenerators(second);

        return driver.GetRunResult().Results[0].TrackedSteps.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<IncrementalStepRunReason>)[.. kvp.Value.SelectMany(s => s.Outputs).Select(o => o.Reason)]);
    }

    /// <summary>
    /// Generator type names, so a typo shows up as a missing-member error rather than a failed test.
    /// </summary>
    public const string ConfigGenerator = "ConfigGenerator";
    public const string DateSourceGenerator = "DateSourceGenerator";
    public const string MiniAPIRouter = "MiniAPIRouter";
    public const string StringValueGenerator = "StringValueGenerator";
}
