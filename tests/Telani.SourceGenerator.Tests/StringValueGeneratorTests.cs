namespace Telani.SourceGenerator.Tests;

public class StringValueGeneratorTests
{
    private const string Gen = GeneratorTest.StringValueGenerator;

    private const string TwoMemberEnum = """
        namespace Demo;

        [StringValueGenerator]
        public enum Planet
        {
            [StringValue("Merkur")] Mercury,
            [StringValue("Venus")] Venus,
        }
        """;

    [Fact]
    public void Generates_a_GetStringValue_extension_that_compiles()
    {
        var run = GeneratorTest.Run(TwoMemberEnum, Gen);

        run.AssertCompiles();

        var source = run.Source("EnumExtensions.g.cs");
        Assert.Contains("public static string GetStringValue(this Planet @this)", source);
        Assert.Contains("Planet.Mercury => \"Merkur\",", source);
        Assert.Contains("Planet.Venus => \"Venus\",", source);
    }

    [Fact]
    public void Generates_a_FromString_method_that_compiles()
    {
        var run = GeneratorTest.Run(TwoMemberEnum, Gen);

        run.AssertCompiles();

        var source = run.Source("EnumExtensions.g.cs");
        Assert.Contains("public static Planet PlanetFromString(string? value)", source);
        Assert.Contains("\"Merkur\" => Planet.Mercury,", source);
    }

    [Fact]
    public void Generates_an_EnumToString_dispatcher_per_namespace()
    {
        var run = GeneratorTest.Run("""
            namespace Demo;

            [StringValueGenerator] public enum First { [StringValue("a")] A }
            [StringValueGenerator] public enum Second { [StringValue("b")] B }
            """, Gen);

        run.AssertCompiles();

        var source = run.Source("EnumToString.g.cs");
        Assert.Equal(1, CountOccurrences(source, "namespace Demo"));
        Assert.Contains("First First => First.GetStringValue(),", source);
        Assert.Contains("Second Second => Second.GetStringValue(),", source);
    }

    [Fact]
    public void Two_enums_in_the_same_namespace_compile()
    {
        var run = GeneratorTest.Run("""
            namespace Demo;

            [StringValueGenerator] public enum First { [StringValue("a")] A }
            [StringValueGenerator] public enum Second { [StringValue("b")] B }
            """, Gen);

        run.AssertCompiles();
    }

    // --- determinism (audit finding 6) -------------------------------------------------------

    [Fact]
    public void Switch_arms_follow_declaration_order()
    {
        // Regression test. The members used to live in an ImmutableDictionary, whose enumeration
        // order follows the randomized string hash codes, so this order changed on every build.
        var members = Enumerable.Range(0, 12).Select(i => $"    [StringValue(\"s{i}\")] M{i},");
        var run = GeneratorTest.Run($$"""
            namespace Demo;

            [StringValueGenerator]
            public enum Big
            {
            {{string.Join("\n", members)}}
            }
            """, Gen);

        run.AssertCompiles();

        var source = run.Source("EnumExtensions.g.cs");
        var order = source.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("Big.M", StringComparison.Ordinal))
            .Select(l => l["Big.".Length..].Split(' ')[0])
            .ToArray();

