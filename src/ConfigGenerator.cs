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
    private readonly record struct SettingsClassModel(EquatableArray<PropertyModel> Settings, string NamespaceName);

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

        var sourceBuilder = new SourceWriter();
        PrintHead(sourceBuilder, settingsClasses.First().NamespaceName);

        sourceBuilder.WriteLine("public partial interface IAppSettings");
        sourceBuilder.WriteStartBlock();

        PrintProperties(sourceBuilder, properties);
        
        sourceBuilder.WriteEndBlock();


        var sourceBuilder2 = new SourceWriter();
        PrintHead(sourceBuilder2, settingsClasses.First().NamespaceName);

        sourceBuilder2.WriteLine("public sealed partial class AppSettings");
        sourceBuilder2.WriteStartBlock();

        sourceBuilder2.WriteLine("private void Reload(AppSettings store)");
        sourceBuilder2.WriteStartBlock();

        foreach (var (_, name, readOnlyAttr, _) in properties)
        {
            if (!readOnlyAttr)
            {
                sourceBuilder2.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0} = store.{0};", name));
            }
        }

        sourceBuilder2.WriteLine("_extraStuff = store._extraStuff;");
        sourceBuilder2.WriteEndBlock();
        sourceBuilder2.WriteEndBlock();

        productionContext.AddSource("TelaniSourceGeneratorAppSettings.g.cs", sourceBuilder2.ToSourceText());
        productionContext.AddSource("TelaniSourceGeneratorIAppSettings.g.cs", sourceBuilder.ToSourceText());
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

    private static void PrintHead(SourceWriter sourceBuilder, string theNamespace)
    {
        sourceBuilder.WriteLine($@"using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

#nullable enable
                
namespace {theNamespace};");
    }

    private static void PrintProperties(SourceWriter sourceBuilder, IEnumerable<PropertyModel> properties)
    {
        foreach (var (type, name, readonlyAttr, doccomment) in properties)
        {
            var isreadonly = readonlyAttr ? "" : "set; ";
            if (!string.IsNullOrEmpty(doccomment) && doccomment is not null)
            {
                // The doccomment has multiple lines, which is not an issue, but the indentation might not be correct.
                // So we split it into lines, trim the whitespace for each line and let SourceWriter handle the indentation.
                Array.ForEach(doccomment.Split('\n'), line => {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        sourceBuilder.WriteLine(line.Trim());
                    }
                });
            }
            sourceBuilder.WriteLine(string.Format(CultureInfo.InvariantCulture, "public {0} {1} {{ get; {2}}}", type, name, isreadonly));
            sourceBuilder.WriteLine();
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
            "Telani.SourceGenerator.AppSettingsAttribute",
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (syntaxContext, _) => ExtractInfo((ClassDeclarationSyntax)syntaxContext.TargetNode, syntaxContext.TargetSymbol.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted))));


        context.RegisterSourceOutput(appSettingsClass.Collect(), ProduceSource);

        context.RegisterPostInitializationOutput(static c =>
        {
            c.AddEmbeddedAttributeDefinition();

            var attribute = Helpers.ReadParameterlessAttributesFile("AppSettingsAttribute", "Class for which the interface should be generated.");
            c.AddSource("TelaniAppSettingsAttribute.g.cs", attribute.ToSourceText());

            var attributeIgnore = Helpers.ReadParameterlessAttributesFile("SettingsIgnoreAttribute", "A property with this attribute will not be added to the generated interface.", AttributeTargets.Property);
            c.AddSource("TelaniSettingsIgnoreAttribute.g.cs", attributeIgnore.ToSourceText());

            var attributeReadOnly = Helpers.ReadParameterlessAttributesFile("SettingsReadOnlyAttribute", "A property with this attribute will be added readonly (only a getter) to the generated interface.", AttributeTargets.Property);
            c.AddSource("TelaniSettingsReadOnlyAttribute.g.cs", attributeReadOnly.ToSourceText());
        });
    }

    private static SettingsClassModel ExtractInfo(ClassDeclarationSyntax s, string namespaceName)
    {
        if (s is null)
        {
            return new(new EquatableArray<PropertyModel>(), "Telani.SourceGenerator");
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
        return new(tempList.ToImmutableArray(), namespaceName);
    }
}
