using Microsoft.CodeAnalysis;

namespace Telani.SourceGenerator.Tests;

/// <summary>
/// An incremental generator must not re-run its transform when something unrelated changes, and it
/// must not keep a SemanticModel or a SyntaxNode in the pipeline: both compare by reference, so the
/// node table sees a change on every keystroke and the driver's state table roots the previous
/// Compilation in memory.
/// </summary>
public class IncrementalCachingTests
{
    /// <summary>One compilation that exercises all four generators at once.</summary>
    private const string AllFeatures = """
        using System.Net.Http;
        using System.Text.RegularExpressions;

        namespace Demo
        {
            public abstract class Route
            {
                public abstract string Path { get; }
                public abstract HttpMethod Method { get; }
                internal abstract Regex RequestRegex { get; }
            }

            [TelaniRoute("GET", "/api/items/{id}")]
            public partial class GetItem : Route { }

            [TelaniBuildDate]
            internal sealed partial class BuildDate { }

            [StringValueGenerator]
            public enum Planet { [StringValue("Merkur")] Mercury }

            [AppSettings]
            public sealed partial class AppSettings
            {
                public string? Name { get; set; }
                public object? _extraStuff { get; set; }
            }
        }
        """;

    [Theory]
    [InlineData(GeneratorTest.StringValueGenerator)]
    [InlineData(GeneratorTest.MiniAPIRouter)]
    [InlineData(GeneratorTest.ConfigGenerator)]
    [InlineData(GeneratorTest.DateSourceGenerator)]
    public void An_unrelated_edit_does_not_re_run_the_transform(string generator)
    {
        var steps = GeneratorTest.RunTwiceWithUnrelatedEdit(AllFeatures, generator);

        // This is the step that holds whatever the ForAttributeWithMetadataName transform returned.
        // Modified here means the model compared unequal, which for a correctly written generator
        // can only happen when the annotated code itself changed.
        var reasons = steps["result_ForAttributeWithMetadataName"];

        Assert.DoesNotContain(IncrementalStepRunReason.Modified, reasons);
    }

    [Theory]
    [InlineData(GeneratorTest.StringValueGenerator)]
    [InlineData(GeneratorTest.MiniAPIRouter)]
    [InlineData(GeneratorTest.ConfigGenerator)]
    [InlineData(GeneratorTest.DateSourceGenerator)]
    public void An_unrelated_edit_does_not_regenerate_the_output(string generator)
    {
        var steps = GeneratorTest.RunTwiceWithUnrelatedEdit(AllFeatures, generator);

        foreach (var (name, reasons) in steps.Where(s => s.Key.EndsWith("SourceOutput", StringComparison.Ordinal)))
        {
            Assert.All(reasons, r => Assert.Equal(IncrementalStepRunReason.Cached, r));
        }
    }
}
