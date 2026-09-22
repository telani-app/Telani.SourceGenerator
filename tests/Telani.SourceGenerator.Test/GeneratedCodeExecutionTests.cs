namespace Telani.SourceGenerator.Test;

/// <summary>
/// These compile the generated code, load it and call it. The rest of the suite asserts on the
/// generated text, which says the generator wrote what we expected; these say the result behaves.
/// Several audit findings are behavioural - an unescaped '.' in a route regex produces text that
/// looks fine and matches the wrong paths - so those are only really pinned down from here.
///
/// Each test source exposes a public static Probe, so the assertions reflect over one entry point
/// and compare primitives rather than poking at internal members from outside the assembly.
/// </summary>
[TestClass]
public sealed class GeneratedCodeExecutionTests
{
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

    // --- StringValueGenerator ----------------------------------------------------------------

    private const string PlanetEnum = """
        namespace Demo;

        [StringValueGenerator]
        public enum Planet
        {
            [StringValue("Merkur")] Mercury,
            [StringValue("Venus")] Venus,
            [StringValue("Erde")] Earth,
        }

        public static class Probe
        {
            public static string Get(int member) => ((Planet)member).GetStringValue();
            public static int FromString(string value) => (int)EnumExtensions.PlanetFromString(value);
            public static string Dispatch(int member) => EnumToStringGenerator.EnumToString((Planet)member);
        }
        """;

    [TestMethod]
    public void GetStringValue_returns_the_annotated_string()
    {
        using var generated = GeneratedAssembly.Load(PlanetEnum, GeneratorTest.StringValueGenerator);

        Assert.AreEqual("Merkur", generated.Call<string>("Demo.Probe", "Get", 0));
        Assert.AreEqual("Venus", generated.Call<string>("Demo.Probe", "Get", 1));
        Assert.AreEqual("Erde", generated.Call<string>("Demo.Probe", "Get", 2));
    }

    [TestMethod]
    public void FromString_round_trips_every_member()
    {
        using var generated = GeneratedAssembly.Load(PlanetEnum, GeneratorTest.StringValueGenerator);

        for (var member = 0; member <= 2; member++)
        {
            var text = generated.Call<string>("Demo.Probe", "Get", member);
            Assert.AreEqual(member, generated.Call<int>("Demo.Probe", "FromString", text), $"'{text}' did not round trip");
        }
    }

    [TestMethod]
    public void FromString_returns_the_first_declared_member_for_an_unknown_string()
    {
        // The text-level test asserts the generated fallback arm reads '_ => Planet.Mercury'.
        // This one asserts the method actually returns it, and is what would catch the old
        // ImmutableDictionary ordering returning an arbitrary member instead.
        using var generated = GeneratedAssembly.Load(PlanetEnum, GeneratorTest.StringValueGenerator);

        Assert.AreEqual(0, generated.Call<int>("Demo.Probe", "FromString", "not a planet"));
    }

    [TestMethod]
    public void GetStringValue_is_not_affected_by_the_current_culture()
    {
        // AssemblySetup runs the whole suite under de-DE, so this passing means the generated
        // switch is plain string literals and carries no culture-sensitive formatting.
        using var generated = GeneratedAssembly.Load(PlanetEnum, GeneratorTest.StringValueGenerator);

        Assert.AreEqual("Erde", generated.Call<string>("Demo.Probe", "Get", 2));
    }

    [TestMethod]
    public void EnumToString_dispatches_to_GetStringValue()
    {
        using var generated = GeneratedAssembly.Load(PlanetEnum, GeneratorTest.StringValueGenerator);

        Assert.AreEqual("Erde", generated.Call<string>("Demo.Probe", "Dispatch", 2));
    }

    // --- MiniAPIRouter -----------------------------------------------------------------------

    private static string RouteProbe(string verb, string route) => $$"""
        {{RouteBaseClass}}

        namespace Demo
        {
            [TelaniRoute("{{verb}}", "{{route}}")]
            public partial class GetTitle : Route { }

            public static class Probe
            {
                public static bool Matches(string path) => new GetTitle().RequestRegex.IsMatch(path);
                public static string Capture(string path, string group)
                    => new GetTitle().RequestRegex.Match(path).Groups[group].Value;
                public static string Path() => new GetTitle().Path;
                public static string Method() => new GetTitle().Method.Method;
            }
        }
        """;

    [TestMethod]
    public void Path_and_Method_expose_the_values_from_the_attribute()
    {
        using var generated = GeneratedAssembly.Load(RouteProbe("GET", "/api/title/{id}"), GeneratorTest.MiniAPIRouter);

        Assert.AreEqual("/api/title/{id}", generated.Call<string>("Demo.Probe", "Path"));
        Assert.AreEqual("GET", generated.Call<string>("Demo.Probe", "Method"));
    }

    [TestMethod]
    public void RequestRegex_matches_a_concrete_path_and_captures_the_placeholder()
    {
        using var generated = GeneratedAssembly.Load(RouteProbe("GET", "/api/title/{id}"), GeneratorTest.MiniAPIRouter);

        Assert.IsTrue(generated.Call<bool>("Demo.Probe", "Matches", "/api/title/123"));
        Assert.AreEqual("123", generated.Call<string>("Demo.Probe", "Capture", "/api/title/123", "id"));
    }

