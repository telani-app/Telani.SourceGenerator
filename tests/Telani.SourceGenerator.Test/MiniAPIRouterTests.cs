namespace Telani.SourceGenerator.Test;

[TestClass]
public sealed class MiniAPIRouterTests
{
    private const string Gen = GeneratorTest.MiniAPIRouter;

    /// <summary>Mirrors Telani.Data.Api.Route, the base class the generated members override.</summary>
    private const string RouteBaseClass = """
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
        }
        """;

    private static GeneratorRun RunWithBase(string routes) => GeneratorTest.Run([RouteBaseClass, routes], [Gen]);

    [TestMethod]
    public void Generates_Path_Method_and_RequestRegex_that_compile()
    {
        var run = RunWithBase("""
            namespace Demo
            {
                [TelaniRoute("GET", "/api/title/{id}")]
                public partial class GetTitle : Route { }
            }
            """);

        run.AssertCompiles();

        var source = run.Source("RouteExtensions.g.cs");
        Assert.Contains("public override string Path => \"/api/title/{id}\";", source);
        Assert.Contains("public override HttpMethod Method => HttpMethod.Get;", source);
        Assert.Contains("(?<id>[^/]+)", source);
    }

    [TestMethod]
    [DataRow("GET", "Get")]
    [DataRow("POST", "Post")]
    [DataRow("PUT", "Put")]
    [DataRow("DELETE", "Delete")]
    [DataRow("HEAD", "Head")]
    [DataRow("OPTIONS", "Options")]
    [DataRow("TRACE", "Trace")]
    public void Maps_every_supported_verb(string verb, string expected)
    {
        var run = RunWithBase($$"""
            namespace Demo
            {
                [TelaniRoute("{{verb}}", "/a")]
                public partial class R : Route { }
            }
            """);

        run.AssertCompiles();
        Assert.Contains($"HttpMethod.{expected};", run.Source("RouteExtensions.g.cs"));
    }

    [TestMethod]
    public void Turns_each_placeholder_into_a_named_capture_group()
    {
        var run = RunWithBase("""
            namespace Demo
            {
                [TelaniRoute("GET", "/api/zones/{zone}/elements/{id}")]
                public partial class R : Route { }
            }
            """);

        run.AssertCompiles();

        var source = run.Source("RouteExtensions.g.cs");
        Assert.Contains("(?<zone>[^/]+)", source);
        Assert.Contains("(?<id>[^/]+)", source);
    }

    [TestMethod]
    public void Routes_in_different_namespaces_compile()
    {
        var run = RunWithBase("""
            namespace Demo
            {
                [TelaniRoute("GET", "/a")]
                public partial class First : Route { }
            }
            namespace Other
            {
                [TelaniRoute("GET", "/b")]
                public partial class Second : Demo.Route { }
            }
            """);

        run.AssertCompiles();
    }

    [TestMethod]
    public void A_route_without_placeholders_anchors_the_pattern()
    {
        var run = RunWithBase("""
            namespace Demo
            {
                [TelaniRoute("GET", "/api/release-notes")]
                public partial class R : Route { }
            }
            """);

        run.AssertCompiles();
        Assert.Contains("^/api/release-notes/?$", run.Source("RouteExtensions.g.cs"));
    }

    // --- known bugs --------------------------------------------------------------------------

    [TestMethod]
    [Ignore("Audit finding 2: ParseMethod throws ArgumentException for anything it does not recognise. Because Execute runs over Collect(), that exception takes down RouteExtensions.g.cs for EVERY route in the compilation, not just the offending one. It should report a diagnostic instead.")]
    [DataRow("get")]
    [DataRow("Get")]
    [DataRow("PATCH")]
    [DataRow("")]
    public void An_unrecognised_verb_reports_a_diagnostic_instead_of_throwing(string verb)
    {
        var run = RunWithBase($$"""
            namespace Demo
            {
                [TelaniRoute("{{verb}}", "/a")]
                public partial class R : Route { }
            }
            """);

        Assert.IsNull(run.Exception);
        Assert.IsNotEmpty(run.GeneratorDiagnostics);
    }

