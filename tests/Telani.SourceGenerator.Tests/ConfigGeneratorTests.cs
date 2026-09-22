namespace Telani.SourceGenerator.Tests;

public class ConfigGeneratorTests
{
    private const string Gen = GeneratorTest.ConfigGenerator;

    private const string Settings = """
        namespace Demo;

        [AppSettings]
        public sealed partial class AppSettings
        {
            /// <summary>The name of the thing.</summary>
            public string? Name { get; set; }

            [SettingsReadOnly]
            public int Version { get; set; } = 17;

            [SettingsIgnore]
            public bool Transient { get; set; }

            [SettingsIgnore]
            public object? _extraStuff { get; set; }
        }
        """;

    [Fact]
    public void Generates_an_interface_and_a_Reload_method_that_compile()
    {
        var run = GeneratorTest.Run(Settings, Gen);

        run.AssertCompiles();
        Assert.True(run.Has("TelaniSourceGeneratorIAppSettings.g.cs"));
        Assert.True(run.Has("TelaniSourceGeneratorAppSettings.g.cs"));
    }

    [Fact]
    public void Puts_writable_properties_on_the_interface()
    {
        var run = GeneratorTest.Run(Settings, Gen);

        run.AssertCompiles();
        Assert.Contains("public string? Name { get; set; }", run.Source("TelaniSourceGeneratorIAppSettings.g.cs"));
    }

    [Fact]
    public void Gives_a_SettingsReadOnly_property_no_setter()
    {
        var run = GeneratorTest.Run(Settings, Gen);

        run.AssertCompiles();
        Assert.Contains("public int Version { get; }", run.Source("TelaniSourceGeneratorIAppSettings.g.cs"));
    }

    [Fact]
    public void Leaves_a_SettingsIgnore_property_off_the_interface()
    {
        var run = GeneratorTest.Run(Settings, Gen);

        run.AssertCompiles();
        Assert.DoesNotContain("Transient", run.Source("TelaniSourceGeneratorIAppSettings.g.cs"));
    }

    [Fact]
    public void Copies_the_doc_comment_onto_the_interface_member()
    {
        var run = GeneratorTest.Run(Settings, Gen);

        run.AssertCompiles();
        Assert.Contains("<summary>The name of the thing.</summary>", run.Source("TelaniSourceGeneratorIAppSettings.g.cs"));
    }

    [Fact]
    public void Reload_assigns_writable_properties_and_skips_read_only_ones()
    {
        var run = GeneratorTest.Run(Settings, Gen);

        run.AssertCompiles();

        var source = run.Source("TelaniSourceGeneratorAppSettings.g.cs");
        Assert.Contains("Name = store.Name;", source);
        Assert.DoesNotContain("Version = store.Version;", source);
    }

    [Fact]
    public void Produces_nothing_when_no_class_is_marked()
    {
        var run = GeneratorTest.Run("namespace Demo; public class NotSettings { public int A { get; set; } }", Gen);

        run.AssertCompiles();
        Assert.False(run.Has("TelaniSourceGeneratorIAppSettings.g.cs"));
    }

    // --- known bugs --------------------------------------------------------------------------

    [Fact(Skip = "Audit finding 8: Reload always emits '_extraStuff = store._extraStuff;', a member name hard-coded in ProduceSource. A settings class without it fails with CS0103 and no explanation.")]
    public void A_settings_class_without__extraStuff_compiles()
    {
        var run = GeneratorTest.Run("""
            namespace Demo;

            [AppSettings]
            public sealed partial class AppSettings
            {
                public string? Name { get; set; }
            }
            """, Gen);

        run.AssertCompiles();
    }

    [Fact(Skip = "Audit finding 9: the attribute is matched by the literal name 'SettingsIgnore', so the long form [SettingsIgnoreAttribute] is ignored and the property leaks onto the interface.")]
    public void The_long_form_SettingsIgnoreAttribute_is_honoured()
    {
        var run = GeneratorTest.Run("""
            namespace Demo;

            [AppSettings]
            public sealed partial class AppSettings
            {
                public string? Name { get; set; }

                [SettingsIgnoreAttribute]
                public bool Transient { get; set; }

                public object? _extraStuff { get; set; }
            }
            """, Gen);

        run.AssertCompiles();
        Assert.DoesNotContain("Transient", run.Source("TelaniSourceGeneratorIAppSettings.g.cs"));
    }

