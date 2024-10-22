using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Telani.SourceGenerator;

[Generator(LanguageNames.CSharp)]
public class MiniAPIRouter : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var routes = context.SyntaxProvider.ForAttributeWithMetadataName("Telani.SourceGenerator.RouteAttribute",
                    predicate: static (node, _) => IsSyntaxTargetForGenerationQuick(node),
                    transform: static (syntaxContext, _) => ((ClassDeclarationSyntax)syntaxContext.TargetNode, syntaxContext.SemanticModel))
                    .Select(static (a, _) => PrepareRoute(a.Item1, a.SemanticModel));

        context.RegisterSourceOutput(routes.Collect(), Execute);

        context.RegisterPostInitializationOutput(static c =>
        {
            c.AddSource("RouterAttributes.g.cs", ReadAttributesFile());
        });
    }

    private readonly record struct RouteEntry(string Name, string ClassNamespace, string RequestRoute, HttpMethod Method, string RequestRegex);

    private static RouteEntry PrepareRoute(ClassDeclarationSyntax i, SemanticModel semModel)
    {
        /*if (!Debugger.IsAttached)
        {
            Debugger.Launch();
            Debugger.Break();
        }*/
        var className = i.Identifier.ValueText;
        var classNamespace = semModel.GetDeclaredSymbol(i)?.ContainingNamespace?.ToDisplayString() ?? "";
        string requestRoute = string.Empty;
        string requestRegex = string.Empty;
        HttpMethod requestMethod = HttpMethod.Get;

        foreach (var attributesOnClass in i.AttributeLists)
        {
            foreach (var att in attributesOnClass.Attributes)
            {
                var name = (att.Name as SimpleNameSyntax)?.Identifier.ValueText;
                if (name == "Route")
                {
                    var args = att.ArgumentList?.Arguments.FirstOrDefault()?.Expression as LiteralExpressionSyntax;
                    if (args is not null)
                    {
                        requestMethod = ParseMethod(args.Token.Text.Trim('"'));
                    }
                    var arg2 = att.ArgumentList?.Arguments.Last()?.Expression as LiteralExpressionSyntax;
                    if (arg2 is not null)
                    {
                        requestRoute = arg2.Token.Text;

                        var regex = new Regex(@"{(\w+)}");
                        requestRegex = regex.Replace(arg2.Token.Text, @"(?<$1>[^/]+)");                        
                    }
                }
            }
        }

        return new RouteEntry(className, classNamespace, requestRoute, requestMethod, requestRegex);
    }

    // In newer dotnet this is built-in.
    private static HttpMethod ParseMethod(string text) => text switch
    {
        "PUT" => HttpMethod.Put,
        "DELETE" => HttpMethod.Delete,
        "GET" => HttpMethod.Get,
        "POST" => HttpMethod.Post,
        "TRACE" => HttpMethod.Trace,
        "OPTIONS" => HttpMethod.Options,
        "HEAD" => HttpMethod.Head,
        _ => throw new ArgumentException("Invalid method type")
    };

    public static string ToTitleCase(string title) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(title.ToLowerInvariant());

    private static void Execute(SourceProductionContext context, ImmutableArray<RouteEntry> inputs)
    {
        if (!inputs.Any())
        {
            return;
        }
        var extension = new StringBuilder();
        extension.AppendLine("#nullable enable");
        extension.AppendLine("");
        extension.AppendLine("using System.Net.Http;");
        extension.AppendLine("using System.Text.RegularExpressions;");
        extension.AppendLine("");

        foreach (var input in inputs)
        {
            extension.AppendLine($"namespace {input.ClassNamespace}");
            extension.AppendLine("{");
            extension.AppendLine($"    /// <summary>");
            extension.AppendLine($"    /// </summary>");
            extension.AppendLine($"    public partial class {input.Name}");
            extension.AppendLine("    {");
            extension.AppendLine($"         internal string RoutePath => {input.RequestRoute};");
            extension.AppendLine($"         internal HttpMethod Method => HttpMethod.{ToTitleCase(input.Method.Method)};");
            extension.AppendLine($"         internal Regex RequestRegex => new Regex($\"^{input.RequestRegex.Trim('"')}/?$\", RegexOptions.IgnoreCase);");
            extension.AppendLine("    }");
            extension.AppendLine("}");
        }
        context.AddSource($"RouteExtensions.g.cs", extension.ToString());
    }

    private static bool IsSyntaxTargetForGenerationQuick(SyntaxNode node)
    {
        if (node is not ClassDeclarationSyntax)
        {
            return false;
        }
        return true;
    }

    private static string ReadAttributesFile()
    {
        return @"
using System.Globalization;

#nullable enable

namespace Telani.SourceGenerator;
/// <summary>
/// This class implements a route for a Rest-API.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
internal sealed class RouteAttribute(string Method, string RequestRoute) : Attribute
{

}
";
    }
}
