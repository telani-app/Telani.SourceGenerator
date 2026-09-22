namespace Telani.SourceGenerator.Tests;

/// <summary>
/// The marker attributes are emitted from RegisterPostInitializationOutput, so they are part of the
/// package's contract with every consumer.
/// </summary>
public class GeneratedAttributeTests
{
    [Theory]
    [InlineData(GeneratorTest.ConfigGenerator, "TelaniAppSettingsAttribute.g.cs")]
    [InlineData(GeneratorTest.ConfigGenerator, "TelaniSettingsIgnoreAttribute.g.cs")]
    [InlineData(GeneratorTest.ConfigGenerator, "TelaniSettingsReadOnlyAttribute.g.cs")]
    [InlineData(GeneratorTest.DateSourceGenerator, "TelaniBuildDateAttribute.g.cs")]
    [InlineData(GeneratorTest.MiniAPIRouter, "TelaniRouteAttributes.g.cs")]
    [InlineData(GeneratorTest.StringValueGenerator, "Attributes.g.cs")]
    [InlineData(GeneratorTest.StringValueGenerator, "StringValueGeneratorAttribute.g.cs")]
    public void Every_generator_emits_its_marker_attributes(string generator, string hintName)
    {
        var run = GeneratorTest.Run("namespace Demo; public class Nothing { }", generator);

        run.AssertCompiles();
        Assert.True(run.Has(hintName), $"Expected a generated file named '{hintName}'.");
    }

    [Fact]
    public void All_four_generators_can_run_in_one_compilation()
    {
        // Each generator calls AddEmbeddedAttributeDefinition, which emits
        // Microsoft.CodeAnalysis.EmbeddedAttribute. That only works because Roslyn declares it
        // partial; this test is what would catch it if that ever stopped being true.
        var run = GeneratorTest.Run(
            [
                """
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

                    [TelaniRoute("GET", "/a")]
                    public partial class R : Route { }

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
                """
            ],
            [
                GeneratorTest.ConfigGenerator,
                GeneratorTest.DateSourceGenerator,
                GeneratorTest.MiniAPIRouter,
                GeneratorTest.StringValueGenerator,
            ]);

        run.AssertCompiles();
    }

    // --- known bugs --------------------------------------------------------------------------

    [Theory(Skip = "Audit finding 19: the generated attribute files declare 'using System.Globalization;' (which they do not use) but never 'using System;', so Attribute, AttributeUsage and AttributeTargets do not resolve. The package only works in a project with ImplicitUsings enabled. Fixing it means fully qualifying, e.g. global::System.Attribute.")]
    [InlineData(GeneratorTest.ConfigGenerator)]
    [InlineData(GeneratorTest.DateSourceGenerator)]
    [InlineData(GeneratorTest.MiniAPIRouter)]
    [InlineData(GeneratorTest.StringValueGenerator)]
    public void The_marker_attributes_compile_without_ImplicitUsings(string generator)
    {
        var run = GeneratorTest.Run("namespace Demo; public class Nothing { }", generator, implicitUsings: false);

        run.AssertCompiles();
    }
}