    [Fact(Skip = "Audit finding 9: same name check, so [SettingsReadOnlyAttribute] is ignored and the property wrongly gets a setter on the interface.")]
    public void The_long_form_SettingsReadOnlyAttribute_is_honoured()
    {
        var run = GeneratorTest.Run("""
            namespace Demo;

            [AppSettings]
            public sealed partial class AppSettings
            {
                [SettingsReadOnlyAttribute]
                public int Version { get; set; }

                public object? _extraStuff { get; set; }
            }
            """, Gen);

        run.AssertCompiles();
        Assert.Contains("public int Version { get; }", run.Source("TelaniSourceGeneratorIAppSettings.g.cs"));
    }

    [Fact(Skip = "Audit finding 10: ExtractInfo only walks the members of the declaration carrying the attribute, so properties declared in the other files of a partial class are silently missing from the interface.")]
    public void Properties_from_every_part_of_a_partial_class_are_included()
    {
        var run = GeneratorTest.Run(
            [
                """
                namespace Demo;

                [AppSettings]
                public sealed partial class AppSettings
                {
                    public string? Name { get; set; }
                    public object? _extraStuff { get; set; }
                }
                """,
                """
                namespace Demo;

                public sealed partial class AppSettings
                {
                    public string? Second { get; set; }
                }
                """
            ],
            [Gen]);

        run.AssertCompiles();
        Assert.Contains("Second", run.Source("TelaniSourceGeneratorIAppSettings.g.cs"));
    }

    [Fact(Skip = "Audit finding 11: the property type is copied as raw syntax via dec.Type.GetText(), which loses the using directives and aliases of the declaring file. The symbol's fully qualified display string is what is wanted.")]
    public void A_type_that_needs_a_using_from_the_declaring_file_resolves()
    {
        var run = GeneratorTest.Run("""
            using System.Collections.Generic;
            using Stamp = System.DateTimeOffset;

            namespace Demo;

            [AppSettings]
            public sealed partial class AppSettings
            {
                public List<string>? Items { get; set; }
                public Stamp Created { get; set; }
                public object? _extraStuff { get; set; }
            }
            """, Gen);

        run.AssertCompiles();
    }

    [Fact(Skip = "Audit finding 12: every property is treated as an instance property with a setter, so a static, expression-bodied or init-only property produces an uncompilable Reload (CS0176, CS0200, CS8852).")]
    public void Properties_that_cannot_be_assigned_are_left_out_of_Reload()
    {
        var run = GeneratorTest.Run("""
            namespace Demo;

            [AppSettings]
            public sealed partial class AppSettings
            {
                public static string Shared { get; set; } = "";
                public string Computed => "x";
                public string Once { get; init; } = "";
                public object? _extraStuff { get; set; }
            }
            """, Gen);

        run.AssertCompiles();
    }

    [Fact(Skip = "Audit finding 7: ProduceSource takes the namespace from settingsClasses.First() and merges every marked class's properties into one AppSettings/IAppSettings pair, so a second marked class produces CS0103/CS1061.")]
    public void Two_marked_classes_get_their_own_interface()
    {
        var run = GeneratorTest.Run("""
            namespace First
            {
                [AppSettings]
                public sealed partial class AppSettings
                {
                    public string? A { get; set; }
                    public object? _extraStuff { get; set; }
                }
            }
            namespace Second
            {
                [AppSettings]
                public sealed partial class OtherSettings
                {
                    public string? B { get; set; }
                    public object? _extraStuff { get; set; }
                }
            }
            """, Gen);

        run.AssertCompiles();
    }

    [Fact(Skip = "Audit finding 16: ContainingNamespace with SymbolDisplayGlobalNamespaceStyle.Omitted yields an empty string for a top-level class, so the generator emits 'namespace ;' (CS1001).")]
    public void A_settings_class_in_the_global_namespace_compiles()
    {
        var run = GeneratorTest.Run("""
            [AppSettings]
            public sealed partial class AppSettings
            {
                public string? Name { get; set; }
                public object? _extraStuff { get; set; }
            }
            """, Gen);

        run.AssertCompiles();
    }
}