    [TestMethod]
    [Ignore("Audit finding 2: one bad route silently removes the generated members from all the others. This is the blast-radius half of the same bug.")]
    public void A_bad_route_does_not_break_the_other_routes()
    {
        var run = RunWithBase("""
            namespace Demo
            {
                [TelaniRoute("PATCH", "/bad")]
                public partial class Bad : Route { }

                [TelaniRoute("GET", "/good")]
                public partial class Good : Route { }
            }
            """);

        Assert.Contains("\"/good\"", run.Source("RouteExtensions.g.cs"));
    }

    [TestMethod]
    [Ignore("Audit finding 2: arguments are read positionally with First()/Last(), so named arguments in the other order make the route string be parsed as the verb, which then throws.")]
    public void Named_arguments_are_resolved_by_parameter_name()
    {
        var run = RunWithBase("""
            namespace Demo
            {
                [TelaniRoute(RequestRoute: "/a", Method: "POST")]
                public partial class R : Route { }
            }
            """);

        run.AssertCompiles();
        Assert.Contains("HttpMethod.Post;", run.Source("RouteExtensions.g.cs"));
    }

    [TestMethod]
    [Ignore("Audit finding 13: the route is inserted into the regex without Regex.Escape, so '.' stays a wildcard and '/file.json' also matches '/fileXjson'.")]
    public void Regex_metacharacters_in_a_route_are_escaped()
    {
        var run = RunWithBase("""
            namespace Demo
            {
                [TelaniRoute("GET", "/file.json")]
                public partial class R : Route { }
            }
            """);

        run.AssertCompiles();
        Assert.Contains(@"/file\.json", run.Source("RouteExtensions.g.cs"));
    }

    [TestMethod]
    [Ignore("Audit finding 14: the generated regex is written as an interpolated string for no reason, so any brace the placeholder regex did not consume becomes a C# interpolation hole. '{id:int}' produces CS0103.")]
    public void A_placeholder_with_a_constraint_compiles()
    {
        var run = RunWithBase("""
            namespace Demo
            {
                [TelaniRoute("GET", "/api/items/{id:int}")]
                public partial class R : Route { }
            }
            """);

        run.AssertCompiles();
    }

    [TestMethod]
    [Ignore("Audit finding 15: Token.Text is the raw source text including the @ and the quotes, so a verbatim route literal is spliced into the generated file and produces a cascade of syntax errors. Token.ValueText is what is wanted.")]
    public void A_verbatim_route_literal_compiles()
    {
        var run = RunWithBase("""
            namespace Demo
            {
                [TelaniRoute("GET", @"/api/items/{id}")]
                public partial class R : Route { }
            }
            """);

        run.AssertCompiles();
    }

    [TestMethod]
    [Ignore("Audit finding 15: a const reference is not a LiteralExpressionSyntax, so the route comes out empty and emits 'public override string Path => ;'.")]
    public void A_const_route_argument_is_resolved()
    {
        var run = RunWithBase("""
            namespace Demo
            {
                public static class Routes { public const string Items = "/api/items"; }

                [TelaniRoute("GET", Routes.Items)]
                public partial class R : Route { }
            }
            """);

        run.AssertCompiles();
        Assert.Contains("\"/api/items\"", run.Source("RouteExtensions.g.cs"));
    }

    [TestMethod]
    [Ignore("Audit finding 16: GetDeclaredSymbol(i).ContainingNamespace.ToDisplayString() returns the literal '<global namespace>', which is emitted verbatim as a namespace declaration.")]
    public void A_route_in_the_global_namespace_compiles()
    {
        var run = GeneratorTest.Run(
            [
                """
                using System.Net.Http;
                using System.Text.RegularExpressions;

                public abstract class Route
                {
                    public abstract string Path { get; }
                    public abstract HttpMethod Method { get; }
                    internal abstract Regex RequestRegex { get; }
                }

                [TelaniRoute("GET", "/a")]
                public partial class R : Route { }
                """
            ],
            [Gen]);

        run.AssertCompiles();
    }

    [TestMethod]
    [Ignore("Audit finding 21: RequestRegex is an expression-bodied property, so every read constructs and parses a new Regex. It should cache in a static field, or use [GeneratedRegex].")]
    public void RequestRegex_does_not_allocate_on_every_read()
    {
        var run = RunWithBase("""
            namespace Demo
            {
                [TelaniRoute("GET", "/api/title/{id}")]
                public partial class R : Route { }
            }
            """);

        var source = run.Source("RouteExtensions.g.cs");
        Assert.DoesNotContain("RequestRegex => new Regex(", source);
    }
}