    [TestMethod]
    public void RequestRegex_tolerates_a_trailing_slash()
    {
        using var generated = GeneratedAssembly.Load(RouteProbe("GET", "/api/title/{id}"), GeneratorTest.MiniAPIRouter);

        Assert.IsTrue(generated.Call<bool>("Demo.Probe", "Matches", "/api/title/123/"));
    }

    [TestMethod]
    public void RequestRegex_is_anchored_at_both_ends()
    {
        using var generated = GeneratedAssembly.Load(RouteProbe("GET", "/api/title/{id}"), GeneratorTest.MiniAPIRouter);

        Assert.IsFalse(generated.Call<bool>("Demo.Probe", "Matches", "/prefix/api/title/123"));
        Assert.IsFalse(generated.Call<bool>("Demo.Probe", "Matches", "/api/title/123/extra"));
    }

    [TestMethod]
    public void RequestRegex_does_not_let_a_placeholder_swallow_a_segment_separator()
    {
        using var generated = GeneratedAssembly.Load(RouteProbe("GET", "/api/title/{id}"), GeneratorTest.MiniAPIRouter);

        Assert.IsFalse(generated.Call<bool>("Demo.Probe", "Matches", "/api/title/1/2"));
    }

    [TestMethod]
    public void RequestRegex_ignores_case()
    {
        using var generated = GeneratedAssembly.Load(RouteProbe("GET", "/api/title/{id}"), GeneratorTest.MiniAPIRouter);

        Assert.IsTrue(generated.Call<bool>("Demo.Probe", "Matches", "/API/Title/123"));
    }

    // --- DateSourceGenerator -----------------------------------------------------------------

    [TestMethod]
    public void GetBuildDate_returns_todays_date()
    {
        using var generated = GeneratedAssembly.Load("""
            namespace Demo;

            [TelaniBuildDate]
            internal sealed partial class BuildDate { }

            public static class Probe
            {
                public static System.DateTime Get() => BuildDate.GetBuildDate();
            }
            """, GeneratorTest.DateSourceGenerator);

        // The generator stamps DateTime.UtcNow, so allow the previous day as well rather than
        // letting the test flake for one run a day when it straddles UTC midnight.
        var actual = generated.Call<DateTime>("Demo.Probe", "Get").Date;
        var today = DateTime.UtcNow.Date;

        Assert.IsTrue(
            actual == today || actual == today.AddDays(-1),
            $"Expected {today:yyyy-MM-dd} (or the day before), got {actual:yyyy-MM-dd}.");
    }

    [TestMethod]
    public void GetBuildDate_does_not_throw_under_a_non_invariant_culture()
    {
        // AssemblySetup puts the process in de-DE. The generated code calls DateTime.ParseExact
        // with format "d" and CultureInfo.InvariantCulture, so it has to agree with the invariant
        // format the generator emitted. If either side ever switched to the current culture, the
        // German d/M/yyyy order would make this throw a FormatException.
        using var generated = GeneratedAssembly.Load("""
            namespace Demo;

            [TelaniBuildDate]
            internal sealed partial class BuildDate { }

            public static class Probe
            {
                public static System.DateTime Get() => BuildDate.GetBuildDate();
            }
            """, GeneratorTest.DateSourceGenerator);

        // Calling it at all is the test: if the emitted string and the parse format ever
        // disagree, ParseExact throws FormatException inside the generated method.
        Assert.AreNotEqual(default, generated.Call<DateTime>("Demo.Probe", "Get"));
    }

    // --- known bugs --------------------------------------------------------------------------

    [TestMethod]
    [Ignore("Audit finding 13: the route is inserted into the regex without Regex.Escape, so the '.' in '/file.json' stays a wildcard and the route also matches '/fileXjson'. This is the behavioural half of MiniAPIRouterTests.Regex_metacharacters_in_a_route_are_escaped - the generated text looks perfectly reasonable, and only running it shows the route matching a path it must not.")]
    public void RequestRegex_treats_a_dot_in_the_route_as_a_literal()
    {
        using var generated = GeneratedAssembly.Load(RouteProbe("GET", "/file.json"), GeneratorTest.MiniAPIRouter);

        Assert.IsTrue(generated.Call<bool>("Demo.Probe", "Matches", "/file.json"));
        Assert.IsFalse(generated.Call<bool>("Demo.Probe", "Matches", "/fileXjson"));
    }

    [TestMethod]
    [Ignore("Audit finding 4: a member with no [StringValue] emits 'Planet.Venus => ,', so the enum does not compile at all and GetStringValue cannot be called. Once the generator emits a sensible default or a diagnostic, this pins down what an unannotated member returns.")]
    public void GetStringValue_returns_an_empty_string_for_an_unannotated_member()
    {
        using var generated = GeneratedAssembly.Load("""
            namespace Demo;

            [StringValueGenerator]
            public enum Planet
            {
                [StringValue("Merkur")] Mercury,
                Venus,
            }

            public static class Probe
            {
                public static string Get(int member) => ((Planet)member).GetStringValue();
            }
            """, GeneratorTest.StringValueGenerator);

        Assert.AreEqual("Merkur", generated.Call<string>("Demo.Probe", "Get", 0));
        Assert.AreEqual("", generated.Call<string>("Demo.Probe", "Get", 1));
    }
}
