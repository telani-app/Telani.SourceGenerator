using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Telani.SourceGenerator.Test;

/// <summary>
/// A compilation that has been run through the generators, emitted and loaded, so a test can call
/// the generated code instead of only reading it. Asserting on the generated text says the
/// generator wrote what we expected; calling it says the result behaves.
/// </summary>
internal sealed class GeneratedAssembly : IDisposable
{
    private readonly AssemblyLoadContext _context;

    private GeneratedAssembly(AssemblyLoadContext context, Assembly assembly)
    {
        _context = context;
        Assembly = assembly;
    }

    public Assembly Assembly { get; }

    /// <summary>
    /// Runs the generators over <paramref name="source"/>, emits the result and loads it.
    /// </summary>
    public static GeneratedAssembly Load(string source, params string[] generators)
    {
        // Unlike the compile-only tests, which pin Basic.Reference.Assemblies so the result does
        // not depend on the machine, code that is going to be executed has to be compiled against
        // the framework it will run on. These are the reference assemblies of the test host.
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));

        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var trees = new[] { "global using System;\nglobal using Telani.SourceGenerator;", source }
            .Select((s, i) => CSharpSyntaxTree.ParseText(s, parseOptions, path: $"Input{i}.cs"));

        // A fresh name per load, so two tests never race over one assembly identity.
        var compilation = CSharpCompilation.Create(
            "Executable_" + Guid.NewGuid().ToString("N"),
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators.Select(g => GeneratorTest.Create(g).AsSourceGenerator()),
            parseOptions: parseOptions,
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None));

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var withGenerated, out _);

        using var peStream = new MemoryStream();
        var result = withGenerated.Emit(peStream);

        Assert.IsTrue(
            result.Success,
            "The generated code did not compile:\n  "
            + string.Join("\n  ", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
            + "\n\n--- generated ---\n"
            + string.Join("\n", withGenerated.SyntaxTrees.Skip(compilation.SyntaxTrees.Length)));

        peStream.Position = 0;

        var context = new AssemblyLoadContext("GeneratedAssembly", isCollectible: true);
        return new GeneratedAssembly(context, context.LoadFromStream(peStream));
    }

    /// <summary>Calls a public static method and returns what it produced.</summary>
    public object? Call(string typeName, string methodName, params object?[] arguments)
    {
        var type = Assembly.GetType(typeName)
            ?? throw new InvalidOperationException($"No type '{typeName}' in the generated assembly.");
        var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"No public static method '{methodName}' on '{typeName}'.");

        try
        {
            return method.Invoke(null, arguments);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            // Surface what the generated code actually threw, not the reflection wrapper.
            throw ex.InnerException;
        }
    }

    /// <summary>Calls a public static method that returns <typeparamref name="T"/>.</summary>
    public T Call<T>(string typeName, string methodName, params object?[] arguments)
    {
        var value = Call(typeName, methodName, arguments);
        Assert.IsInstanceOfType<T>(value, $"{typeName}.{methodName} returned {value?.GetType().Name ?? "null"}.");
        return (T)value!;
    }

    public void Dispose() => _context.Unload();
}