        Assert.Equal(Enumerable.Range(0, 12).Select(i => $"M{i}"), order);
    }

    [Fact]
    public void FromString_falls_back_to_the_first_declared_member()
    {
        var run = GeneratorTest.Run("""
            namespace Demo;

            [StringValueGenerator]
            public enum Planet
            {
                [StringValue("Merkur")] Mercury,
                [StringValue("Venus")] Venus,
                [StringValue("Erde")] Earth,
            }
            """, Gen);

        run.AssertCompiles();
        Assert.Contains("_ => Planet.Mercury", run.Source("EnumExtensions.g.cs"));
    }

    [Fact]
    public void Generated_source_is_identical_for_identical_input()
    {
        // Within one process this cannot catch the hash-order problem that finding 6 describes,
        // since the hash seed is fixed per process. It does guard against anything else that
        // makes the output depend on run order, and it is free.
        var first = GeneratorTest.Run(TwoMemberEnum, Gen).AllSources;
        var second = GeneratorTest.Run(TwoMemberEnum, Gen).AllSources;

        Assert.Equal(first, second);
    }

    // --- known bugs --------------------------------------------------------------------------

    [Fact(Skip = "Audit finding 3: e.Values.First() throws InvalidOperationException on an enum with no members, which Roslyn surfaces as CS8785 and which takes down the generator for every other enum in the compilation.")]
    public void An_empty_enum_does_not_crash_the_generator()
    {
        var run = GeneratorTest.Run("""
            namespace Demo;

            [StringValueGenerator]
            public enum Empty { }
            """, Gen);

        Assert.Null(run.Exception);
    }

    [Fact(Skip = "Audit finding 4: PrepareEnum matches the attribute with (att.Name as SimpleNameSyntax) and compares against the literal 'StringValue', so a fully qualified usage is silently skipped and the arm is emitted as 'Planet.Mercury => ,'.")]
    public void A_fully_qualified_StringValue_attribute_is_recognised()
    {
        var run = GeneratorTest.Run("""
            namespace Demo;

            [Telani.SourceGenerator.StringValueGenerator]
            public enum Planet
            {
                [Telani.SourceGenerator.StringValue("Merkur")] Mercury,
            }
            """, Gen);

        run.AssertCompiles();
        Assert.Contains("Planet.Mercury => \"Merkur\",", run.Source("EnumExtensions.g.cs"));
    }

    [Fact(Skip = "Audit finding 4: the same name check rejects the long form [StringValueAttribute(...)], which C# treats as identical to [StringValue(...)].")]
    public void The_long_form_StringValueAttribute_is_recognised()
    {
        var run = GeneratorTest.Run("""
            namespace Demo;

            [StringValueGenerator]
            public enum Planet
            {
                [StringValueAttribute("Merkur")] Mercury,
            }
            """, Gen);

        run.AssertCompiles();
        Assert.Contains("Planet.Mercury => \"Merkur\",", run.Source("EnumExtensions.g.cs"));
    }

    [Fact(Skip = "Audit finding 4: a member without [StringValue] emits 'Planet.Venus => ,' instead of a value or a diagnostic. The generator should report a diagnostic naming the member.")]
    public void A_member_without_a_StringValue_reports_a_diagnostic()
    {
        var run = GeneratorTest.Run("""
            namespace Demo;

            [StringValueGenerator]
            public enum Planet
            {
                [StringValue("Merkur")] Mercury,
                Venus,
            }
            """, Gen);

        Assert.NotEmpty(run.GeneratorDiagnostics);
        Assert.True(run.CompileErrors.IsEmpty, "The generated code must stay compilable even when a member is unannotated.");
    }

    [Fact(Skip = "Audit finding 4: the argument is read as a LiteralExpressionSyntax, so a const reference yields an empty string and an uncompilable arm.")]
    public void A_const_string_argument_is_resolved()
    {
        var run = GeneratorTest.Run("""
            namespace Demo;

            public static class Names { public const string Mercury = "Merkur"; }

            [StringValueGenerator]
            public enum Planet
            {
                [StringValue(Names.Mercury)] Mercury,
            }
            """, Gen);

        run.AssertCompiles();
        Assert.Contains("Planet.Mercury => \"Merkur\",", run.Source("EnumExtensions.g.cs"));
    }

    [Fact(Skip = "Audit finding 17: only the enum's own identifier is used, so a nested enum is emitted as 'this Inner @this' at namespace scope and does not resolve (CS0246).")]
    public void A_nested_enum_is_qualified_with_its_containing_type()
    {
        var run = GeneratorTest.Run("""
            namespace Demo;

            public class Outer
            {
                [StringValueGenerator]
                public enum Inner { [StringValue("a")] A }
            }
            """, Gen);

        run.AssertCompiles();
    }

    [Fact(Skip = "Audit finding 16: ContainingNamespace.ToDisplayString() returns the literal '<global namespace>' for a top-level enum, which is emitted verbatim as 'namespace <global namespace>'.")]
    public void An_enum_in_the_global_namespace_compiles()
    {
        var run = GeneratorTest.Run("""
            [StringValueGenerator]
            public enum Planet { [StringValue("Merkur")] Mercury }
            """, Gen);

        run.AssertCompiles();
    }

    [Fact(Skip = "Audit finding 4 follow-on: two members sharing a string value produce duplicate case labels in XFromString (CS8510). The generator should report a diagnostic instead.")]
    public void Duplicate_string_values_report_a_diagnostic()
    {
        var run = GeneratorTest.Run("""
            namespace Demo;

            [StringValueGenerator]
            public enum Planet
            {
                [StringValue("Same")] A,
                [StringValue("Same")] B,
            }
            """, Gen);

        Assert.NotEmpty(run.GeneratorDiagnostics);
        run.AssertCompiles();
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }
}
