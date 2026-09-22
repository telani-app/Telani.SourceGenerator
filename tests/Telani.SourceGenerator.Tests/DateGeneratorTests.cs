namespace Telani.SourceGenerator.Tests;

public class DateGeneratorTests
{
    private const string Gen = GeneratorTest.DateSourceGenerator;

    [Fact]
    public void Generates_a_declaration_and_an_implementation_that_compile()
    {
        var run = GeneratorTest.Run("""
            namespace Demo;

            [TelaniBuildDate]
            internal sealed partial class BuildDate { }
            """, Gen);

        run.AssertCompiles();
        Assert.Contains("internal static partial DateTime GetBuildDate();", run.Source("TelaniSourceGeneratorBuildDate.g.cs"));
        Assert.Contains("DateTime.ParseExact(", run.Source("TelaniSourceGeneratorBuildDate_Impl.g.cs"));
    }

    [Fact]
    public void The_marked_class_really_gains_a_callable_GetBuildDate()
    {
        // Asserting that the compilation succeeds is not enough on its own: the generator emits a
        // free-standing type from the namespace and the class name, so it can happily produce a
        // *different* type that compiles while the marked class gains nothing. Calling the method
        // through the marked class is what actually pins the behaviour down.
        var run = GeneratorTest.Run("""
            namespace Demo;

            [TelaniBuildDate]
            internal sealed partial class BuildDate { }

            internal static class Caller
            {
                public static System.DateTime Get() => BuildDate.GetBuildDate();
            }
            """, Gen);

        run.AssertCompiles();
    }

    [Fact]
    public void Emits_a_date_the_generated_code_can_round_trip()
    {
        var run = GeneratorTest.Run("""
            namespace Demo;

            [TelaniBuildDate]
            internal sealed partial class BuildDate { }
            """, Gen);

        run.AssertCompiles();

        var source = run.Source("TelaniSourceGeneratorBuildDate_Impl.g.cs");
        var start = source.IndexOf("ParseExact(\"", StringComparison.Ordinal) + "ParseExact(\"".Length;
        var date = source[start..source.IndexOf('"', start)];

        // The generated code parses with format "d" and the invariant culture, so the emitted
        // string has to be produced the same way or GetBuildDate() throws at run time.
        Assert.True(
            DateTime.TryParseExact(date, "d", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _),
            $"'{date}' is not parseable with the format the generated code uses.");
    }

    [Theory]
    [InlineData("internal sealed partial class")]
    [InlineData("public partial class")]
    [InlineData("internal static partial class")]
    [InlineData("public abstract partial class")]
    public void Reproduces_the_class_modifiers(string declaration)
    {
        var run = GeneratorTest.Run($$"""
            namespace Demo;

            [TelaniBuildDate]
            {{declaration}} BuildDate { }
            """, Gen);

        run.AssertCompiles();
        Assert.Contains($"{declaration} BuildDate", run.Source("TelaniSourceGeneratorBuildDate.g.cs"));
    }

    [Fact]
    public void Produces_nothing_when_no_class_is_marked()
    {
        var run = GeneratorTest.Run("namespace Demo; internal sealed partial class Plain { }", Gen);

        run.AssertCompiles();
        Assert.False(run.Has("TelaniSourceGeneratorBuildDate.g.cs"));
    }

    // --- known bugs --------------------------------------------------------------------------

    [Fact(Skip = "Audit finding 1: the hint names are constants but the output is registered per target rather than over Collect(), so a second marked class in the same compilation throws ArgumentException ('hintName must be unique') and BOTH classes lose GetBuildDate.")]
    public void Two_marked_classes_in_one_compilation_both_get_a_build_date()
    {
        var run = GeneratorTest.Run("""
            namespace First { [TelaniBuildDate] internal sealed partial class BuildA { } }
            namespace Second { [TelaniBuildDate] internal sealed partial class BuildB { } }
            """, Gen);

        Assert.Null(run.Exception);
        run.AssertCompiles();
    }

    [Fact(Skip = "Audit finding 17: only ContainingNamespace and Name are used, so the generated partial is emitted at namespace scope. That compiles - as a second, unrelated Demo.BuildDate type - while the nested class the attribute was put on never gets GetBuildDate.")]
    public void A_nested_marked_class_gains_the_method()
    {
        var run = GeneratorTest.Run("""
            namespace Demo;

            public partial class Outer
            {
                [TelaniBuildDate]
                internal sealed partial class BuildDate { }
            }

            internal static class Caller
            {
                public static System.DateTime Get() => Outer.BuildDate.GetBuildDate();
            }
            """, Gen);

        run.AssertCompiles();
    }

    [Fact(Skip = "Audit finding 18: className.Name drops the type parameter list, so the generated partial declares a separate non-generic Demo.BuildDate. That compiles, but BuildDate<T> never gains the method.")]
    public void A_generic_marked_class_gains_the_method()
    {
        var run = GeneratorTest.Run("""
            namespace Demo;

            [TelaniBuildDate]
            internal sealed partial class BuildDate<T> { }

            internal static class Caller
            {
                public static System.DateTime Get() => BuildDate<int>.GetBuildDate();
            }
            """, Gen);

        run.AssertCompiles();
    }

    [Fact(Skip = "Audit finding 16: ContainingNamespace with SymbolDisplayGlobalNamespaceStyle.Omitted yields an empty string for a top-level class, so the generator emits 'namespace ;' (CS1001).")]
    public void A_marked_class_in_the_global_namespace_compiles()
    {
        var run = GeneratorTest.Run("""
            [TelaniBuildDate]
            internal sealed partial class BuildDate { }
            """, Gen);

        run.AssertCompiles();
    }

    [Fact(Skip = "Audit finding, and a TODO already in DateGenerator.Initialize: a class that forgets 'partial' gets CS0260 pointing at the user's own declaration. The generator should report a diagnostic naming the attribute instead.")]
    public void A_non_partial_marked_class_reports_a_diagnostic()
    {
        var run = GeneratorTest.Run("""
            namespace Demo;

            [TelaniBuildDate]
            internal sealed class BuildDate { }
            """, Gen);

        Assert.NotEmpty(run.GeneratorDiagnostics);
    }
}
