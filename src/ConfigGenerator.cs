using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Telani.SourceGenerator;

/// <summary>
/// A generator to create source files related to settings in telani.
/// </summary>
[Generator(LanguageNames.CSharp)]
public class ConfigGenerator : IIncrementalGenerator
{
    private readonly record struct SettingsClassModel(EquatableArray<PropertyModel> Settings);

    private readonly record struct PropertyModel(string Type, string Name, bool ReadonlyAttr, string? Doccomment);

/*
    private static readonly DiagnosticDescriptor UnspecificError = new(
#pragma warning disable RS2008 // Enable analyzer release tracking
            "Telani001",
#pragma warning restore RS2008 // Enable analyzer release tracking
            "ConfigGenerator failed",
        "An error occurred during ConfigGenerator run: \"{0}\"",
        "Functionality",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
*/

    private static void ProduceSource(SourceProductionContext productionContext, ImmutableArray<SettingsClassModel> settingsClasses)
    {
        if (!settingsClasses.Any())
        {
            return;
        }

        var properties = settingsClasses.SelectMany(static a => a.Settings);

        var sourceBuilder = new StringBuilder();
        PrintHead(sourceBuilder, "Telani.Data", printConfigUsings: true);

        sourceBuilder.Append(@"
public partial interface IAppSettings
{
");
        PrintProperties(sourceBuilder, properties);
        sourceBuilder.Append(@"
}");


        var sourceBuilder2 = new StringBuilder();
        PrintHead(sourceBuilder2, "Telani.Data.Settings", printConfigUsings: true);

        sourceBuilder2.Append(@"
public sealed partial class AppSettings
{");

        sourceBuilder2.Append(@"
    private void Reload(AppSettings store)
    {
");

        foreach (var (_, name, readOnlyAttr, _) in properties)
        {
            if (!readOnlyAttr)
            {
                sourceBuilder2.AppendLine(string.Format(CultureInfo.InvariantCulture, "        {0} = store.{0};", name));
            }
        }

        sourceBuilder2.Append(@"        _extraStuff = store._extraStuff;
    }
}");
        productionContext.AddSource("TelaniSourceGeneratorAppSettingsIncremental.g.cs",
            SourceText.From(sourceBuilder2.ToString(), Encoding.UTF8));
        productionContext.AddSource("TelaniSourceGeneratorIAppSettingsIncremental.g.cs",
            SourceText.From(sourceBuilder.ToString(), Encoding.UTF8));
    }

    private static PropertyModel? ParseProperty(PropertyDeclarationSyntax dec)
    {
        var doccomment = "";
        var trivias = dec.GetLeadingTrivia();
        var xmlCommentTrivia = trivias.Where(static t => t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) 
        || t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia)).ToList();

        if (xmlCommentTrivia.Count != 0)
        {
            var first = xmlCommentTrivia.FirstOrDefault();
            var xml = first.GetStructure() as DocumentationCommentTriviaSyntax;
            if (xml is not null)
            {
                doccomment = xml.Content.ToFullString();
            }
        }

        var att = dec.AttributeLists.SelectMany(static a => a.Attributes).Cast<AttributeSyntax>();
        if (!att.Any(static a => a.Name.ToString() == "SettingsIgnore"))
        {
            bool readonlyAttr = false;
            var atty = att.FirstOrDefault(static a => a.Name.ToString() == "SettingsReadOnly");
            if (atty is not null)
            {
                readonlyAttr = true;
            }
            return new(dec.Type.WithoutTrivia().GetText().ToString(), dec.Identifier.Text, readonlyAttr, doccomment);
        }
        return null;
    }

    private static void PrintHead(StringBuilder sourceBuilder, string theNamespace, bool printConfigUsings)
    {
        sourceBuilder.Append($@"using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Telani.Data;
{(printConfigUsings ? "using Telani.Data.Model;" : "")}

#nullable enable
                
namespace {theNamespace};
                ");
    }

    private static void PrintProperties(StringBuilder sourceBuilder, IEnumerable<PropertyModel> properties)
    {
        foreach (var (type, name, readonlyAttr, doccomment) in properties)
        {
            var isreadonly = readonlyAttr ? "" : "set; ";
            if (!string.IsNullOrEmpty(doccomment))
            {
                sourceBuilder.Append("    " + doccomment);
            }
            sourceBuilder.AppendLine(string.Format(CultureInfo.InvariantCulture, "    public {0} {1} {{ get; {2}}}", type, name, isreadonly));
        }
    }

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        /*if (!Debugger.IsAttached)
        {
            Debugger.Launch();
            Debugger.Break();
        }*/
        var appSettingsClass = context.SyntaxProvider.ForAttributeWithMetadataName(
            "Telani.Data.AppSettingsAttribute",
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (syntaxContext, _) => ExtractInfo((ClassDeclarationSyntax)syntaxContext.TargetNode));


        context.RegisterSourceOutput(appSettingsClass.Collect(), ProduceSource);

        context.RegisterPostInitializationOutput(static c =>
        {
            c.AddSource("TelaniSourceGeneratorAttributes.g.cs", WriteAttributesFile());
        });
    }

    private static SettingsClassModel ExtractInfo(ClassDeclarationSyntax s)
    {
        if (s is null)
        {
            return new(new EquatableArray<PropertyModel>());
        }
        var tempList = new List<PropertyModel>();

        foreach (var member in s.Members)
        {
            if (member is PropertyDeclarationSyntax propertySyntax)
            {
                var info = ParseProperty(propertySyntax);
                if (info is not null)
                {
                    tempList.Add(info.Value);
                }
            }
        }
        return new(tempList.ToImmutableArray());
    }

    private static SourceText WriteAttributesFile()
    {
        var sourceBuilder3 = new StringBuilder();
        PrintHead(sourceBuilder3, "Telani.Data", printConfigUsings: false);

        sourceBuilder3.Append(@"
    [AttributeUsage(AttributeTargets.Property)]
    internal sealed class SettingsIgnoreAttribute : Attribute{}");

        sourceBuilder3.Append(@"

    [AttributeUsage(AttributeTargets.Property)]
    internal sealed class SettingsReadOnlyAttribute : Attribute{}");

        sourceBuilder3.Append(@"

    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class AppSettingsAttribute : Attribute{}");

        return SourceText.From(sourceBuilder3.ToString(), Encoding.UTF8);
    }
}
